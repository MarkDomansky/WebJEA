#nullable enable
namespace WebJEA.Middleware;

/// <summary>
/// Always-on canonical-URL normalization for pre-migration URLs. Old deployments
/// exposed the app under /webjea (WebForms era) and at /default.aspx /command.aspx;
/// links to those URLs live in ticketing systems, wikis, and bookmarks. All rules are
/// evaluated in a single pass so a combined legacy URL (e.g. /webjea/default.aspx)
/// produces one redirect hop. GET/HEAD get a 301; other methods get 405 rather than
/// silently dropping a request body. Redirect targets are prefixed with the request
/// path base so the app also works when hosted as an IIS sub-application (e.g. /hr).
/// </summary>
public class LegacyRedirectMiddleware
{
    private const string WebjeaPrefix = "/webjea";

    private readonly RequestDelegate _next;

    public LegacyRedirectMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        string path = request.Path.Value ?? "";

        // A sub-application requested without a trailing slash ("GET /hr") yields an
        // empty path; redirect to the canonical "/hr/" so document-relative URLs on
        // the static pages resolve correctly.
        if (path.Length == 0)
        {
            context.Response.Redirect(request.PathBase + "/" + request.QueryString);
            return Task.CompletedTask;
        }

        bool webjeaPrefixStripped = false;
        if (path.Equals(WebjeaPrefix, StringComparison.OrdinalIgnoreCase))
        {
            path = "/";
            webjeaPrefixStripped = true;
        }
        else if (path.StartsWith(WebjeaPrefix + "/", StringComparison.OrdinalIgnoreCase))
        {
            path = path[WebjeaPrefix.Length..];
            webjeaPrefixStripped = true;

            // A Location header starting with "//" (or "/\") is a protocol-relative
            // URL to another host — refuse to build an open redirect from it.
            if (path.StartsWith("//") || path.StartsWith("/\\"))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return Task.CompletedTask;
            }
        }

        string? target = null;
        if (path.Equals("/default.aspx", StringComparison.OrdinalIgnoreCase))
        {
            target = string.IsNullOrEmpty(request.Query["cmdid"]) ? "/" : "/command.html";
        }
        else if (path.Equals("/command.aspx", StringComparison.OrdinalIgnoreCase))
        {
            target = "/command.html";
        }
        else if (path == "/" && !string.IsNullOrEmpty(request.Query["cmdid"]))
        {
            // Old default-document URLs (/?cmdid=...) must land on the form page;
            // the dashboard makes no use of cmdid.
            target = "/command.html";
        }
        else if (webjeaPrefixStripped)
        {
            // Only the prefix was legacy; re-encode the remainder so decoded
            // characters (spaces etc.) stay valid in the Location header.
            target = new PathString(path).ToUriComponent();
        }

        if (target is null)
        {
            return _next(context);
        }

        if (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method))
        {
            context.Response.StatusCode = StatusCodes.Status301MovedPermanently;
            context.Response.Headers.Location = request.PathBase.ToUriComponent() + target + request.QueryString;
        }
        else
        {
            // A failed integration must fail loudly, not appear to work while its
            // payload is dropped: pages accept GET/HEAD only, the API handles POST.
            context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            context.Response.Headers.Allow = "GET, HEAD";
        }

        return Task.CompletedTask;
    }
}
