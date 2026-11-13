using Microsoft.Graph;
using Microsoft.Identity.Client;
using Microsoft.Kiota.Abstractions;

namespace WebJEA.Auth.Entra;

public enum GroupLookupStatus
{
    Ok,

    /// <summary>The user's sign-in is no longer valid (revoked, disabled, or no cached token).</summary>
    SignInRequired,

    /// <summary>Graph couldn't answer: consent missing, throttled, down, or unreachable.</summary>
    Unavailable
}

public sealed record GroupLookup(GroupLookupStatus Status, IReadOnlyList<string> Ids, string Detail);

/// <summary>
/// Microsoft Graph lookups for Entra mode: the transitive group membership fallback for
/// token group overage, and optional group display-name resolution.
/// A missing admin consent (AADSTS65001) is logged once per process as a warning rather
/// than as an error per lookup, and the rest of the request's lookups are skipped.
/// </summary>
public class GraphGroupLoader
{
    private NLog.Logger dlog = NLog.LogManager.GetCurrentClassLogger();

    private const string ConsentRequiredCode = "AADSTS65001";

    // 1 once the consent warning has been logged; cleared by the next successful Graph call
    private static int _consentWarned;

    private readonly GraphServiceClient _graph;

    // Scoped per request: after one consent failure, skip the request's remaining lookups
    private bool _consentMissing;

    public GraphGroupLoader(GraphServiceClient graph)
    {
        _graph = graph;
    }

    internal static bool ConsentWarningLogged => Volatile.Read(ref _consentWarned) == 1;

    internal static void ResetConsentWarning()
    {
        Interlocked.Exchange(ref _consentWarned, 0);
    }

    /// <summary>
    /// True when ex, or any exception it wraps, is an AADSTS65001 consent failure. Token
    /// acquisition surfaces it as an MsalUiRequiredException inside Microsoft.Identity.Web's
    /// IDW10502 challenge exception.
    /// </summary>
    internal static bool IsConsentRequired(Exception ex)
    {
        for (Exception e = ex; e != null; e = e.InnerException)
        {
            if (e.Message != null && e.Message.Contains(ConsentRequiredCode)) return true;
        }

        return false;
    }

    public virtual List<string> GetTransitiveGroupIds()
    {
        if (_consentMissing) return new List<string>();

        try
        {
            var ids = QueryTransitiveGroupIds();
            OnGraphSuccess();
            return ids;
        }
        catch (Exception ex)
        {
            if (!HandleConsentFailure(ex))
            {
                dlog.Error("GraphGroupLoader: transitiveMemberOf query failed: " + ex.Message);
            }

            return new List<string>();
        }
    }

    /// <summary>
    /// The user's transitive group IDs, for the pre-execute revalidation. Unlike
    /// GetTransitiveGroupIds, failures are reported rather than swallowed, so the caller
    /// can tell a revoked sign-in from a Graph outage.
    /// </summary>
    public virtual GroupLookup LoadTransitiveGroupIdsForRevalidation()
    {
        if (_consentMissing) return new GroupLookup(GroupLookupStatus.Unavailable, Array.Empty<string>(), ConsentRequiredCode);

        try
        {
            var ids = QueryTransitiveGroupIds();
            OnGraphSuccess();
            return new GroupLookup(GroupLookupStatus.Ok, ids, "");
        }
        catch (Exception ex)
        {
            GroupLookupStatus status = ClassifyFailure(ex);
            if (!HandleConsentFailure(ex) && status == GroupLookupStatus.Unavailable)
            {
                dlog.Error("GraphGroupLoader: revalidation transitiveMemberOf query failed: " + ex.Message);
            }

            return new GroupLookup(status, Array.Empty<string>(), DescribeFailure(ex));
        }
    }

