using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Mangosteen.Rendering.Advanced;

/// <summary>Vertex and face totals; <see cref="Vertices"/> is null when they could not be counted cheaply.</summary>
internal readonly record struct ModelStatistics(long? Vertices, long Faces);

/// <summary>
/// Counts model geometry straight from the file so the viewer can show it while F3D
/// imports, without asking the native engine (its C API exposes no scene statistics).
/// </summary>
internal static class ModelStatisticsReader
{
    private const int BufferSize = 1024 * 1024;
    private const int MaxPlyHeaderBytes = 64 * 1024;
    private const int MaxGltfJsonBytes = 64 * 1024 * 1024;
    // STL repeats each corner per triangle; deduplicating beyond this costs too much memory.
    internal const long MaxStlTrianglesToDeduplicate = 4_000_000;

    /// <param name="counter">Receives the bytes this scan reads, so load progress can exclude them.</param>
    public static ModelStatistics? TryRead(string path, CancellationToken token, ModelReadCounter? counter = null)
    {
        try
        {
            return Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".obj" => ReadObj(path, token, counter),
                ".ply" => ReadPly(path, counter),
                ".stl" => ReadStl(path, token, counter),
                ".gltf" => ReadGltfJson(ReadAllBytes(path, counter)),
                ".glb" => ReadGlb(path, counter),
                _ => null
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or
            InvalidDataException or FormatException or OverflowException or InvalidOperationException or
            KeyNotFoundException or IndexOutOfRangeException)
        {
            return null;
        }
    }

    internal static ModelStatistics ReadObj(string path, CancellationToken token, ModelReadCounter? counter = null)
    {
        using var stream = OpenSequential(path, counter);
        return ReadObj(stream, token);
    }

    internal static ModelStatistics ReadObj(Stream stream, CancellationToken token)
    {
        long vertices = 0;
        long faces = 0;
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            // Keyword bytes seen so far on the current line; -1 once the line is decided.
            var prefixLength = 0;
            var first = (byte)0;
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                token.ThrowIfCancellationRequested();
                var span = buffer.AsSpan(0, read);
                var index = 0;
                while (index < span.Length)
                {
                    if (prefixLength >= 0)
                    {
                        var value = span[index];
                        if (prefixLength == 0)
                        {
                            if (value is (byte)' ' or (byte)'\t' or (byte)'\r')
                            {
                                index++;
                                continue;
                            }

                            first = value;
                            prefixLength = value is (byte)'v' or (byte)'f' ? 1 : -1;
                            index++;
                            if (value == (byte)'\n') prefixLength = 0;
                            continue;
                        }

                        if (value is (byte)' ' or (byte)'\t')
                        {
                            if (first == (byte)'v') vertices++;
                            else faces++;
                        }

                        prefixLength = value == (byte)'\n' ? 0 : -1;
                        index++;
                        continue;
                    }

                    var newline = span[index..].IndexOf((byte)'\n');
                    if (newline < 0) break;
                    index += newline + 1;
                    prefixLength = 0;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return new ModelStatistics(vertices, faces);
    }

    internal static ModelStatistics? ReadPly(string path, ModelReadCounter? counter = null)
    {
        using var stream = OpenSequential(path, counter);
        return ReadPly(stream);
    }

    internal static ModelStatistics? ReadPly(Stream stream)
    {
        var header = new byte[MaxPlyHeaderBytes];
        var length = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
        var text = Encoding.ASCII.GetString(header, 0, length);
        var end = text.IndexOf("end_header", StringComparison.Ordinal);
        if (!text.StartsWith("ply", StringComparison.Ordinal) || end < 0) return null;

        long? vertices = null;
        long faces = 0;
        foreach (var rawLine in text[..end].Split('\n'))
        {
            var parts = rawLine.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3 || parts[0] != "element") continue;
            var count = long.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
            if (parts[1] == "vertex") vertices = count;
            else if (parts[1] == "face") faces = count;
        }

        return vertices is null ? null : new ModelStatistics(vertices, faces);
    }

