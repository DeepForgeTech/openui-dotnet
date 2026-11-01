using OpenUI.Lang.Runtime;

namespace OpenUI.Lang.Core.Tests.Validation;

/// <summary>Section 5.4: "<c>url</c> checks for an absolute URL with a scheme and a host."</summary>
public class AbsoluteUrlTests
{
    [Theory]
    [InlineData("https://openui.com/x")]
    [InlineData("http://a")]
    [InlineData("HTTP://A.B")]
    [InlineData("ftp://files.example.com")]
    [InlineData("custom+scheme.1-2://host")]
    [InlineData("https://host:8080/path?q=1#f")]
    [InlineData("https://user:pw@host/")]
    [InlineData("http://[::1]:80/")]
    public void A_scheme_and_a_host_pass(string url) => Assert.True(UrlText.IsAbsolute(url));

    [Theory]
    [InlineData("openui.com")]
    [InlineData("/path")]
    [InlineData("//host/path")]
    [InlineData("javascript:alert(1)")]
    [InlineData("mailto:a@b.co")]
    [InlineData("data:text/plain,x")]
    [InlineData("https:")]
    [InlineData("https://")]
    [InlineData("https:///path")]
    [InlineData("https://?q")]
    [InlineData("https://#f")]
    [InlineData("https://:80")]
    [InlineData("https://user@")]
    [InlineData("1http://host")]
    [InlineData("://host")]
    [InlineData("ht tp://host")]
    [InlineData("https://ho st")]
    [InlineData(" https://host")]
    [InlineData("https://host\n")]
    public void Anything_without_a_scheme_and_a_host_fails(string url) => Assert.False(UrlText.IsAbsolute(url));
}
