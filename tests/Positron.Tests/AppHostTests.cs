using Positron;
using Positron.Ipc;

namespace Positron.Tests;

/// <summary>
/// Tests for the small, platform-agnostic pieces of the public host surface that do not need a
/// native window: <see cref="App.BuildPreload"/>, the embedded <c>BridgeScript</c> loader, and
/// <see cref="WindowOptions"/> defaults.
/// </summary>
public sealed class AppHostTests
{
    [Fact]
    public void BuildPreload_NullAppPreload_IsBridgeOnly()
    {
        string preload = App.BuildPreload(null);
        Assert.Equal(BridgeScript.Source, preload);
    }

    [Fact]
    public void BuildPreload_WithAppPreload_AppendsAfterBridge()
    {
        const string app = "window.foo = 1;";
        string preload = App.BuildPreload(app);
        Assert.Equal(BridgeScript.Source + "\n" + app, preload);
    }

    [Fact]
    public void BridgeScript_Source_IsLoadable_AndDefinesPositron()
    {
        string source = BridgeScript.Source;
        Assert.False(string.IsNullOrWhiteSpace(source));
        Assert.Contains("window.positron", source);
        Assert.Contains("__positronReceive", source);
    }

    [Fact]
    public void WindowOptions_Defaults()
    {
        var options = new WindowOptions();
        Assert.Equal("Positron", options.Title);
        Assert.Equal(1024, options.Width);
        Assert.Equal(768, options.Height);
        Assert.True(options.Resizable);
        Assert.Null(options.Preload);
        Assert.Equal(ContextMenuMode.Default, options.ContextMenu);
        Assert.False(options.EnableDevTools);
        Assert.Null(options.ServeDirectory);
        Assert.Equal("index.html", options.ServeEntry);
    }
}