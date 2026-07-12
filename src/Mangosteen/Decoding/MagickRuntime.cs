using ImageMagick;

namespace Mangosteen.Decoding;

internal static class MagickRuntime
{
    private static readonly Lazy<bool> NativeRuntime = new(
        static () =>
        {
            MagickNET.Initialize();
            return true;
        },
        LazyThreadSafetyMode.ExecutionAndPublication);

    public static SemaphoreSlim OperationGate { get; } = new(1, 1);

    public static void EnsureInitialized()
    {
        _ = NativeRuntime.Value;
    }
}
