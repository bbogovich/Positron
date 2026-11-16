using System.Text.Json;
using System.Text.Json.Serialization;

using Positron.Backends.Mac;
using Positron.Ipc;

namespace Positron.Sample.Hello;

/// <summary>
/// Headless, bounded self-test of the native WebView IPC round trip. Runs in-process as a real
/// standalone program (so native AppKit owns a genuine main thread) and returns a process exit
/// code the integration test can assert on: 0 = pass, 1 = fail.
/// </summary>
internal static partial class SelfTest
{
    /// <summary>Runs the round-trip check and returns a process exit code.</summary>
    public static int Run(string mode = "ipc")
    {
        if (!OperatingSystem.IsMacOS())
        {
            Console.Error.WriteLine("selftest: only implemented for macOS.");
            return 2;
        }

        return mode == "serve" ? RunMacServe() : RunMac();
    }

    [System.Runtime.Versioning.SupportedOSPlatform("macos")]
    private static int RunMacServe()
    {
        // Serve a temp directory over app:// and confirm the served document + a linked asset load,
        // and that app:// → C# IPC works from the served origin.
        string dir = Path.Combine(Path.GetTempPath(), "positron-serve-selftest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "app.js"),
            "window.__served = true; window.positron.invoke('ping', {}).then(r => { document.title = 'SERVED:' + r; });");
        File.WriteAllText(Path.Combine(dir, "index.html"),
            "<!doctype html><html><head><title>pending</title></head><body><script src=\"/app.js\"></script></body></html>");

        using var backend = new MacWebViewBackend();
        var router = new IpcRouter(backend, SelfTestJsonContext.Default.Options);
        router.Handle<object, string>("ping", _ => "pong");

        using Stream stream = typeof(App).Assembly.GetManifestResourceStream("Positron.Ipc.PositronBridge.js")!;
        using var reader = new StreamReader(stream);
        string preload = reader.ReadToEnd();

        backend.Initialize(
            new WindowOptions { Title = "pending", Width = 400, Height = 300, ServeDirectory = dir },
            preload);
        backend.Navigate("app://localhost/");
        backend.RunFor(TimeSpan.FromSeconds(4));

        string title = backend.GetTitle();
        Console.WriteLine($"selftest-serve: title='{title}'");
        Directory.Delete(dir, recursive: true);

        // Title becomes SERVED:pong only if: index.html was served, /app.js was served and ran, and
        // the app:// origin could invoke a C# handler and receive the reply.
        return title.StartsWith("SERVED:pong", StringComparison.Ordinal) ? 0 : 1;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("macos")]
    private static int RunMac()
    {
        using var backend = new MacWebViewBackend();
        var router = new IpcRouter(backend, SelfTestJsonContext.Default.Options);

        bool handlerHit = false;
        router.Handle<GreetRequest, GreetResponse>("greet", req =>
        {
            handlerHit = true;
            return new GreetResponse($"hi-{req.Name}");
        });

        const string html = """
            <!DOCTYPE html><html><head><title>pending</title></head><body>
            <script>
              document.title = "JS-RAN";
              if (!window.positron) {
                document.title = "NO-BRIDGE";
              } else {
                document.title = "BRIDGE-OK";
                window.positron.invoke("greet", { name: "e2e" })
                  .then(r => { document.title = "RESULT:" + r.message; })
                  .catch(e => { document.title = "ERROR:" + e.message; });
              }
            </script></body></html>
            """;

        using Stream stream = typeof(App).Assembly.GetManifestResourceStream("Positron.Ipc.PositronBridge.js")!;
        using var reader = new StreamReader(stream);
        string preload = reader.ReadToEnd();

        backend.Initialize(new WindowOptions { Title = "pending", Width = 400, Height = 300 }, preload);
        backend.LoadHtml(html);
        backend.RunFor(TimeSpan.FromSeconds(3));

        string title = backend.GetTitle();
        Console.WriteLine($"selftest: handlerHit={handlerHit} title='{title}'");

        bool ok = handlerHit && title.StartsWith("RESULT:", StringComparison.Ordinal) && title.Contains("hi-e2e");
        return ok ? 0 : 1;
    }

    private sealed record GreetRequest(string Name);

    private sealed record GreetResponse(string Message);

    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(GreetRequest))]
    [JsonSerializable(typeof(GreetResponse))]
    [JsonSerializable(typeof(string))]
    [JsonSerializable(typeof(object))]
    private sealed partial class SelfTestJsonContext : JsonSerializerContext
    {
    }
}