namespace ClassicPhotoViewer.Decoding;

internal static class ImageDialogFilter
{
    public static string Build(
        IEnumerable<string> extensions,
        string imageFilesLabel = "Image files",
        string allFilesLabel = "All files")
    {
        ArgumentNullException.ThrowIfNull(extensions);

        imageFilesLabel = NormalizeFilterLabel(imageFilesLabel, "Image files");
        allFilesLabel = NormalizeFilterLabel(allFilesLabel, "All files");

        var patterns = extensions
            .Select(ImageFileExtensions.NormalizeExtensionToken)
            .Where(static extension => !string.IsNullOrEmpty(extension))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static extension => extension, StringComparer.OrdinalIgnoreCase)
            .Select(static extension => "*" + extension)
            .ToArray();

        return patterns.Length == 0
            ? $"{allFilesLabel}|*.*"
            : $"{imageFilesLabel}|{string.Join(';', patterns)}|{allFilesLabel}|*.*";
    }

    private static string NormalizeFilterLabel(string label, string fallback)
    {
        return string.IsNullOrWhiteSpace(label)
            ? fallback
            : label.Trim().Replace('|', ' ');
    }
}
