namespace ClassicPhotoViewer;

internal sealed class LoadSession
{
    private readonly object _gate = new();
    private int _referenceCount = 1;
    private bool _disposeWhenInactive;
    private bool _disposed;

    public LoadSession(int generation)
    {
        Generation = generation;
        Source = new CancellationTokenSource();
    }

    public int Generation { get; }

    private CancellationTokenSource Source { get; }

    public CancellationToken Token => Source.Token;

    internal bool IsDisposed
    {
        get
        {
            lock (_gate)
            {
                return _disposed;
            }
        }
    }

    public bool TryAddReference()
    {
        lock (_gate)
        {
            if (_disposed || _disposeWhenInactive)
            {
                return false;
            }

            _referenceCount++;
            return true;
        }
    }

    public void Release()
    {
        var shouldDispose = false;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _referenceCount--;
            if (_referenceCount <= 0)
            {
                _disposed = true;
                shouldDispose = true;
            }
        }

        if (shouldDispose)
        {
            Source.Dispose();
        }
    }

    public void CancelAndDisposeWhenInactive()
    {
        var shouldDispose = false;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposeWhenInactive = true;
            shouldDispose = _referenceCount <= 0;
            if (shouldDispose)
            {
                _disposed = true;
            }
        }

        try
        {
            Source.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        if (shouldDispose)
        {
            Source.Dispose();
        }
    }
}
