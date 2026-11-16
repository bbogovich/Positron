namespace Positron.Files;

/// <summary>
/// Options for <see cref="Window.ShowOpenFileAsync(OpenFileOptions)"/>. All properties are optional.
/// </summary>
public sealed class OpenFileOptions
{
    /// <summary>Dialog title. Null uses the OS default.</summary>
    public string? Title { get; set; }

    /// <summary>
    /// Allowed file extensions WITHOUT the leading dot (e.g. <c>["txt", "md"]</c>). Null or empty
    /// allows any file.
    /// </summary>
    public IReadOnlyList<string>? AllowedExtensions { get; set; }

    /// <summary>Initial directory the dialog opens in. Null uses the OS default (usually last-used).</summary>
    public string? InitialDirectory { get; set; }
}

/// <summary>
/// Options for <see cref="Window.ShowSaveFileAsync(SaveFileOptions)"/>. All properties are optional.
/// </summary>
public sealed class SaveFileOptions
{
    /// <summary>Dialog title. Null uses the OS default.</summary>
    public string? Title { get; set; }

    /// <summary>The default file name suggested in the save dialog.</summary>
    public string? SuggestedFileName { get; set; }

    /// <summary>
    /// Allowed file extensions WITHOUT the leading dot (e.g. <c>["txt"]</c>). Null or empty allows
    /// any extension.
    /// </summary>
    public IReadOnlyList<string>? AllowedExtensions { get; set; }

    /// <summary>Initial directory the dialog opens in. Null uses the OS default.</summary>
    public string? InitialDirectory { get; set; }
}