    /// <summary>
    /// Consent failures are Unavailable (a misconfiguration, not a revocation). Any other
    /// MsalUiRequiredException, or a Graph 401 (a CAE claims challenge that couldn't be
    /// satisfied), means the user must sign in again. Everything else is Unavailable.
    /// </summary>
    internal static GroupLookupStatus ClassifyFailure(Exception ex)
    {
        if (IsConsentRequired(ex)) return GroupLookupStatus.Unavailable;

        for (Exception e = ex; e != null; e = e.InnerException)
        {
            if (e is MsalUiRequiredException) return GroupLookupStatus.SignInRequired;
            if (e is ApiException api && api.ResponseStatusCode == 401) return GroupLookupStatus.SignInRequired;
        }

        return GroupLookupStatus.Unavailable;
    }

    private static string DescribeFailure(Exception ex)
    {
        if (IsConsentRequired(ex)) return ConsentRequiredCode;

        for (Exception e = ex; e != null; e = e.InnerException)
        {
            if (e is MsalException msal) return msal.ErrorCode;
            if (e is ApiException api && api.ResponseStatusCode != 0) return "Graph HTTP " + api.ResponseStatusCode;
        }

        return ex.GetType().Name;
    }

    public virtual string FindGroupIdByDisplayName(string displayName)
    {
        if (_consentMissing) return "";

        try
        {
            string id = QueryGroupIdByDisplayName(displayName);
            OnGraphSuccess();
            return id;
        }
        catch (Exception ex)
        {
            if (!HandleConsentFailure(ex))
            {
                dlog.Error("GraphGroupLoader: group lookup for '" + displayName + "' failed: " + ex.Message);
            }

            return "";
        }
    }

    protected virtual List<string> QueryTransitiveGroupIds()
    {
        var ids = new List<string>();
        var page = _graph.Me.TransitiveMemberOf.GraphGroup.GetAsync(cfg =>
        {
            cfg.QueryParameters.Select = new[] { "id" };
            cfg.QueryParameters.Top = 999;
        }).GetAwaiter().GetResult();

        while (page != null)
        {
            if (page.Value != null)
            {
                ids.AddRange(page.Value.Where(g => g.Id != null).Select(g => g.Id));
            }

            if (string.IsNullOrEmpty(page.OdataNextLink)) break;

            page = _graph.Me.TransitiveMemberOf.GraphGroup
                .WithUrl(page.OdataNextLink)
                .GetAsync().GetAwaiter().GetResult();
        }

        return ids;
    }

    protected virtual string QueryGroupIdByDisplayName(string displayName)
    {
        var resp = _graph.Groups.GetAsync(cfg =>
        {
            cfg.QueryParameters.Filter = "displayName eq '" + displayName.Replace("'", "''") + "'";
            cfg.QueryParameters.Select = new[] { "id" };
        }).GetAwaiter().GetResult();

        return resp?.Value?.FirstOrDefault()?.Id ?? "";
    }

    private void OnGraphSuccess()
    {
        if (Interlocked.Exchange(ref _consentWarned, 0) == 1)
        {
            dlog.Info("Entra: Microsoft Graph lookups are working again.");
        }
    }

    /// <summary>
    /// Returns true if ex was a consent failure (and has been logged); false for any other error.
    /// </summary>
    private bool HandleConsentFailure(Exception ex)
    {
        if (!IsConsentRequired(ex)) return false;

        _consentMissing = true;
        if (Interlocked.Exchange(ref _consentWarned, 1) == 0)
        {
            dlog.Warn("Entra: Microsoft Graph denied access (" + ConsentRequiredCode + "): GroupMember.Read.All has not been " +
                      "granted admin consent. Group display names in PermittedGroups and the >200-group overage lookup " +
                      "won't work until it is. Grant admin consent (docs/entra-appreg.md Step 5), then have users sign out " +
                      "and back in.");
        }
        else
        {
            dlog.Debug("GraphGroupLoader: Graph lookup skipped; admin consent still missing (" + ConsentRequiredCode + ").");
        }

        return true;
    }
}
