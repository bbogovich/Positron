# Tutorial: custom web components

Positron has no opinion about your frontend — any web technology works, including the platform's own
component model, **custom elements**. This tutorial builds a reusable `<positron-greeter>` element
that talks to C#.

The runnable source is in [`samples/web-components`](https://github.com/bbogovich/Positron/tree/main/samples/web-components).

```bash
dotnet run --project samples/web-components
```

## The element

A standard custom element with Shadow DOM. The only Positron-specific line is the
`window.positron.invoke` call — everything else is vanilla Web Components:

```js
class PositronGreeter extends HTMLElement {
  constructor() {
    super();
    this.attachShadow({ mode: "open" });
  }

  connectedCallback() {
    const name = this.getAttribute("name") || "world";
    this.shadowRoot.innerHTML = `
      <input value="${name}" aria-label="name" />
      <button>Greet via C#</button>
      <output>Result will appear here.</output>
    `;

    const input = this.shadowRoot.querySelector("input");
    const output = this.shadowRoot.querySelector("output");
    this.shadowRoot.querySelector("button").addEventListener("click", async () => {
      try {
        const res = await window.positron.invoke("greet", { name: input.value });
        output.textContent = res.message;
      } catch (e) {
        output.textContent = "error: " + e.message;
      }
    });
  }
}

customElements.define("positron-greeter", PositronGreeter);
```

Use it anywhere in your markup:

```html
<positron-greeter name="world"></positron-greeter>
```

!!! note "window.positron is available inside Shadow DOM"
    `window.positron` is a global installed by the preload bridge, so it's reachable from inside a
    custom element's shadow root just like any other `window` API. No special wiring needed.

## The host

Serve the folder over `app://` and register the one channel the element invokes:

```csharp
var options = new WindowOptions
{
    Title = "Positron — Web Components",
    ServeDirectory = AppContext.BaseDirectory + "wwwroot",
};

App.Run(options, window =>
{
    window.Handle<GreetRequest, GreetResponse>("greet", req =>
        new GreetResponse($"Hello, {req.Name}! — greeted by C#"));

    window.LoadUrl("app://localhost/");
}, WebComponentsJsonContext.Default.Options);

internal sealed record GreetRequest(string Name);
internal sealed record GreetResponse(string Message);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(GreetRequest))]
[JsonSerializable(typeof(GreetResponse))]
internal sealed partial class WebComponentsJsonContext : JsonSerializerContext { }
```

## Dependencies

Custom elements need no build step or framework, but if you want to use a component library (Lit,
Shoelace, FAST, etc.), install it with npm and either bundle it or load it from your served
`wwwroot`. Because the app runs over the `app://` origin, absolute module paths and `import` work the
same as on the web — put the dependency's files under `ServeDirectory` and reference them with an
absolute path (`/vendor/lit.js`) or an import map. For a full framework build pipeline, see the
[Angular](angular.md) and [React](react.md) tutorials, which follow the same serve-the-`dist`-folder
pattern.

## Next steps

- [Angular](angular.md) / [React](react.md) — framework SPAs served the same way.
- [IPC](../concepts/ipc.md) — the `invoke`/`Handle` contract.
