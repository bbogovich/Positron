using System.Reflection;

using Positron.Backends;

namespace Positron.Ipc;

/// <summary>
/// Loads the embedded <c>PositronBridge.js</c> preload script from the assembly's resources.
/// The bridge is embedded (rather than shipped as a loose file) so the library remains a single
/// self-contained assembly.
/// </summary>
internal static class BridgeScript
{
    private const string ResourceName = "Positron.Ipc.PositronBridge.js";

    private static readonly Lazy<string> s_source = new(Load);

    /// <summary>The JavaScript source of the Positron bridge.</summary>
    public static string Source => s_source.Value;

    private static string Load()
    {
        Assembly assembly = typeof(BridgeScript).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded bridge script '{ResourceName}' was not found. Available: "
                + string.Join(", ", assembly.GetManifestResourceNames()));
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}