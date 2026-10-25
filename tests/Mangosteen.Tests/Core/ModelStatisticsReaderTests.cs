using Mangosteen.Rendering.Advanced;
using System.Buffers.Binary;
using System.Text;

namespace Mangosteen.Tests.Core;

[TestClass]
public sealed class ModelStatisticsReaderTests
{
    [TestMethod]
    public void Obj_Counts_Vertex_And_Face_Lines_Only()
    {
        const string obj = """
            # comment with v and f
            mtllib model.mtl
            v 0 0 0
            v 1 0 0
              v 0 1 0
            v	0 0 1
            vt 0 0
            vn 0 0 1
            usemtl skin
            f 1/1/1 2/1/1 3/1/1
            f 1 3 4
            fo not-a-face
            """;

        var statistics = ModelStatisticsReader.ReadObj(ToStream(obj.ReplaceLineEndings("\r\n")), CancellationToken.None);

        Assert.AreEqual(new ModelStatistics(4, 2), statistics);
    }

    [TestMethod]
    public void Obj_Counts_Lines_Split_Across_Read_Buffers()
    {
        var builder = new StringBuilder();
        // Enough lines to cross the 1 MiB read buffer several times at varying offsets.
        for (var i = 0; i < 120_000; i++)
        {
            builder.Append("v ").Append(i).Append(".25 1.5 -2.75\n");
            if (i % 3 == 2) builder.Append("f ").Append(i - 1).Append(' ').Append(i).Append(' ').Append(i + 1).Append('\n');
        }

        var statistics = ModelStatisticsReader.ReadObj(ToStream(builder.ToString()), CancellationToken.None);

        Assert.AreEqual(new ModelStatistics(120_000, 40_000), statistics);
    }

