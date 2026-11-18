# Security

Positron's security model follows the same principle as Electron's `contextBridge`: the web
content never touches a native handle. It gets a small, frozen, validated JavaScript surface
(`window.positron`), and everything privileged happens in C#, behind a boundary you control.

## The isolation boundary

The preload bridge exposes `window.positron` as a **frozen** object — not writable, not
reconfigurable, installed once and never replaced. Page script can call `invoke` and `on`; it
cannot reach into the transport, swap the bridge, or obtain a reference to any OS object. The
only way across the boundary is a channel you registered in C#.

## Validate handler arguments

A handler's argument arrives as **JSON deserialized from the renderer** — it is untrusted
input from the system boundary. Validate it the same way you would validate any external
request:

```csharp
window.Handle<ReadRequest, string>("readConfig", req =>
{
    // The renderer chose this path. Do not trust it blindly.
    string full = Path.GetFullPath(req.Path);
    if (!full.StartsWith(_allowedRoot, StringComparison.Ordinal))
        throw new InvalidOperationException("Path outside allowed root.");

    return File.ReadAllText(full);
});
```

The host decides what each channel is allowed to do. A handler is the right place to enforce
path allow-lists, size limits, and argument shape — there is no implicit privilege granted to
the renderer.

## Only documents you loaded can use the bridge

The bridge is installed in whatever page the WebView's main frame shows, and on WebKit the native
message handler is reachable from every frame. If a page from another origin could talk to it —
because the user clicked an external link, a redirect happened, or an iframe embeds third-party
content — it could call every channel you registered, including the built-in `positron.fs.*`
file I/O.

Positron therefore trusts only the origins **the host itself loaded**:

| Host call | Trusted origin |
|-----------|----------------|
| `window.LoadUrl(url)` | the origin of `url` (e.g. `http://localhost:5173` for a dev server) |
| `window.LoadHtml(html)` | `about:blank` (the base URL HTML strings load under) |
| `WindowOptions.ServeDirectory` | `app://localhost` |

Messages from any other document are dropped before they reach a handler, and results and
events are not delivered to one; its `invoke` calls simply never settle. On macOS and Windows only
the main frame can send messages; on Linux the bridge is injected into the top frame only, and
messages are checked against the main frame's URL. All `file://` pages count as one origin.

Keep external links out of the app window: open them in the system browser through a channel of
your own that validates the URL.

## Errors stay host-side

When a handler throws, the renderer receives a **generic failure** — the `result` envelope
carries `ok: false` with a plain error message, and the JS Promise rejects with an `Error`.
Internal specifics (stack traces, file-system layout, secrets in exception text) are **not**
marshalled to the page. The real exception goes to
[`Window.HandlerError`](../api/window.md#handlererror) — subscribe to it to log failures (with no
subscriber it is written to standard error); the renderer only learns that the call failed.

## File access is mediated

The WebView never gets raw file-system access. The built-in `positron.fs.readText` /
`positron.fs.writeText` channels and the native file dialogs run **in C#**, on the host. The
renderer can only ask; the host performs the I/O and can refuse, sandbox, or audit it.

The built-ins are always registered and accept **any absolute path** from a trusted document —
they are as powerful as the user account. That is safe only while every document that can reach
the bridge is your own code (see above): never load remote or user-supplied content into the app
window. If you need narrower file access, do your file I/O through channels of your own that
enforce an allow-list, and do not call the built-ins.

## Content Security Policy

Positron does not impose a CSP for you — set one in your page's `<head>` to constrain what the
rendered content may load and execute:

```html
<meta http-equiv="Content-Security-Policy"
      content="default-src 'self' app:; script-src 'self'; object-src 'none'">
```

A restrictive CSP is the renderer-side complement to the C# boundary: the boundary controls
what native capabilities the page can reach, and the CSP controls what the page itself is
allowed to load and run.

## Development vs. release

[`WindowOptions.EnableDevTools`](../api/window-options.md) defaults to `false`. Enable it
during development for the WebView's inspector; leave it off in release builds.

## Checklist

- Treat every handler argument as untrusted; validate at the boundary.
- Enforce path allow-lists before touching the file system.
- Let handler exceptions surface as generic failures — never leak internals to the renderer; log
  them from `Window.HandlerError`.
- Load only your own content into the app window; open external links in the system browser.
- Set a restrictive CSP in the page `<head>`.
- Expose the smallest set of channels your UI actually needs.
- Disable dev tools in release builds.
