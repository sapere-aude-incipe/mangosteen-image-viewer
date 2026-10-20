using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace Mangosteen;

internal enum ActivationSendResult
{
    Delivered,
    Rejected,
    Failed,
}

internal sealed class AppInstanceCoordinator : IDisposable
{
    private const string DefaultInstanceName = "Mangosteen.ImageViewer.5505BFA7-AFF8-4C6E-8B60-52EDF84880D3";
    private const int MaximumMessageBytes = 32 * 1024;
    private static readonly TimeSpan ClientConnectTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ServerRequestTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ClientResultTimeout = TimeSpan.FromSeconds(30);
    private const byte RequestAccepted = 1;
    private const byte RequestRejected = 0;

    // Named mutexes are re-entrant for their owning thread, so ownership taken by
    // another coordinator in this process must not be mistaken for a fresh acquire.
    private static readonly HashSet<string> s_ownedInstanceNames = new(StringComparer.Ordinal);

    private readonly Mutex _instanceMutex;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _serverCts = new();
    private Task? _serverTask;
    private bool _ownsMutex;
    private bool _disposed;

    public AppInstanceCoordinator()
        : this(GetCurrentInstanceName())
    {
    }

    internal AppInstanceCoordinator(string instanceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceName);
        // Holding ownership for the lifetime of the primary process keeps the
        // kernel object authoritative across self-contained app hosts as well.
        _instanceMutex = new Mutex(initiallyOwned: true, $@"Local\{instanceName}", out var createdNew);
        _pipeName = instanceName;
        // Another process may still hold a handle to a mutex the previous primary
        // released while exiting, so the name existing does not mean it is owned.
        _ownsMutex = TryClaimOwnership(createdNew);
        IsPrimaryInstance = _ownsMutex;
    }

    public bool IsPrimaryInstance { get; }

    private bool TryClaimOwnership(bool createdNew)
    {
        lock (s_ownedInstanceNames)
        {
            if (s_ownedInstanceNames.Contains(_pipeName))
            {
                if (createdNew) _instanceMutex.ReleaseMutex();
                return false;
            }

            if (!createdNew && !TryAcquireMutex(_instanceMutex))
            {
                return false;
            }

            s_ownedInstanceNames.Add(_pipeName);
            return true;
        }
    }

    private void ForgetOwnership()
    {
        lock (s_ownedInstanceNames)
        {
            s_ownedInstanceNames.Remove(_pipeName);
        }
    }

    private static bool TryAcquireMutex(Mutex mutex)
    {
        try
        {
            return mutex.WaitOne(TimeSpan.Zero);
        }
        catch (AbandonedMutexException)
        {
            return true;
        }
    }

    private static string GetCurrentInstanceName()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return GetInstanceName(AppContext.BaseDirectory, identity.User?.Value ?? identity.Name);
    }

    internal static string GetInstanceName(string directory, string userIdentity)
    {
        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)).ToUpperInvariant();
        var identity = Encoding.UTF8.GetBytes(userIdentity + "\n" + path);
        return DefaultInstanceName + "." + Convert.ToHexStringLower(SHA256.HashData(identity));
    }

    /// <summary>
    /// Starts accepting activations. The handler returns false to refuse a request
    /// (for example while exiting) so the sender can take over instead of losing it.
    /// </summary>
    public void StartServer(Func<AppActivationRequest, Task<bool>> requestHandler)
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

    public async Task<ActivationSendResult> TrySendAsync(AppActivationRequest request, CancellationToken token = default)
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

            // The primary answers only after its handler has taken the request, so a
            // request it cannot handle any more is never reported as delivered.
            timeoutCts.CancelAfter(ClientResultTimeout);
            var result = new byte[1];
            await client.ReadExactlyAsync(result, timeoutCts.Token).ConfigureAwait(false);
            return result[0] == RequestAccepted ? ActivationSendResult.Delivered : ActivationSendResult.Rejected;
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or UnauthorizedAccessException)
        {
            Trace.TraceWarning($"Failed to contact the running Mangosteen instance: {ex}");
            return ActivationSendResult.Failed;
        }
    }

    /// <summary>
    /// Stops accepting activations and gives up the single-instance lock so a new
    /// launch becomes the primary instance while this one finishes exiting. Must be
    /// called on the thread that created the coordinator.
    /// </summary>
    public void ReleasePrimary()
    {
        if (_disposed)
        {
            return;
        }

        _serverCts.Cancel();
        if (!_ownsMutex)
        {
            return;
        }

        _ownsMutex = false;
        ForgetOwnership();
        try
        {
            _instanceMutex.ReleaseMutex();
        }
        catch (ApplicationException ex)
        {
            Trace.TraceWarning($"Failed to release the Mangosteen instance lock: {ex}");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_ownsMutex)
        {
            _ownsMutex = false;
            ForgetOwnership();
        }

        _serverCts.Cancel();
        _serverCts.Dispose();
        _instanceMutex.Dispose();
    }

    private async Task RunServerAsync(
        Func<AppActivationRequest, Task<bool>> requestHandler,
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

                using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                requestCts.CancelAfter(ServerRequestTimeout);
                var request = await ReadRequestAsync(server, requestCts.Token).ConfigureAwait(false);
                var processIdBuffer = new byte[sizeof(int)];
                BinaryPrimitives.WriteInt32LittleEndian(processIdBuffer, Environment.ProcessId);
                await server.WriteAsync(processIdBuffer, requestCts.Token).ConfigureAwait(false);
                await server.FlushAsync(requestCts.Token).ConfigureAwait(false);

                var acknowledgement = new byte[1];
                await server.ReadExactlyAsync(acknowledgement, requestCts.Token).ConfigureAwait(false);
                var accepted = await requestHandler(request).ConfigureAwait(false);
                // The handler has already acted on the request, so report it even if
                // the request timeout or shutdown fired meanwhile.
                await server.WriteAsync(
                    new[] { accepted ? RequestAccepted : RequestRejected },
                    CancellationToken.None).ConfigureAwait(false);
                await server.FlushAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (OperationCanceledException ex)
            {
                Trace.TraceWarning($"Mangosteen activation pipe request timed out: {ex}");
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
