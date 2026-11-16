namespace Positron.Ipc;

/// <summary>
/// The narrow boundary between the IPC router and whatever actually carries messages to and
/// from the web content. A real backend implements this over its native WebView message bridge
/// (WKScriptMessageHandler, WebView2 WebMessageReceived, or WebKitGTK script-message-received);
/// tests implement it with an in-memory fake. The router depends only on this interface, so the
/// entire routing, serialization, and dispatch layer is testable without a window.
/// </summary>
internal interface IMessageTransport
{
    /// <summary>
    /// Raised when the web content posts a raw JSON string to the host (JS → C#).
    /// The argument is the exact UTF-8 JSON text produced by the preload bridge.
    /// </summary>
    event Action<string>? MessageReceived;

    /// <summary>
    /// Sends a raw JSON string to the web content (C# → JS). The backend is responsible for
    /// delivering it to the preload bridge's inbound dispatcher on the UI thread.
    /// </summary>
    /// <param name="json">UTF-8 JSON text; never null.</param>
    void PostMessage(string json);
}