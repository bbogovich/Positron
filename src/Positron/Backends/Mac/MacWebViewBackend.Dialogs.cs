using System.Runtime.Versioning;

using Positron.Files;

using static Positron.Backends.Mac.ObjC;

namespace Positron.Backends.Mac;

/// <summary>
/// macOS native file-dialog support for <see cref="MacWebViewBackend"/>, using <c>NSOpenPanel</c>
/// and <c>NSSavePanel</c> run modally on the main thread. Called on the main thread (the built-in
/// dialog channels are dispatched there), the modal call completes synchronously; called from any
/// other thread, it is marshalled to the main queue and the returned task completes when the user
/// dismisses the panel.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed partial class MacWebViewBackend
{
    private const nint NSModalResponseOK = 1;

    /// <inheritdoc />
    public Task<string?> ShowOpenDialogAsync(OpenFileOptions options) =>
        AppKit.InvokeOnMainAsync(() => RunOpenPanel(options));

    /// <inheritdoc />
    public Task<string?> ShowSaveDialogAsync(SaveFileOptions options) =>
        AppKit.InvokeOnMainAsync(() => RunSavePanel(options));

    private static string? RunOpenPanel(OpenFileOptions options)
    {
        IntPtr panel = Send(Cls("NSOpenPanel"), Sel("openPanel"));
        Send(panel, Sel("setCanChooseFiles:"), (IntPtr)1);
        Send(panel, Sel("setCanChooseDirectories:"), IntPtr.Zero);
        Send(panel, Sel("setAllowsMultipleSelection:"), IntPtr.Zero);

        ConfigureCommon(panel, options.Title, options.InitialDirectory, options.AllowedExtensions);

        nint response = (nint)Send(panel, Sel("runModal"));
        if (response != NSModalResponseOK)
        {
            return null;
        }

        IntPtr urls = Send(panel, Sel("URLs"));
        if (urls == IntPtr.Zero || (nint)Send(urls, Sel("count")) == 0)
        {
            return null;
        }

        IntPtr url = SendIndex(urls, Sel("objectAtIndex:"), 0);
        return PathFromUrl(url);
    }

    private static string? RunSavePanel(SaveFileOptions options)
    {
        IntPtr panel = Send(Cls("NSSavePanel"), Sel("savePanel"));

        if (!string.IsNullOrEmpty(options.SuggestedFileName))
        {
            Send(panel, Sel("setNameFieldStringValue:"), AppKit.NSString(options.SuggestedFileName));
        }

        ConfigureCommon(panel, options.Title, options.InitialDirectory, options.AllowedExtensions);

        nint response = (nint)Send(panel, Sel("runModal"));
        if (response != NSModalResponseOK)
        {
            return null;
        }

        IntPtr url = Send(panel, Sel("URL"));
        return PathFromUrl(url);
    }

    private static void ConfigureCommon(IntPtr panel, string? title, string? initialDir, IReadOnlyList<string>? extensions)
    {
        if (!string.IsNullOrEmpty(title))
        {
            Send(panel, Sel("setMessage:"), AppKit.NSString(title));
        }

        if (!string.IsNullOrEmpty(initialDir))
        {
            IntPtr dirUrl = Send(Cls("NSURL"), Sel("fileURLWithPath:"), AppKit.NSString(initialDir));
            Send(panel, Sel("setDirectoryURL:"), dirUrl);
        }

        if (extensions is { Count: > 0 })
        {
            // Build an NSArray<NSString> of the allowed extensions for -setAllowedFileTypes:.
            IntPtr array = Send(Cls("NSMutableArray"), Sel("array"));
            foreach (string ext in extensions)
            {
                Send(array, Sel("addObject:"), AppKit.NSString(ext));
            }

            Send(panel, Sel("setAllowedFileTypes:"), array);
        }
    }

    private static string? PathFromUrl(IntPtr url)
    {
        if (url == IntPtr.Zero)
        {
            return null;
        }

        IntPtr path = Send(url, Sel("path"));
        string result = AppKit.FromNSString(path);
        return string.IsNullOrEmpty(result) ? null : result;
    }
}