    internal static ModelStatistics? ReadStl(string path, CancellationToken token, ModelReadCounter? counter = null)
    {
        using var stream = OpenSequential(path, counter);
        return ReadStl(stream, token);
    }

    internal static ModelStatistics? ReadStl(Stream stream, CancellationToken token)
    {
        Span<byte> header = stackalloc byte[84];
        var headerLength = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
        if (headerLength == 84 && stream.CanSeek)
        {
            var triangles = BinaryPrimitives.ReadUInt32LittleEndian(header[80..]);
            // ASCII files also begin with "solid", so the exact binary size is the reliable test.
            if (stream.Length == 84 + 50L * triangles)
            {
                return new ModelStatistics(
                    triangles <= MaxStlTrianglesToDeduplicate ? CountBinaryStlVertices(stream, triangles, token) : null,
                    triangles);
            }
        }

        stream.Position = 0;
        return CountAsciiStl(stream, token);
    }

    private static long CountBinaryStlVertices(Stream stream, long triangles, CancellationToken token)
    {
        stream.Position = 84;
        var unique = new HashSet<StlVertex>((int)Math.Min(triangles * 3 / 2, int.MaxValue / 2));
        var record = new byte[50 * 4096];
        var remaining = triangles;
        while (remaining > 0)
        {
            token.ThrowIfCancellationRequested();
            var count = (int)Math.Min(remaining, 4096);
            stream.ReadExactly(record, 0, count * 50);
            for (var i = 0; i < count; i++)
            {
                var triangle = record.AsSpan(i * 50, 50);
                for (var corner = 0; corner < 3; corner++)
                {
                    var offset = 12 + corner * 12;
                    unique.Add(new StlVertex(
                        BinaryPrimitives.ReadInt32LittleEndian(triangle[offset..]),
                        BinaryPrimitives.ReadInt32LittleEndian(triangle[(offset + 4)..]),
                        BinaryPrimitives.ReadInt32LittleEndian(triangle[(offset + 8)..])));
                }
            }
            remaining -= count;
        }

        return unique.Count;
    }

