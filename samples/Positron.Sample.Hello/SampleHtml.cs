namespace Positron.Sample.Hello;

/// <summary>
/// The sample's HTML UI, kept as an embedded constant so the app is self-contained and has no
/// runtime file dependencies when launched as the end-to-end test target. The equivalent file
/// lives at <c>wwwroot/index.html</c> for the project-template shape.
/// </summary>
internal static class SampleHtml
{
    /// <summary>The single-page document exercising invoke + event + preload.</summary>
    public const string Document = """
        <!DOCTYPE html>
        <html lang="en">
        <head>
          <meta charset="utf-8" />
          <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; script-src 'unsafe-inline';" />
          <title>Positron Hello</title>
          <style>
            :root { color-scheme: light dark; }
            body { font: 15px -apple-system, system-ui, sans-serif; margin: 2rem; }
            h1 { font-size: 1.3rem; }
            input, button { font: inherit; padding: 0.4rem 0.6rem; }
            #out { margin-top: 1rem; padding: 0.75rem; border: 1px solid #8884; border-radius: 6px; }
            #clock { font-variant-numeric: tabular-nums; opacity: 0.8; }
          </style>
        </head>
        <body>
          <h1>Positron — native WebView, no Chromium</h1>
          <p>
            <input id="name" value="world" aria-label="name" />
            <button id="greet">Greet via C#</button>
          </p>
          <div id="out" data-testid="out">Result will appear here.</div>
          <p id="clock" data-testid="clock">clock: —</p>

          <script>
            const out = document.getElementById("out");
            document.getElementById("greet").addEventListener("click", async () => {
              try {
                const name = document.getElementById("name").value;
                const res = await window.positron.invoke("greet", { name });
                out.textContent = res.message;
              } catch (e) {
                out.textContent = "error: " + e.message;
              }
            });

            window.positron.on("tick", (p) => {
              document.getElementById("clock").textContent = "clock: " + p.time;
            });
          </script>
        </body>
        </html>
        """;
}