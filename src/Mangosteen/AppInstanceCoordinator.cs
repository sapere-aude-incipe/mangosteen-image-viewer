using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Mangosteen;

internal sealed class AppInstanceCoordinator : IDisposable
{
    private const string DefaultInstanceName = "Mangosteen.ImageViewer.5505BFA7-AFF8-4C6E-8B60-52EDF84880D3";
    private const int MaximumMessageBytes = 32 * 1024;
    private static readonly TimeSpan ClientConnectTimeout = TimeSpan.FromSeconds(5);

    private readonly Mutex _instanceMutex;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _serverCts = new();
    private Task? _serverTask;
    private bool _disposed;

    public AppInstanceCoordinator()
        : this(DefaultInstanceName)
    {
    }

    internal AppInstanceCoordinator(string instanceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceName);
        // Holding ownership for the lifetime of the primary process keeps the
        // kernel object authoritative across self-contained app hosts as well.
        _instanceMutex = new Mutex(initiallyOwned: true, $@"Local\{instanceName}", out var createdNew);
        _pipeName = instanceName;
        IsPrimaryInstance = createdNew;
    }

    public bool IsPrimaryInstance { get; }

    public void StartServer(Func<AppActivationRequest, Task> requestHandler)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(requestHandler);

        if (!IsPrimaryInstance)
        {
            throw new InvalidOperationException("Only the primary application instance can host the activation pipe.");
        }

        if (_serverTask is not null)
        {
            throw new InvalidOperationException("The activation pipe is already running.");
        }

        _serverTask = Task.Run(() => RunServerAsync(requestHandler, _serverCts.Token));
    }

    public async Task<bool> TrySendAsync(AppActivationRequest request, CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(request);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeoutCts.CancelAfter(ClientConnectTimeout);

        try
        {
            await using var client = new NamedPipeClientStream(
                ".",
                _pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);
            await client.ConnectAsync(timeoutCts.Token).ConfigureAwait(false);
            await WriteRequestAsync(client, request, timeoutCts.Token).ConfigureAwait(false);

            var processIdBuffer = new byte[sizeof(int)];
            await client.ReadExactlyAsync(processIdBuffer, timeoutCts.Token).ConfigureAwait(false);
            var serverProcessId = BinaryPrimitives.ReadInt32LittleEndian(processIdBuffer);
            if (serverProcessId > 0)
            {
                _ = AllowSetForegroundWindow((uint)serverProcessId);
            }

            await client.WriteAsync(new byte[] { 1 }, timeoutCts.Token).ConfigureAwait(false);
            await client.FlushAsync(timeoutCts.Token).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or UnauthorizedAccessException)
        {
            Trace.TraceWarning($"Failed to contact the running Mangosteen instance: {ex}");
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _serverCts.Cancel();
        _serverCts.Dispose();
        _instanceMutex.Dispose();
    }

    private async Task RunServerAsync(
        Func<AppActivationRequest, Task> requestHandler,
        CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(token).ConfigureAwait(false);

                var request = await ReadRequestAsync(server, token).ConfigureAwait(false);
                var processIdBuffer = new byte[sizeof(int)];
                BinaryPrimitives.WriteInt32LittleEndian(processIdBuffer, Environment.ProcessId);
                await server.WriteAsync(processIdBuffer, token).ConfigureAwait(false);
                await server.FlushAsync(token).ConfigureAwait(false);

                var acknowledgement = new byte[1];
                await server.ReadExactlyAsync(acknowledgement, token).ConfigureAwait(false);
                await requestHandler(request).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or InvalidDataException)
            {
                Trace.TraceWarning($"Mangosteen activation pipe request failed: {ex}");
            }
            catch (Exception ex)
            {
                Trace.TraceError($"Mangosteen activation handler failed: {ex}");
            }
        }
    }

    private static async Task WriteRequestAsync(
        Stream stream,
        AppActivationRequest request,
        CancellationToken token)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            request,
            AppInstanceJsonContext.Default.AppActivationRequest);
        if (payload.Length > MaximumMessageBytes)
        {
            throw new InvalidDataException("The activation request is too large.");
        }

        var lengthBuffer = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(lengthBuffer, payload.Length);
        await stream.WriteAsync(lengthBuffer, token).ConfigureAwait(false);
        await stream.WriteAsync(payload, token).ConfigureAwait(false);
        await stream.FlushAsync(token).ConfigureAwait(false);
    }

    private static async Task<AppActivationRequest> ReadRequestAsync(
        Stream stream,
        CancellationToken token)
    {
        var lengthBuffer = new byte[sizeof(int)];
        await stream.ReadExactlyAsync(lengthBuffer, token).ConfigureAwait(false);
        var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(lengthBuffer);
        if (payloadLength is <= 0 or > MaximumMessageBytes)
        {
            throw new InvalidDataException("The activation request length is invalid.");
        }

        var payload = new byte[payloadLength];
        await stream.ReadExactlyAsync(payload, token).ConfigureAwait(false);
        return JsonSerializer.Deserialize(
                payload,
                AppInstanceJsonContext.Default.AppActivationRequest)
            ?? throw new InvalidDataException("The activation request is empty.");
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint processId);
}