    private static ModelStatistics? CountAsciiStl(Stream stream, CancellationToken token)
    {
        using var reader = new StreamReader(stream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, BufferSize);
        long faces = 0;
        HashSet<(float, float, float)>? unique = new();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            token.ThrowIfCancellationRequested();
            var trimmed = line.AsSpan().TrimStart();
            if (trimmed.StartsWith("facet", StringComparison.Ordinal))
            {
                faces++;
                if (faces > MaxStlTrianglesToDeduplicate) unique = null;
            }
            else if (unique is not null && trimmed.StartsWith("vertex", StringComparison.Ordinal))
            {
                var parts = trimmed[6..].ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 3) return null;
                var culture = System.Globalization.CultureInfo.InvariantCulture;
                unique.Add((float.Parse(parts[0], culture), float.Parse(parts[1], culture), float.Parse(parts[2], culture)));
            }
        }

        return faces == 0 ? null : new ModelStatistics(unique?.Count, faces);
    }

    private static ModelStatistics? ReadGlb(string path, ModelReadCounter? counter)
    {
        using var stream = OpenSequential(path, counter);
        Span<byte> header = stackalloc byte[20];
        stream.ReadExactly(header);
        // "glTF" magic, then a JSON chunk header.
        if (BinaryPrimitives.ReadUInt32LittleEndian(header) != 0x46546C67 ||
            BinaryPrimitives.ReadUInt32LittleEndian(header[16..]) != 0x4E4F534A)
        {
            return null;
        }

        var jsonLength = BinaryPrimitives.ReadUInt32LittleEndian(header[12..]);
        if (jsonLength > MaxGltfJsonBytes) return null;
        var json = new byte[jsonLength];
        stream.ReadExactly(json);
        return ReadGltfJson(json);
    }

    internal static ModelStatistics? ReadGltfJson(ReadOnlySpan<byte> json)
    {
        if (json.Length > MaxGltfJsonBytes) return null;
        if (json.StartsWith(Encoding.UTF8.Preamble)) json = json[3..];
        var reader = new Utf8JsonReader(json, new JsonReaderOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (!root.TryGetProperty("meshes", out var meshes) || meshes.ValueKind != JsonValueKind.Array) return null;
        root.TryGetProperty("accessors", out var accessors);

        var meshTotals = new (long Vertices, long Faces)[meshes.GetArrayLength()];
        var meshIndex = 0;
        foreach (var mesh in meshes.EnumerateArray())
        {
            long vertices = 0, faces = 0;
            if (mesh.TryGetProperty("primitives", out var primitives))
            {
                foreach (var primitive in primitives.EnumerateArray())
                {
                    var positions = primitive.GetProperty("attributes").TryGetProperty("POSITION", out var position)
                        ? GetAccessorCount(accessors, position)
                        : 0;
                    var elements = primitive.TryGetProperty("indices", out var indices)
                        ? GetAccessorCount(accessors, indices)
                        : positions;
                    var mode = primitive.TryGetProperty("mode", out var modeValue) ? modeValue.GetInt32() : 4;
                    vertices += positions;
                    faces += mode switch
                    {
                        4 => elements / 3,
                        5 or 6 => Math.Max(0, elements - 2),
                        _ => 0
                    };
                }
            }
            meshTotals[meshIndex++] = (vertices, faces);
        }

        // Count what the default scene draws, so instanced meshes are counted per instance.
        var instances = CountMeshInstances(root, meshTotals.Length);
        long totalVertices = 0, totalFaces = 0;
        for (var i = 0; i < meshTotals.Length; i++)
        {
            totalVertices += meshTotals[i].Vertices * instances[i];
            totalFaces += meshTotals[i].Faces * instances[i];
        }

        return new ModelStatistics(totalVertices, totalFaces);
    }

    private static long[] CountMeshInstances(JsonElement root, int meshCount)
    {
        var instances = new long[meshCount];
        if (!root.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array ||
            !root.TryGetProperty("scenes", out var scenes) || scenes.ValueKind != JsonValueKind.Array ||
            scenes.GetArrayLength() == 0)
        {
            Array.Fill(instances, 1);
            return instances;
        }

        var sceneIndex = root.TryGetProperty("scene", out var defaultScene) ? defaultScene.GetInt32() : 0;
        var nodeList = nodes.EnumerateArray().ToArray();
        var pending = new Stack<(int Node, int Depth)>();
        if (scenes[sceneIndex].TryGetProperty("nodes", out var sceneNodes))
        {
            foreach (var node in sceneNodes.EnumerateArray()) pending.Push((node.GetInt32(), 0));
        }

        while (pending.Count > 0)
        {
            var (nodeIndex, depth) = pending.Pop();
            // glTF forbids cycles; the depth cap keeps malformed files from looping forever.
            if (depth > 256) continue;
            var node = nodeList[nodeIndex];
            if (node.TryGetProperty("mesh", out var mesh)) instances[mesh.GetInt32()]++;
            if (node.TryGetProperty("children", out var children))
            {
                foreach (var child in children.EnumerateArray()) pending.Push((child.GetInt32(), depth + 1));
            }
        }

        return instances;
    }

    private static long GetAccessorCount(JsonElement accessors, JsonElement index)
    {
        return accessors[index.GetInt32()].GetProperty("count").GetInt64();
    }

    private static byte[] ReadAllBytes(string path, ModelReadCounter? counter)
    {
        var bytes = File.ReadAllBytes(path);
        counter?.Add(bytes.Length);
        return bytes;
    }

    private static Stream OpenSequential(string path, ModelReadCounter? counter)
    {
        // No FileStream buffer: the readers use large buffers, and bytes are counted as read.
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan);
        return counter is null ? stream : new CountingStream(stream, counter);
    }

    private sealed class CountingStream(Stream inner, ModelReadCounter counter) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }

        public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));
        public override int Read(Span<byte> buffer) => Count(inner.Read(buffer));
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }

        private int Count(int read)
        {
            counter.Add(read);
            return read;
        }
    }

    private readonly record struct StlVertex(int X, int Y, int Z);
}
