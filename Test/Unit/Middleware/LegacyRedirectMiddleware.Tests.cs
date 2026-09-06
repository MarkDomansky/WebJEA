using Microsoft.AspNetCore.Http;
using WebJEA.Middleware;
using Xunit;

namespace WebJEA.Tests.Middleware;

public class LegacyRedirectMiddlewareTests
{
    private static DefaultHttpContext BuildContext(string pathBase, string path, string queryString = "", string method = "GET")
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.PathBase = pathBase;
        context.Request.Path = path;
        if (!string.IsNullOrEmpty(queryString))
        {
            context.Request.QueryString = new QueryString(queryString);
        }

        return context;
    }

    private static async Task<(int StatusCode, string Location, bool NextCalled)> Invoke(DefaultHttpContext context)
    {
        bool nextCalled = false;
        var middleware = new LegacyRedirectMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);
        return (context.Response.StatusCode, context.Response.Headers.Location.ToString(), nextCalled);
    }

    [Fact]
    public async Task DefaultAspx_WithCmdid_PermanentlyRedirectsToCommandHtml()
    {
        var result = await Invoke(BuildContext("", "/default.aspx", "?cmdid=demo"));

        Assert.Equal(StatusCodes.Status301MovedPermanently, result.StatusCode);
        Assert.Equal("/command.html?cmdid=demo", result.Location);
        Assert.False(result.NextCalled);
    }

    [Fact]
    public async Task DefaultAspx_WithoutCmdid_RedirectsToRoot()
    {
        var result = await Invoke(BuildContext("", "/default.aspx"));

        Assert.Equal(StatusCodes.Status301MovedPermanently, result.StatusCode);
        Assert.Equal("/", result.Location);
    }

    [Fact]
    public async Task CommandAspx_RedirectsToCommandHtml()
    {
        var result = await Invoke(BuildContext("", "/command.aspx", "?cmdid=demo&x=1"));

        Assert.Equal("/command.html?cmdid=demo&x=1", result.Location);
    }

    [Fact]
    public async Task PathBase_IsPreservedOnRedirects()
    {
        var result = await Invoke(BuildContext("/hr", "/command.aspx", "?cmdid=demo"));

        Assert.Equal("/hr/command.html?cmdid=demo", result.Location);
    }

    [Fact]
    public async Task PathBase_DefaultAspxWithoutCmdid_RedirectsToAppRoot()
    {
        var result = await Invoke(BuildContext("/hr", "/default.aspx"));

        Assert.Equal("/hr/", result.Location);
    }

    [Fact]
    public async Task EmptyPath_RedirectsToTrailingSlash()
    {
        // "GET /hr" on a sub-application arrives with PathBase=/hr and an empty path.
        var result = await Invoke(BuildContext("/hr", ""));

        Assert.Equal("/hr/", result.Location);
    }

    [Fact]
    public async Task OtherPaths_PassThrough()
    {
        var result = await Invoke(BuildContext("", "/command.html", "?cmdid=demo"));

        Assert.True(result.NextCalled);
        Assert.Equal("", result.Location);
    }

    [Theory]
    [InlineData("/resources/app.js")]
    [InlineData("/api/commands")]
    public async Task UnmatchedPaths_FlowThroughUnchanged(string path)
    {
        var result = await Invoke(BuildContext("", path));

        Assert.True(result.NextCalled);
        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
    }

    [Theory]
    [InlineData("/webjea")]
    [InlineData("/webjea/")]
    public async Task WebjeaPrefix_Alone_RedirectsToRoot(string path)
    {
        var result = await Invoke(BuildContext("", path));

        Assert.Equal(StatusCodes.Status301MovedPermanently, result.StatusCode);
        Assert.Equal("/", result.Location);
    }

    [Fact]
    public async Task WebjeaPrefix_IsStripped_PreservingRemainderAndQuery()
    {
        var result = await Invoke(BuildContext("", "/webjea/command.html", "?cmdid=x"));

        Assert.Equal(StatusCodes.Status301MovedPermanently, result.StatusCode);
        Assert.Equal("/command.html?cmdid=x", result.Location);
    }

    [Fact]
    public async Task WebjeaPrefix_IsCaseInsensitive()
    {
        var result = await Invoke(BuildContext("", "/WebJEA/command.html", "?cmdid=x"));

        Assert.Equal("/command.html?cmdid=x", result.Location);
    }

    [Fact]
    public async Task WebjeaPrefix_IsSegmentBounded()
    {
        var result = await Invoke(BuildContext("", "/webjeaX/command.html"));

        Assert.True(result.NextCalled);
        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
    }

    [Fact]
    public async Task WebjeaDefaultAspx_WithCmdid_IsOneRedirectHop()
    {
        // Combined legacy URL: one 301 direct to the final target, not a redirect chain.
        var result = await Invoke(BuildContext("", "/webjea/default.aspx", "?cmdid=x"));

        Assert.Equal(StatusCodes.Status301MovedPermanently, result.StatusCode);
        Assert.Equal("/command.html?cmdid=x", result.Location);
    }

    [Fact]
    public async Task WebjeaWithCmdidQuery_RedirectsToCommandHtml()
    {
        // Old default-document URL: /webjea/?cmdid=x must land on the form page.
        var result = await Invoke(BuildContext("", "/webjea/", "?cmdid=x"));

        Assert.Equal("/command.html?cmdid=x", result.Location);
    }

    [Fact]
    public async Task RootWithCmdidQuery_RedirectsToCommandHtml()
    {
        var result = await Invoke(BuildContext("", "/", "?cmdid=abc"));

        Assert.Equal(StatusCodes.Status301MovedPermanently, result.StatusCode);
        Assert.Equal("/command.html?cmdid=abc", result.Location);
    }

    [Fact]
    public async Task RootWithoutCmdid_PassesThrough()
    {
        var result = await Invoke(BuildContext("", "/"));

        Assert.True(result.NextCalled);
    }

    [Fact]
    public async Task QueryString_IsPreservedVerbatim_IncludingRepeatsAndEncoding()
    {
        var result = await Invoke(BuildContext("", "/default.aspx", "?cmdid=a&cmdid=b&x=%26y"));

        Assert.Equal("/command.html?cmdid=a&cmdid=b&x=%26y", result.Location);
    }

    [Fact]
    public async Task StrippedPath_IsReEncodedInLocation()
    {
        var result = await Invoke(BuildContext("", "/webjea/my file.txt"));

        Assert.Equal("/my%20file.txt", result.Location);
    }

    [Fact]
    public async Task PathBase_PrefixesWebjeaRedirects()
    {
        var result = await Invoke(BuildContext("/hr", "/webjea/default.aspx", "?cmdid=x"));

        Assert.Equal("/hr/command.html?cmdid=x", result.Location);
    }

    [Fact]
    public async Task Head_IsRedirectedLikeGet()
    {
        var result = await Invoke(BuildContext("", "/webjea/", method: "HEAD"));

        Assert.Equal(StatusCodes.Status301MovedPermanently, result.StatusCode);
        Assert.Equal("/", result.Location);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task NonGetMethods_OnMatchedPaths_Return405WithAllowHeader(string method)
    {
        var context = BuildContext("", "/webjea/default.aspx", "?cmdid=x", method);

        var result = await Invoke(context);

        Assert.Equal(StatusCodes.Status405MethodNotAllowed, result.StatusCode);
        Assert.Equal("GET, HEAD", context.Response.Headers.Allow.ToString());
        Assert.Equal("", result.Location);
        Assert.False(result.NextCalled);
    }

    [Fact]
    public async Task Post_ToDefaultAspx_Returns405()
    {
        var result = await Invoke(BuildContext("", "/default.aspx", "?cmdid=x", "POST"));

        Assert.Equal(StatusCodes.Status405MethodNotAllowed, result.StatusCode);
    }

    [Theory]
    [InlineData("/webjea//evil.com")]
    [InlineData(@"/webjea/\evil.com")]
    public async Task StrippedPathStartingWithDoubleSlash_IsRejected(string path)
    {
        // "//host" in a Location header is a protocol-relative URL to another host.
        var result = await Invoke(BuildContext("", path));

        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
        Assert.Equal("", result.Location);
        Assert.False(result.NextCalled);
    }
}
