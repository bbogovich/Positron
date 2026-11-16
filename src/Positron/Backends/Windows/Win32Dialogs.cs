using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Positron.Files;

namespace Positron.Backends.Windows;

/// <summary>
/// Native open/save file dialogs via the Win32 common dialog box API (<c>GetOpenFileNameW</c> /
/// <c>GetSaveFileNameW</c>). Design-complete; not exercised on the development machine (macOS).
/// </summary>
[SupportedOSPlatform("windows")]
internal static partial class Win32Dialogs
{
    public static string? ShowOpen(IntPtr owner, OpenFileOptions options) =>
        Show(owner, options.Title, filter: BuildFilter(options.AllowedExtensions),
            initialDir: options.InitialDirectory, suggestedName: null, save: false);

    public static string? ShowSave(IntPtr owner, SaveFileOptions options) =>
        Show(owner, options.Title, filter: BuildFilter(options.AllowedExtensions),
            initialDir: options.InitialDirectory, suggestedName: options.SuggestedFileName, save: true);

    private static unsafe string? Show(
        IntPtr owner, string? title, string? filter, string? initialDir, string? suggestedName, bool save)
    {
        // OPENFILENAME expects a writable buffer for the resulting path.
        const int bufferChars = 4096;
        char[] fileBuffer = new char[bufferChars];
        if (!string.IsNullOrEmpty(suggestedName))
        {
            suggestedName.AsSpan(0, Math.Min(suggestedName.Length, bufferChars - 1)).CopyTo(fileBuffer);
        }

        fixed (char* file = fileBuffer)
        fixed (char* filterPtr = filter)
        fixed (char* titlePtr = title)
        fixed (char* dirPtr = initialDir)
        {
            var ofn = new OpenFileName
            {
                lStructSize = (uint)Marshal.SizeOf<OpenFileName>(),
                hwndOwner = owner,
                lpstrFilter = (IntPtr)filterPtr,
                lpstrFile = (IntPtr)file,
                nMaxFile = bufferChars,
                lpstrInitialDir = (IntPtr)dirPtr,
                lpstrTitle = (IntPtr)titlePtr,
                // OFN_EXPLORER | OFN_PATHMUSTEXIST (+ OFN_FILEMUSTEXIST for open / OFN_OVERWRITEPROMPT for save)
                Flags = 0x00080000 | 0x00000800 | (save ? 0x00000002u : 0x00001000u),
            };

            bool ok = save ? GetSaveFileNameW(ref ofn) : GetOpenFileNameW(ref ofn);
            if (!ok)
            {
                return null;
            }

            string result = new string(file);
            return string.IsNullOrEmpty(result) ? null : result;
        }
    }

    /// <summary>Builds a double-null-terminated Win32 filter string from extensions, or null for all files.</summary>
    private static string? BuildFilter(IReadOnlyList<string>? extensions)
    {
        if (extensions is not { Count: > 0 })
        {
            return null;
        }

        string patterns = string.Join(";", extensions.Select(e => $"*.{e}"));
        // "Supported files\0*.txt;*.md\0All files\0*.*\0\0"
        return $"Supported files\0{patterns}\0All files\0*.*\0\0";
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenFileName
    {
        public uint lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        public IntPtr lpstrFilter;
        public IntPtr lpstrCustomFilter;
        public uint nMaxCustFilter;
        public uint nFilterIndex;
        public IntPtr lpstrFile;
        public uint nMaxFile;
        public IntPtr lpstrFileTitle;
        public uint nMaxFileTitle;
        public IntPtr lpstrInitialDir;
        public IntPtr lpstrTitle;
        public uint Flags;
        public ushort nFileOffset;
        public ushort nFileExtension;
        public IntPtr lpstrDefExt;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        public IntPtr lpTemplateName;
        public IntPtr pvReserved;
        public uint dwReserved;
        public uint FlagsEx;
    }

    [LibraryImport("comdlg32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetOpenFileNameW(ref OpenFileName ofn);

    [LibraryImport("comdlg32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetSaveFileNameW(ref OpenFileName ofn);
}