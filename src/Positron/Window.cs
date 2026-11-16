using Positron.Backends;
using Positron.Ipc;

namespace Positron;

/// <summary>
/// A live Positron window. Wraps the native backend and the IPC router, exposing a small surface
/// for loading content, registering C# handlers callable from JavaScript, and pushing events to
/// the web content. Obtain one via the configure callback of <see cref="App"/>.<c>Run</c>.
/// </summary>
public sealed class Window
{
    private readonly IWebViewBackend _backend;
    private readonly IpcRouter _router;

    internal Window(IWebViewBackend backend, IpcRouter router)
    {
        _backend = backend;
        _router = router;
    }

    /// <summary>
    /// Raised after the window's native run loop has been asked to exit via <see cref="Close"/>.
    /// Use it to release app resources (timers, file handles) tied to the window's lifetime.
    /// </summary>
    public event Action? Closed;

    /// <summary>Navigates the web content to a URL (e.g. a <c>file://</c> or <c>http://</c> address).</summary>
    public void LoadUrl(string url)
    {
        ArgumentException.ThrowIfNullOrEmpty(url);
        _backend.Navigate(url);
    }

    /// <summary>Loads an HTML string directly into the web content.</summary>
    public void LoadHtml(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        _backend.LoadHtml(html);
    }

    /// <summary>
    /// Registers an async C# handler invokable from JavaScript via
    /// <c>window.positron.invoke(channel, payload)</c>.
    /// </summary>
    public void Handle<TArg, TResult>(string channel, Func<TArg, Task<TResult>> handler) =>
        _router.Handle(channel, handler);

    /// <summary>
    /// Registers a synchronous C# handler invokable from JavaScript via
    /// <c>window.positron.invoke(channel, payload)</c>.
    /// </summary>
    public void Handle<TArg, TResult>(string channel, Func<TArg, TResult> handler) =>
        _router.Handle(channel, handler);

    /// <summary>
    /// Pushes an event to JavaScript listeners registered via
    /// <c>window.positron.on(channel, cb)</c>.
    /// </summary>
    public void Emit<TPayload>(string channel, TPayload payload) =>
        _router.Emit(channel, payload);

    /// <summary>
    /// Closes the window and exits the native run loop, unblocking
    /// <see cref="App.Run(WindowOptions, Action{Window}, System.Text.Json.JsonSerializerOptions)"/>.
    /// Raises <see cref="Closed"/>. Safe to call from a handler or any thread.
    /// </summary>
    public void Close()
    {
        _backend.Close();
        Closed?.Invoke();
    }

    /// <summary>
    /// Shows a native open-file dialog on the UI thread and returns the selected absolute path, or
    /// null if the user cancelled. Read the file with <see cref="System.IO.File"/> from the host.
    /// </summary>
    public Task<string?> ShowOpenFileAsync(Files.OpenFileOptions? options = null) =>
        _backend.ShowOpenDialogAsync(options ?? new Files.OpenFileOptions());

    /// <summary>
    /// Shows a native save-file dialog on the UI thread and returns the chosen absolute path, or
    /// null if the user cancelled. Write the file with <see cref="System.IO.File"/> from the host.
    /// </summary>
    public Task<string?> ShowSaveFileAsync(Files.SaveFileOptions? options = null) =>
        _backend.ShowSaveDialogAsync(options ?? new Files.SaveFileOptions());

    /// <summary>
    /// Registers the reserved <c>positron.*</c> built-in channels (file dialogs + text file I/O)
    /// that back <c>window.positron.dialog</c> / <c>window.positron.fs</c> in the web content.
    /// Called once by <see cref="App"/> before the consumer's configure callback.
    /// </summary>
    internal void RegisterBuiltins(IWebViewBackend backend)
    {
        _router.Handle<Ipc.OpenDialogRequest, string?>("positron.dialog.open", req =>
            backend.ShowOpenDialogAsync(new Files.OpenFileOptions
            {
                Title = req.Title,
                AllowedExtensions = req.AllowedExtensions,
                InitialDirectory = req.InitialDirectory,
            }));

        _router.Handle<Ipc.SaveDialogRequest, string?>("positron.dialog.save", req =>
            backend.ShowSaveDialogAsync(new Files.SaveFileOptions
            {
                Title = req.Title,
                SuggestedFileName = req.SuggestedFileName,
                AllowedExtensions = req.AllowedExtensions,
                InitialDirectory = req.InitialDirectory,
            }));

        _router.Handle<Ipc.ReadTextRequest, string>("positron.fs.readText", req =>
            File.ReadAllTextAsync(req.Path));

        _router.Handle<Ipc.WriteTextRequest, bool>("positron.fs.writeText", async req =>
        {
            await File.WriteAllTextAsync(req.Path, req.Contents).ConfigureAwait(false);
            return true;
        });
    }
}