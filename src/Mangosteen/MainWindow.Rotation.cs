using System.IO;
using System.Windows;
using Mangosteen.Dialogs;
using Mangosteen.Editing;
using Mangosteen.Localization;

namespace Mangosteen;

public partial class MainWindow
{
    internal async Task ApplyRotationWithPromptsAsync(Func<string, bool> confirm,
        Func<RotationWriteRisk, RotationSaveMode?> chooseSaveMode, Func<string, bool> confirmOverwrite)
    {
        if (!HasPendingRotation || !CanRotateCurrentImage())
        {
            UpdateRotationControls();
            return;
        }

        var path = _navigator.CurrentPath!;
        var turns = _pendingRotationQuarterTurns;
        var openGeneration = _openGeneration;
        _isApplyingRotation = true;
        var rotationSaveCts = new CancellationTokenSource();
        var rotationSaveToken = rotationSaveCts.Token;
        _rotationSaveCts = rotationSaveCts;
        UpdateNavigationButtons();
        var prepared = false;

        try
        {
            if (!confirm(path)) return;
            var risk = await _rotationService.GetWriteRiskAsync(path, rotationSaveToken);
            rotationSaveToken.ThrowIfCancellationRequested();
            if (!IsCurrentRotation()) return;
            var choice = chooseSaveMode(risk);
            if (choice is not { } saveMode) return;
            if (saveMode == RotationSaveMode.PngCopy)
            {
                var copyPath = ImageRotationService.GetDefaultPngCopyPath(path);
                if (File.Exists(copyPath) && !confirmOverwrite(copyPath)) return;
            }
            rotationSaveToken.ThrowIfCancellationRequested();
            if (!IsCurrentRotation()) return;
            PrepareForRotationWrite(path);
            prepared = true;
            ShowStatus(LocalizedText.Get(LocalizedText.ApplyingRotation));
            var outputPath = await _rotationService.RotateAsync(
                path,
                turns,
                saveMode,
                rotationSaveToken);
            rotationSaveToken.ThrowIfCancellationRequested();
            if (!IsCurrentRotation()) return;

            _autoRefreshTimer.Stop();
            _pendingRotationQuarterTurns = 0;
            RemovePreloadedImage(path);
            ClearImage();
            if (saveMode == RotationSaveMode.PngCopy)
            {
                await OpenPathAsync(outputPath);
            }
            else
            {
                await LoadCurrentImageAsync(fitToWindow: true);
            }
        }
        catch (OperationCanceledException) when (rotationSaveToken.IsCancellationRequested)
        {
            if (prepared && IsCurrentRotation())
            {
                HideStatus();
                ResumeLoadingAfterRotationWrite();
            }
        }
        catch (Exception ex)
        {
            TraceBackgroundError("Image rotation failed", ex);
            if (IsCurrentRotation())
            {
                HideStatus();
                if (prepared) ResumeLoadingAfterRotationWrite();
                MessageBox.Show(
                    this,
                    LocalizedText.Format(LocalizedText.RotationFailedFormat, ex.Message),
                    LocalizedText.Get(LocalizedText.RotationDialogTitle),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        finally
        {
            rotationSaveCts.Dispose();
            if (ReferenceEquals(_rotationSaveCts, rotationSaveCts))
            {
                _rotationSaveCts = null;
            }
            _isApplyingRotation = false;
            if (!_isClosing) UpdateNavigationButtons();
        }

        bool IsCurrentRotation() => !_isClosing && _openGeneration == openGeneration &&
            string.Equals(path, _navigator.CurrentPath, StringComparison.OrdinalIgnoreCase) &&
            _pendingRotationQuarterTurns == turns;
    }

    private void PrepareForRotationWrite(string path)
    {
        StopCurrentImageInteraction();
        _loadGeneration++;
        _setActualPixelsAfterFullLoad = false;
        _loadSession?.CancelAndDisposeWhenInactive();
        _loadSession = null;
        CancelPreloadWorker();
        CancelBackgroundFullWarmups();
        RemovePreloadedImage(path);
    }

    // PrepareForRotationWrite cancelled the full-resolution decode and preloads;
    // a write that did not complete must not leave the image stuck as a preview.
    private void ResumeLoadingAfterRotationWrite()
    {
        if (!RestartFullResolutionLoadForCurrentPreview(forceDeferred: _isCurrentPreviewAwaitingFullResolution))
        {
            ScheduleSmartPreloads(allowFullPreloads: _image?.IsFullResolution == true);
        }
    }

    private void UpdateRotationControls()
    {
        var canRotate = CanRotateCurrentImage();
        RotateLeftButton.IsEnabled = canRotate;
        RotateRightButton.IsEnabled = canRotate;
        RotationActionsDock.Visibility = HasPendingRotation && _image is not null
            ? Visibility.Visible
            : Visibility.Collapsed;
        ResetRotationButton.IsEnabled = HasPendingRotation && !_isApplyingRotation && !_isPrinting;
        ApplyRotationButton.IsEnabled = HasPendingRotation && canRotate;
        OpenMenuItem.IsEnabled = !_isApplyingRotation;
    }

    private bool CanRotateCurrentImage()
    {
        return !_isApplyingRotation && !_isPrinting &&
            _contentMode == ViewerContentMode.Image &&
            _image is not null &&
            IsDisplayedImageCurrent() &&
            _navigator.CurrentPath is { } path &&
            File.Exists(path);
    }

    private Task ApplyPendingRotationAsync() => ApplyRotationWithPromptsAsync(
        path => MessageBox.Show(this,
            LocalizedText.Format(LocalizedText.ApplyRotationConfirmationFormat, Path.GetFileName(path)),
            LocalizedText.Get(LocalizedText.RotationDialogTitle), MessageBoxButton.YesNo,
            MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes,
        ChooseRotationSaveMode,
        path => MessageBox.Show(this,
            LocalizedText.Format(LocalizedText.RotatedCopyExistsFormat, Path.GetFileName(path)),
            LocalizedText.Get(LocalizedText.RotationDialogTitle), MessageBoxButton.YesNo,
            MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes);

    private RotationSaveMode? ChooseRotationSaveMode(RotationWriteRisk risk)
    {
        if (risk == RotationWriteRisk.PngCopyOnly)
        {
            return MessageBox.Show(this, LocalizedText.Get(LocalizedText.PngCopyOnlyWarning),
                LocalizedText.Get(LocalizedText.RotationDialogTitle), MessageBoxButton.YesNo,
                MessageBoxImage.Warning, MessageBoxResult.Yes) == MessageBoxResult.Yes
                ? RotationSaveMode.PngCopy : null;
        }
        if (risk == RotationWriteRisk.Lossy)
        {
            var warning = new LossyRotationDialog(this);
            if (warning.ShowDialog() != true || warning.Choice == LossyRotationChoice.No) return null;
            return warning.Choice == LossyRotationChoice.SaveAsPng
                ? RotationSaveMode.PngCopy : RotationSaveMode.ReplaceOriginal;
        }
        return RotationSaveMode.ReplaceOriginal;
    }
}
