using System.Diagnostics;

namespace Positron.IntegrationTests;

/// <summary>
/// End-to-end tests of the real macOS WKWebView backend. Each launches the sample app in a
/// self-test mode as a separate process — so native AppKit owns a genuine main thread (a unit-test
/// host does not, and crashes) — and asserts the self-test's exit code. Tagged E2E so CI can
/// exclude them where no window server is available.
/// </summary>
[Trait("Category", "E2E")]
public sealed class MacWebViewE2ETests
{
    [SkippableFact]
    public void SelfTest_FullIpcRoundTrip_ExitsZero()
    {
        Skip.IfNot(OperatingSystem.IsMacOS(), "macOS WKWebView backend only runs on macOS.");
        RunSelfTest("--selftest");
    }

    [SkippableFact]
    public void SelfTest_AppSchemeServing_ExitsZero()
    {
        Skip.IfNot(OperatingSystem.IsMacOS(), "macOS WKWebView backend only runs on macOS.");
        // Serves a temp directory over app://, loads it, runs a served <script>, and round-trips an
        // invoke from the app:// origin — exercising the WKURLSchemeHandler path end to end.
        RunSelfTest("--selftest-serve");
    }

    private static void RunSelfTest(string mode)
    {
        string dll = LocateSampleDll();

        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add(dll);
        psi.ArgumentList.Add(mode);

        using var process = Process.Start(psi)!;
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();

        bool exited = process.WaitForExit(milliseconds: 30_000);
        if (!exited)
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail($"Self-test '{mode}' did not exit within 30s.");
        }

        Assert.True(process.ExitCode == 0,
            $"Self-test '{mode}' failed (exit {process.ExitCode}).\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
    }

    /// <summary>Finds the built sample assembly relative to the test output directory.</summary>
    private static string LocateSampleDll()
    {
        // tests/Positron.IntegrationTests/bin/<cfg>/net11.0 → repo root is five levels up.
        string testDir = AppContext.BaseDirectory;
        string config = new DirectoryInfo(testDir).Parent!.Name; // Debug / Release
        string repoRoot = Path.GetFullPath(Path.Combine(testDir, "..", "..", "..", "..", ".."));
        string dll = Path.Combine(repoRoot, "samples", "Positron.Sample.Hello", "bin", config, "net11.0",
            "Positron.Sample.Hello.dll");

        Assert.True(File.Exists(dll), $"Sample assembly not found at {dll}. Build the sample first.");
        return dll;
    }
}