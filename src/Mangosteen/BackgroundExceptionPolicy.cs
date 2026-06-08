namespace Mangosteen;

internal static class BackgroundExceptionPolicy
{
    public static bool IsExpectedShutdownOrCancellation(
        Exception exception,
        bool isClosing,
        CancellationToken token = default)
    {
        return exception is OperationCanceledException ||
            exception is ObjectDisposedException && (isClosing || token.IsCancellationRequested);
    }
}