    [TestMethod]
    public void Obj_Honors_Cancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsExactly<OperationCanceledException>(() =>
            ModelStatisticsReader.ReadObj(ToStream("v 0 0 0\n"), cts.Token));
    }

    [TestMethod]
    public void Ply_Reads_Element_Counts_From_Header()
    {
        const string ply = "ply\nformat binary_little_endian 1.0\ncomment made by test\nelement vertex 3210620\nproperty float x\nelement face 6421238\nproperty list uchar int vertex_indices\nend_header\n";

        Assert.AreEqual(new ModelStatistics(3_210_620, 6_421_238), ModelStatisticsReader.ReadPly(ToStream(ply)));
    }

    [TestMethod]
    public void Ply_Without_Header_End_Is_Unknown()
    {
        Assert.IsNull(ModelStatisticsReader.ReadPly(ToStream("ply\nelement vertex 3\n")));
    }

    [TestMethod]
    public void Binary_Stl_Counts_Triangles_And_Shared_Corners()
    {
        // Two triangles of a unit square share two corners.
        var stl = CreateBinaryStl(
            [(0, 0, 0), (1, 0, 0), (1, 1, 0)],
            [(0, 0, 0), (1, 1, 0), (0, 1, 0)]);

        Assert.AreEqual(new ModelStatistics(4, 2), ModelStatisticsReader.ReadStl(new MemoryStream(stl), CancellationToken.None));
    }

    [TestMethod]
    public void Binary_Stl_Starting_With_Solid_Is_Still_Binary()
    {
        var stl = CreateBinaryStl([(0, 0, 0), (1, 0, 0), (0, 1, 0)]);
        Encoding.ASCII.GetBytes("solid exported").CopyTo(stl, 0);

        Assert.AreEqual(new ModelStatistics(3, 1), ModelStatisticsReader.ReadStl(new MemoryStream(stl), CancellationToken.None));
    }

    [TestMethod]
    public void Ascii_Stl_Counts_Facets_And_Unique_Vertices()
    {
        const string stl = """
            solid square
              facet normal 0 0 1
                outer loop
                  vertex 0 0 0
                  vertex 1 0 0
                  vertex 1 1 0
                endloop
              endfacet
              facet normal 0 0 1
                outer loop
                  vertex 0 0 0
                  vertex 1 1 0
                  vertex 0 1 0
                endloop
              endfacet
            endsolid square
            """;

        Assert.AreEqual(new ModelStatistics(4, 2), ModelStatisticsReader.ReadStl(ToStream(stl), CancellationToken.None));
    }

    [TestMethod]
    public void Gltf_Counts_Primitives_Per_Scene_Instance()
    {
        const string gltf = """
            {
              "scene": 0,
              "scenes": [{ "nodes": [0, 1] }],
              "nodes": [{ "mesh": 0 }, { "children": [2] }, { "mesh": 0 }, { "mesh": 1 }],
              "meshes": [
                { "primitives": [{ "attributes": { "POSITION": 0 }, "indices": 1 }] },
                { "primitives": [{ "attributes": { "POSITION": 2 }, "mode": 5 }] }
              ],
              "accessors": [{ "count": 8 }, { "count": 36 }, { "count": 10 }]
            }
            """;

        // Mesh 0 is drawn twice; mesh 1 is not in the scene.
        Assert.AreEqual(new ModelStatistics(16, 24), ModelStatisticsReader.ReadGltfJson(Encoding.UTF8.GetBytes(gltf)));
    }

    [TestMethod]
    public void Gltf_Without_Scenes_Counts_Each_Mesh_Once()
    {
        const string gltf = """
            {
              "meshes": [{ "primitives": [{ "attributes": { "POSITION": 0 } }] }],
              "accessors": [{ "count": 9 }]
            }
            """;

        Assert.AreEqual(new ModelStatistics(9, 3), ModelStatisticsReader.ReadGltfJson(Encoding.UTF8.GetBytes(gltf)));
    }

    [TestMethod]
    public void TryRead_Returns_Null_For_Corrupt_Or_Missing_Files()
    {
        var directory = Directory.CreateTempSubdirectory("mangosteen-model-stats-");
        try
        {
            var gltf = Path.Combine(directory.FullName, "broken.gltf");
            File.WriteAllText(gltf, "{ \"meshes\": [ { \"primitives\": [ { \"attributes\": { \"POSITION\": 4 } } ] } ] }");

            Assert.IsNull(ModelStatisticsReader.TryRead(gltf, CancellationToken.None));
            Assert.IsNull(ModelStatisticsReader.TryRead(Path.Combine(directory.FullName, "missing.obj"), CancellationToken.None));
            Assert.IsNull(ModelStatisticsReader.TryRead(Path.Combine(directory.FullName, "image.png"), CancellationToken.None));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void TryRead_Reads_Glb_Json_Chunk()
    {
        var json = Encoding.UTF8.GetBytes("""{"meshes":[{"primitives":[{"attributes":{"POSITION":0},"indices":1}]}],"accessors":[{"count":4},{"count":6}]}  """);
        var glb = new byte[20 + json.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(glb, 0x46546C67);
        BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(8), (uint)glb.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(12), (uint)json.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(16), 0x4E4F534A);
        json.CopyTo(glb, 20);
        var path = Path.Combine(Path.GetTempPath(), $"mangosteen-{Guid.NewGuid():N}.glb");
        try
        {
            File.WriteAllBytes(path, glb);

            Assert.AreEqual(new ModelStatistics(4, 2), ModelStatisticsReader.TryRead(path, CancellationToken.None));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void TryRead_Counts_The_Bytes_It_Reads()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mangosteen-{Guid.NewGuid():N}.obj");
        try
        {
            File.WriteAllText(path, string.Concat(Enumerable.Repeat("v 0.125 1.5 -2.75\n", 50_000)) + "f 1 2 3\n");
            var counter = new ModelReadCounter();

            Assert.AreEqual(new ModelStatistics(50_000, 1), ModelStatisticsReader.TryRead(path, CancellationToken.None, counter));
            Assert.AreEqual(new FileInfo(path).Length, counter.BytesRead);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void Load_Progress_Excludes_The_Viewers_Own_Reads()
    {
        const long megabyte = 1024 * 1024;
        // 845 MB file: the statistics scan read all of it, F3D has read 405 MB so far.
        var progress = ModelLoadProgress.Estimate(1_000 * megabyte, 2_250 * megabyte, 845 * megabyte, 845 * megabyte);

        Assert.AreEqual(405 * megabyte, progress.BytesRead);
        Assert.AreEqual(405d / 845, progress.Fraction, 1e-9);
        Assert.IsFalse(progress.IsPreparing);
        Assert.IsTrue(ModelLoadProgress.Estimate(0, 900 * megabyte, 0, 845 * megabyte).IsPreparing, "Extra reads clamp to complete.");
        Assert.AreEqual(0, ModelLoadProgress.Estimate(500, 400, 0, megabyte).BytesRead, "Counter skew never goes negative.");
    }

    [TestMethod]
    public void Process_Read_Counter_Advances_When_Reading_A_File()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mangosteen-{Guid.NewGuid():N}.bin");
        try
        {
            File.WriteAllBytes(path, new byte[4 * 1024 * 1024]);
            var before = ModelLoadProgress.TryGetProcessBytesRead();
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.None))
            {
                stream.ReadExactly(new byte[4 * 1024 * 1024]);
            }
            var after = ModelLoadProgress.TryGetProcessBytesRead();

            Assert.IsNotNull(before);
            Assert.IsNotNull(after);
            Assert.IsGreaterThanOrEqualTo(4L * 1024 * 1024, after.Value - before.Value);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static MemoryStream ToStream(string text) => new(Encoding.ASCII.GetBytes(text));

    private static byte[] CreateBinaryStl(params (float X, float Y, float Z)[][] triangles)
    {
        var bytes = new byte[84 + 50 * triangles.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(80), (uint)triangles.Length);
        for (var i = 0; i < triangles.Length; i++)
        {
            for (var corner = 0; corner < 3; corner++)
            {
                var offset = 84 + i * 50 + 12 + corner * 12;
                var (x, y, z) = triangles[i][corner];
                BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset), x);
                BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset + 4), y);
                BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset + 8), z);
            }
        }

        return bytes;
    }
}
