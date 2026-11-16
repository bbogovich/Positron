using System.Text.Json.Serialization;

using Positron;

// Positron.Sample.TextEditor — the canonical "first real app" tutorial.
//
// A tiny text editor: Open a .txt/.md file into a <textarea>, Save it back. It uses only the
// built-in bridges the library injects — window.positron.dialog.open / .save and
// window.positron.fs.readText / .writeText — so the host registers NO channels of its own.
//
// The whole UI is a wwwroot folder (index.html + app.js) served over app://localhost/, which is
// the recommended way to ship a static web UI with Positron: a real origin, absolute /asset paths,
// and history-API fallback to index.html.

var options = new WindowOptions
{
    Title = "Positron — Text Editor",
    Width = 760,
    Height = 560,
    // Serve the copied wwwroot folder. ServeEntry defaults to "index.html".
    ServeDirectory = AppContext.BaseDirectory + "wwwroot",
    // Disable the default right-click menu so our custom in-page context menu is the only one.
    ContextMenu = ContextMenuMode.Disabled,
};

App.Run(options, window =>
{
    window.LoadUrl("app://localhost/");
}, TextEditorJsonContext.Default.Options);

return 0;

// This sample calls only the library's built-in channels, so it has no custom IPC payload types.
// App.Run still requires a JsonSerializerContext, and the source generator needs at least one
// [JsonSerializable] type to emit the context, so we register the primitives the built-ins use.
/// <summary>Source-generated JSON context (this sample uses only built-in channels).</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(string))]
internal sealed partial class TextEditorJsonContext : JsonSerializerContext
{
}
