using Positron.Backends;

namespace Positron.Tests;

/// <summary>
/// In-memory <see cref="IWebViewBackend"/> test double. Records the calls <see cref="Positron.Window"/>
/// delegates to it and plays the <see cref="IMessageTransport"/> role so a real
/// <see cref="Positron.Ipc.IpcRouter"/> can be wired on top.
/// </summary>
internal sealed class FakeWebViewBackend : IWebViewBackend
{
    public event Action<string>? MessageReceived;

    public event Action? ClosedEvent;

    event Action? IWebViewBackend.Closed
    {
        add => ClosedEvent += value;
        remove => ClosedEvent -= value;
    }

    public string? NavigatedUrl { get; private set; }
    public string? LoadedHtml { get; private set; }
    public List<string> Evaluated { get; } = new();
    public List<string> Posted { get; } = new();
    public bool Ran { get; private set; }
    public bool Closed { get; private set; }
    public bool Disposed { get; private set; }

    public void Initialize(WindowOptions options, string preloadScript)
    {
    }

    public void Navigate(string url) => NavigatedUrl = url;

    public void LoadHtml(string html) => LoadedHtml = html;

    public void Evaluate(string javaScript) => Evaluated.Add(javaScript);

    public void PostMessage(string json) => Posted.Add(json);

    public void Run() => Ran = true;

    public void Close()
    {
        // Mirror a native backend: closing the window raises Closed once.
        if (!Closed)
        {
            Closed = true;
            ClosedEvent?.Invoke();
        }
    }

    /// <summary>Canned path returned by the next open/save dialog call (null = cancelled).</summary>
    public string? NextDialogResult { get; set; }

    public Task<string?> ShowOpenDialogAsync(Positron.Files.OpenFileOptions options) =>
        Task.FromResult(NextDialogResult);

    public Task<string?> ShowSaveDialogAsync(Positron.Files.SaveFileOptions options) =>
        Task.FromResult(NextDialogResult);

    public void Dispose() => Disposed = true;

    /// <summary>Simulates an inbound frame from the web content (drives the router under test).</summary>
    public void Receive(string json) => MessageReceived?.Invoke(json);
}