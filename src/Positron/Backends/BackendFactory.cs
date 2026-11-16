using System.Runtime.Versioning;

using Positron.Backends.Linux;
using Positron.Backends.Mac;
using Positron.Backends.Windows;

namespace Positron.Backends;

/// <summary>
/// Selects and constructs the native WebView backend for the current operating system.
/// </summary>
internal static class BackendFactory
{
    /// <summary>
    /// Creates the backend for the host OS.
    /// </summary>
    /// <returns>A platform-specific <see cref="IWebViewBackend"/>.</returns>
    /// <exception cref="PlatformNotSupportedException">The current OS has no backend.</exception>
    public static IWebViewBackend Create()
    {
        if (OperatingSystem.IsMacOS())
        {
            return CreateMac();
        }

        if (OperatingSystem.IsWindows())
        {
            return CreateWindows();
        }

        if (OperatingSystem.IsLinux())
        {
            return CreateLinux();
        }

        throw new PlatformNotSupportedException(
            "Positron supports macOS, Windows, and Linux. The current OS is not supported.");
    }

    [SupportedOSPlatform("macos")]
    private static IWebViewBackend CreateMac() => new MacWebViewBackend();

    [SupportedOSPlatform("windows")]
    private static IWebViewBackend CreateWindows() => new WindowsWebViewBackend();

    [SupportedOSPlatform("linux")]
    private static IWebViewBackend CreateLinux() => new LinuxWebViewBackend();
}