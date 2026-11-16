# File Dialogs

`namespace Positron.Files`

Option types for the native file dialogs shown by
[`Window.ShowOpenFileAsync`](window.md#showopenfileasync) and
[`Window.ShowSaveFileAsync`](window.md#showsavefileasync). All properties on both types are
optional — pass `null` (or omit the options object) to accept OS defaults.

## `OpenFileOptions`

`sealed class Positron.Files.OpenFileOptions`

Options for `Window.ShowOpenFileAsync`.

| Property | Type | Meaning |
|----------|------|---------|
| `Title` | `string?` | Dialog title. Null uses the OS default. |
| `AllowedExtensions` | `IReadOnlyList<string>?` | Allowed file extensions **without** the leading dot (e.g. `["txt", "md"]`). Null or empty allows any file. |
| `InitialDirectory` | `string?` | Initial directory the dialog opens in. Null uses the OS default (usually last-used). |

```csharp
string? path = await window.ShowOpenFileAsync(new Positron.Files.OpenFileOptions
{
    Title = "Open a document",
    AllowedExtensions = ["txt", "md"],
});
```

## `SaveFileOptions`

`sealed class Positron.Files.SaveFileOptions`

Options for `Window.ShowSaveFileAsync`.

| Property | Type | Meaning |
|----------|------|---------|
| `Title` | `string?` | Dialog title. Null uses the OS default. |
| `SuggestedFileName` | `string?` | The default file name suggested in the save dialog. |
| `AllowedExtensions` | `IReadOnlyList<string>?` | Allowed file extensions **without** the leading dot (e.g. `["txt"]`). Null or empty allows any extension. |
| `InitialDirectory` | `string?` | Initial directory the dialog opens in. Null uses the OS default. |

```csharp
string? path = await window.ShowSaveFileAsync(new Positron.Files.SaveFileOptions
{
    SuggestedFileName = "untitled.txt",
    AllowedExtensions = ["txt"],
});
```

## From JavaScript

The renderer reaches the same dialogs through the built-in bridge namespaces, which take the
same fields (camelCase): `positron.dialog.open(options)` and `positron.dialog.save(options)`,
each resolving to the chosen path or `null`. See [IPC](../concepts/ipc.md) and the
[file-system tutorial](../tutorials/file-system.md).

```js
const path = await window.positron.dialog.save({
  suggestedFileName: "untitled.txt",
  allowedExtensions: ["txt"],
});
```
