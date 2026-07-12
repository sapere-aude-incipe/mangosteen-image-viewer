using Mangosteen.Localization;
using System.Windows;

namespace Mangosteen.Dialogs;

internal enum LossyRotationChoice
{
    No,
    ReplaceOriginal,
    SaveAsPng
}

public partial class LossyRotationDialog : Window
{
    private static readonly string[] ThemeResourceKeys =
    [
        "PanelBackground",
        "PanelBorder",
        "ControlHoverBackground",
        "ControlPressedBackground",
        "TextPrimary",
        "TextSecondary",
        "TextDisabled",
        "AccentCheckBackground",
        "AccentCheckBorder"
    ];

    internal LossyRotationDialog(Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        Owner = owner;
        InitializeComponent();
        CopyThemeResources(owner);
        Title = LocalizedText.Get(LocalizedText.RotationDialogTitle);
        MessageText.Text = LocalizedText.Get(LocalizedText.LossyRotationWarning);
        NoButton.Content = LocalizedText.Get(LocalizedText.No);
        SaveAsPngButton.Content = LocalizedText.Get(LocalizedText.SaveAsPng);
        YesButton.Content = LocalizedText.Get(LocalizedText.Yes);
    }

    internal LossyRotationChoice Choice { get; private set; } = LossyRotationChoice.No;

    private void NoButton_Click(object sender, RoutedEventArgs e)
    {
        Choice = LossyRotationChoice.No;
        DialogResult = false;
    }

    private void SaveAsPngButton_Click(object sender, RoutedEventArgs e)
    {
        Choice = LossyRotationChoice.SaveAsPng;
        DialogResult = true;
    }

    private void YesButton_Click(object sender, RoutedEventArgs e)
    {
        Choice = LossyRotationChoice.ReplaceOriginal;
        DialogResult = true;
    }

    private void CopyThemeResources(Window owner)
    {
        foreach (var key in ThemeResourceKeys)
        {
            if (owner.TryFindResource(key) is { } resource)
            {
                Resources[key] = resource;
            }
        }
    }
}
