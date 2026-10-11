using System.IO;
using Mangosteen.Decoding;
using Mangosteen.Localization;
using Mangosteen.Printing;

namespace Mangosteen;

public partial class MainWindow
{
    private async Task PrintCurrentImageAsync()
    {
        if (_isPrinting || !CanPrintCurrentImage() || _image is null)
        {
            UpdateNavigationButtons();
            return;
        }

        _isPrinting = true;
        UpdateNavigationButtons();
        using var cancellation = new CancellationTokenSource();
        _printCts = cancellation;
        var generation = _loadGeneration;
        var originalPath = _navigator.CurrentPath!;
        var preparing = false;
        try
        {
            var path = originalPath;
            if (!PhotoPrintService.CanPrintOriginal(path, _pendingRotationQuarterTurns, _image.Frames.Count))
            {
                preparing = true;
                ShowStatus(LocalizedText.Get(LocalizedText.Loading));
                var snapshot = await ImagePrintPreparation.PrepareAsync(
                    _image, _frameIndex, _pendingRotationQuarterTurns, DecodeForPrintingAsync,
                    GetInteractiveDecodedByteLimit(), cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                if (_isClosing || generation != _loadGeneration ||
                    !string.Equals(originalPath, _navigator.CurrentPath, StringComparison.OrdinalIgnoreCase)) return;
                path = PhotoPrintService.CreatePrintCopy(snapshot.Source, snapshot.QuarterTurns);
            }

            PhotoPrintService.Show(path);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(_printCts, cancellation)) _printCts = null;
            _isPrinting = false;
            if (preparing && !_isClosing && generation == _loadGeneration) HideStatus();
            if (!_isClosing) UpdateNavigationButtons();
        }
    }

    private async Task<DecodedImage> DecodeForPrintingAsync(ImageDecodeRequest request, CancellationToken token)
    {
        await _fullDecodeGate.WaitAsync(token);
        try { return await Decoders.DecodeAsync(request, token); }
        finally { _fullDecodeGate.Release(); }
    }

    internal static PrintSnapshot CapturePrintSnapshot(DecodedImage image, int frameIndex, int quarterTurns, string jobName)
    {
        return ImagePrintPreparation.Capture(image, frameIndex, quarterTurns, jobName);
    }
}
