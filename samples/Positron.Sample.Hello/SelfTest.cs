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

        return mode switch
        {
            "serve" => RunMacServe(),
            "close-user" => RunMacClose(userClose: true),
            "close-api" => RunMacClose(userClose: false),
            "thread" => RunMacThreading(),
            "origin" => RunMacOrigin(),
            _ => RunMac(),
        };
    }

    /// <summary>
    /// Runs the real <c>App</c> host loop (<c>-[NSApp run]</c>) and closes the window — either as a
    /// user would (the close button, via <c>-performClose:</c>) or with <c>Window.Close()</c> from a
    /// thread-pool thread — then checks that <c>Window.Closed</c> fired and the run loop returned.
    /// A watchdog fails the test if the run loop never exits.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("macos")]
    private static int RunMacClose(bool userClose)
    {
        string label = userClose ? "close-user" : "close-api";
        var watchdog = new Thread(() =>
        {
            Thread.Sleep(TimeSpan.FromSeconds(15));
            Console.WriteLine($"selftest-{label}: run loop did not exit");
            Environment.Exit(3);
        })
        { IsBackground = true };
        watchdog.Start();

        bool closed = false;
        using var backend = new MacWebViewBackend();
        App.RunWith(backend, new WindowOptions { Title = "close test", Width = 300, Height = 200 }, window =>
        {
            window.Closed += () => closed = true;
            window.LoadHtml("<!doctype html><title>close</title><p>closing…</p>");
            _ = Task.Run(async () =>
            {
                await Task.Delay(1500);
                if (userClose)
                {
                    backend.PerformCloseForTest();
                }
                else
                {
                    window.Close(); // from a thread-pool thread
                }
            });
        }, SelfTestJsonContext.Default.Options);

        Console.WriteLine($"selftest-{label}: run loop returned, closed={closed}");
        return closed ? 0 : 1;
    }

    /// <summary>
    /// Delivers C#→JS traffic from thread-pool threads: an <c>Emit</c> from <c>Task.Run</c> and the
    /// result of an async handler that resumes off the main thread. Both must reach the page.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("macos")]
    private static int RunMacThreading()
    {
        using var backend = new MacWebViewBackend();
        var router = new IpcRouter(backend, SelfTestJsonContext.Default.Options);
        router.Handle<GreetRequest, GreetResponse>("slow", async req =>
        {
            await Task.Delay(200).ConfigureAwait(false); // resume on the thread pool
            return new GreetResponse("slow-" + req.Name);
        });

        const string html = """
            <!DOCTYPE html><html><head><title>pending</title></head><body><script>
              let parts = { tick: "-", slow: "-" };
              const show = () => { document.title = "T:" + parts.tick + "|S:" + parts.slow; };
              window.positron.on("tick", p => { parts.tick = p; show(); });
              window.positron.invoke("slow", { name: "x" }).then(r => { parts.slow = r.message; show(); });
            </script></body></html>
            """;

        backend.Initialize(new WindowOptions { Title = "pending", Width = 400, Height = 300 }, ReadBridge());
        backend.LoadHtml(html);
        _ = Task.Run(async () =>
        {
            await Task.Delay(1000);
            router.Emit("tick", "pool"); // from a thread-pool thread
        });
        backend.RunFor(TimeSpan.FromSeconds(3));

        string title = backend.GetTitle();
        Console.WriteLine($"selftest-thread: title='{title}'");
        return title == "T:pool|S:slow-x" ? 0 : 1;
    }

    /// <summary>
    /// The trust boundary. A trusted page's own invoke must work, while (a) an iframe inside it that
    /// posts straight to the native message handler and (b) a page under an origin the host never
    /// loaded (standing in for a remote site) must not reach the handler at all.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("macos")]
    private static int RunMacOrigin()
    {
        using var backend = new MacWebViewBackend();
        var router = new IpcRouter(backend, SelfTestJsonContext.Default.Options);
        var hits = new System.Collections.Concurrent.ConcurrentBag<string>();
        router.Handle<GreetRequest, GreetResponse>("probe", req =>
        {
            hits.Add(req.Name);
            return new GreetResponse("ok-" + req.Name);
        });

        const string directPost =
            "window.webkit.messageHandlers.positron.postMessage(JSON.stringify("
            + "{ kind: 'invoke', seq: 99, channel: 'probe', payload: { name: 'NAME' } }));";

        string trustedPage = """
            <!DOCTYPE html><html><head><title>pending</title></head><body><script>
              window.positron.invoke("probe", { name: "control" })
                .then(r => { document.title = "CONTROL:" + r.message; });
            </script>
            <iframe srcdoc="<script>try { ATTACK } catch (e) {}</script>"></iframe>
            </body></html>
            """.Replace("ATTACK", directPost.Replace("NAME", "iframe"));

        string remotePage = """
            <!DOCTYPE html><html><head><title>remote</title></head><body><script>
              window.positron.invoke("probe", { name: "remote-bridge" })
                .then(r => { document.title = "LEAKED:" + r.message; });
              try { ATTACK } catch (e) {}
            </script></body></html>
            """.Replace("ATTACK", directPost.Replace("NAME", "remote-direct"));

        backend.Initialize(new WindowOptions { Title = "pending", Width = 400, Height = 300 }, ReadBridge());
        backend.LoadHtml(trustedPage);
        backend.RunFor(TimeSpan.FromSeconds(3));
        string trustedTitle = backend.GetTitle();

        backend.LoadHtmlUntrustedForTest(remotePage, "https://untrusted.example/");
        backend.RunFor(TimeSpan.FromSeconds(3));
        string remoteTitle = backend.GetTitle();

        string hitList = string.Join(",", hits.OrderBy(h => h, StringComparer.Ordinal));
        Console.WriteLine($"selftest-origin: trusted='{trustedTitle}' remote='{remoteTitle}' hits=[{hitList}]");
        return trustedTitle == "CONTROL:ok-control" && hitList == "control" && remoteTitle == "remote" ? 0 : 1;
    }

    private static string ReadBridge()
    {
        using Stream stream = typeof(App).Assembly.GetManifestResourceStream("Positron.Ipc.PositronBridge.js")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
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