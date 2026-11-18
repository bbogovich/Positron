using Positron.Backends;

namespace Positron.Tests;

public sealed class TrustedOriginsTests
{
    [Theory]
    [InlineData("app://localhost/", "app://localhost")]
    [InlineData("app://LocalHost/deep/route?q=1#x", "app://localhost")]
    [InlineData("http://localhost:5173/", "http://localhost:5173")]
    [InlineData("https://example.com:443/a", "https://example.com")]
    [InlineData("http://example.com:80/", "http://example.com")]
    [InlineData("https://example.com:8443/", "https://example.com:8443")]
    [InlineData("file:///Users/me/app/index.html", "file://")]
    [InlineData("about:blank", "about:blank")]
    public void OriginOf_ReturnsComparableOrigin(string url, string expected) =>
        Assert.Equal(expected, TrustedOrigins.OriginOf(url));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/relative/path")]
    [InlineData("data:text/html,<script>1</script>")]
    [InlineData("blob:app://localhost/1234")]
    [InlineData("javascript:alert(1)")]
    [InlineData("about:srcdoc")]
    public void OriginOf_NoTrustworthyOrigin_IsNull(string? url) =>
        Assert.Null(TrustedOrigins.OriginOf(url));

    [Fact]
    public void NothingIsTrusted_Initially()
    {
        var trust = new TrustedOrigins();
        Assert.False(trust.IsTrusted("app://localhost/"));
        Assert.False(trust.IsTrusted("about:blank"));
    }

    [Fact]
    public void Trust_CoversWholeOrigin_ButNotOtherOrigins()
    {
        var trust = new TrustedOrigins();
        trust.Trust("http://localhost:5173/");

        Assert.True(trust.IsTrusted("http://localhost:5173/settings/profile"));
        Assert.False(trust.IsTrusted("http://localhost:4200/"));
        Assert.False(trust.IsTrusted("https://localhost:5173/"));
        Assert.False(trust.IsTrusted("https://evil.example/"));
        Assert.False(trust.IsTrusted(null));
    }

    [Fact]
    public void Trust_UntrustworthyUrl_IsIgnored()
    {
        var trust = new TrustedOrigins();
        trust.Trust("data:text/html,hi");
        Assert.False(trust.IsTrusted("data:text/html,hi"));
    }
}