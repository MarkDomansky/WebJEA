# Entra ID Authentication

Entra ID mode uses OpenID Connect (OIDC) to sign users in through your Entra ID (Azure AD) tenant. WebJEA then authorizes them against the `PermittedGroups` entries in `webjea.json` (global and per-command). This mode works on Windows servers and Linux containers.

Setup has three parts:

1. [Create the app registration](entra-appreg.md) in Entra.
2. [Configure WebJEA](#configure-webjea) with the values from the app registration.
3. [Write `PermittedGroups` entries](#authorizing-users) in `webjea.json`.

## Authorizing users

`PermittedGroups` supports these entry types, and you can mix them freely in one list:

| Entry | Example |
|---|---|
| [Group, by object ID](#group-object-ids) | `550e8400-e29b-41d4-a716-446655440000` |
| [Group, by display name](#group-display-names) | `HR Admins` |
| [App role](entra-advanced.md#app-roles) | `WebJEA.HR` |
| [Individual user, by UPN or object ID](#individual-users) | `alice@contoso.com` `dee17057-2d28-4afc-9cdf-d9bd5f226b7c` |
| [Any signed-in user](#any-signed-in-user) | `*` |

### Recommended entries

For the most robust, performant setup, use **group object IDs**, **UPNs**, and **`*`**. These need nothing beyond the [app registration](entra-appreg.md): no premium license and no extra Entra configuration. Group entries include nested group membership, and Microsoft Graph covers users in more than 200 groups. Changes in Entra take effect at the user's **next sign-in**, because claims are fixed in the sign-in token. Matching is case-insensitive.

[Group display names](#group-display-names) also work out of the box, but each one costs a Graph lookup on every request.

App roles and the other options in [Advanced Entra Options](entra-advanced.md) also work, but they need more setup, and some need an Entra ID P1/P2 license.

### Group object IDs

Use the group's **object ID** (the GUID on the group's overview page in Entra):

```json
{
  "PermittedGroups": [
    "550e8400-e29b-41d4-a716-446655440000",
    "550e8400-e29b-41d4-a716-446655440001"
  ]
}
```

This works for every group, cloud-only or synced, and includes nested groups.

### Group display names

Write the group's **display name** instead of its object ID:

```json
{ "PermittedGroups": [ "HR Admins" ] }
```

WebJEA looks up each entry that isn't a GUID or a UPN in Microsoft Graph, using the signed-in user's token, and matches the object ID of the group it finds. This is on by default (`ResolveGroupNamesViaGraph: true`). Entries that match no group fall back to app role matching. Two kinds of entry are never looked up: UPN-shaped entries (`user@domain.tld`), and entries starting with the [`AppRolePrefix`](#configure-webjea), if you set one.

Trade-offs:

- `webjea.json` is re-read on **every request**, and lookups are not cached, so each non-GUID entry (global and per-command) costs one Graph round trip per page load. With several entries this can add up to a second or more per request.
- App role values are looked up too, and never match a display name, unless they start with `AppRolePrefix`.
- If you set `AppRolePrefix`, a group whose display name starts with that prefix can't be used by display name. It's treated as an app role and never looked up. Use its object ID instead.
- Display names are not unique in Entra. If two groups share a name, the first match wins.

Prefer group object IDs. If you don't use display names, set `ResolveGroupNamesViaGraph: false` to skip the lookups.

### Individual users

Grant access to specific users by **UPN** or **user object ID**:

```json
{
  "PermittedGroups": [
    "alice@contoso.com",
    "6f9619ff-8b86-d011-b42d-00cf4fc964ff"
  ]
}
```

Useful for a handful of users without creating a group.

### Any signed-in user

`*` matches any authenticated user, globally or per command. By default that's **anyone in your tenant**. To narrow it to users you've assigned to WebJEA, see [Require assignment](entra-advanced.md#require-assignment).

```json
{
  "PermittedGroups": [ "550e8400-e29b-41d4-a716-446655440000" ],
  "Commands": [
    { "Id": "status",  "PermittedGroups": [ "*" ] },
    { "Id": "newuser", "PermittedGroups": [ "alice@contoso.com" ] }
  ]
}
```

### How entries are matched

- **GUID** entries match group object IDs in the token's `groups` claim, and the user's own object ID.
- **Non-GUID** entries match app role values from the `roles` claim and the user's UPN. With `ResolveGroupNamesViaGraph: true` (the default), entries that aren't UPNs and don't start with `AppRolePrefix` are first looked up as [group display names](#group-display-names).
- `*` matches any authenticated user.

## Configure WebJEA

Put the values from the [app registration](entra-appreg.md#what-youll-collect) in `appsettings.Production.json` in the site folder:

```json
{
  "Authentication": { "Mode": "Entra", "Entra": { "EnableGraph": true, "ResolveGroupNamesViaGraph": true, "AppRolePrefix": "WebJEA." } },
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "<tenant-guid>",
    "ClientId": "<app-registration-client-id>",
    "ClientSecret": "<client-secret-value>"
  }
}
```

- `EnableGraph`: `true` (the default) lets WebJEA look up the groups of users in more than 200 groups ([Group overage](#group-overage-users-in-more-than-200-groups)) and resolve [group display names](#group-display-names). It needs the client secret and the Graph permission from [App registration Steps 4–5](entra-appreg.md#step-4-create-a-client-secret). Set it to `false` only if you have no client secret; group display names and overage handling then stop working.
- `ResolveGroupNamesViaGraph`: `true` (the default) allows [group display names](#group-display-names) in `PermittedGroups`. Object IDs, UPNs and `*` are never looked up. Set it to `false` if you don't use display names.
- `AppRolePrefix`: entries starting with this prefix (case-insensitive) are treated as [app role](entra-advanced.md#app-roles) values and never looked up in Graph. There is no default. We recommend `WebJEA.`, with every app role value named to match (`WebJEA.HR`, `WebJEA.Admin`). **Groups can't use the prefix:** a group whose display name starts with it can only be referenced by object ID.

Any of these can also be set as environment variables using `__` separators (e.g. `AzureAd__ClientSecret`), which is how the [Docker images](docker.md) are configured. Keep `ClientSecret` out of version control.

The `AzureAd` section name comes from Microsoft.Identity.Web, the library WebJEA uses for sign-in, and matches Microsoft's own documentation and samples.

## Group overage (users in more than 200 groups)

When a user belongs to more than ~200 groups, counting nested groups, Entra omits the `groups` claim entirely and sends an overage marker instead. With `EnableGraph: true`, WebJEA detects this and calls `GET /me/transitiveMemberOf` at sign-in to fetch the user's full list of group object IDs. That adds some latency at sign-in (typically under 500 ms).

Without Graph, WebJEA can't see any of that user's groups, even ones they're a direct member of, and logs a warning. UPN, app role and `*` entries still work. Turning Graph off can also lower the limit to as few as 5 groups ([Troubleshooting](#troubleshooting)), so keep the default `EnableGraph: true` and complete the [app registration](entra-appreg.md).

## Switching from Windows mode

By default, Windows-style `PermittedGroups` entries never match in Entra mode. That includes `DOMAIN\group`, `.\Administrators` (used by the starter `webjea.json`), `BUILTIN\...` and SIDs, because the `groups` claim carries Entra object IDs, not AD names. Before switching `Authentication:Mode` to `Entra`, replace them with group object IDs or UPNs. For groups synced from on-premises AD, use the synced group's object ID in Entra.

## WEBJEAUSERNAME

In Entra mode, scripts receive the **UPN** (`user@tenant.com`) in `WEBJEAUSERNAME` instead of `DOMAIN\user`. Audit logs use the same value.

If your scripts parse `DOMAIN\user`, update them before switching to Entra mode.

## Troubleshooting

Entra errors are written to the WebJEA log. Most authorization problems show up as an empty or partial menu rather than an error page.

| Log message | Cause | Fix |
|---|---|---|
| `AADSTS65001: The user or administrator has not consented to use the application`, followed by `IDW10502: An MsalUiRequiredException was thrown` and `GraphGroupLoader: ... failed` | `GroupMember.Read.All` has not been granted admin consent. | Grant admin consent ([Step 5](entra-appreg.md#step-5-grant-microsoft-graph-permission)), then have affected users sign out and back in. |
| `AADSTS700054: response_type 'id_token' is not enabled` | ID tokens not enabled on the app registration. | [Step 2](entra-appreg.md#step-2-enable-id-tokens). |
| `AADSTS50011: The redirect URI ... does not match` | The URL users browse to (or the proxy's external URL) isn't registered. | Add `https://<host>/signin-oidc` under **Authentication** ([Step 1](entra-appreg.md#step-1-create-the-app-registration)). |
| `AADSTS7000215: Invalid client secret` | Wrong value copied (Secret ID instead of Value) or the secret expired. | Create a new secret and copy the **Value** column ([Step 4](entra-appreg.md#step-4-create-a-client-secret)). |
| `EntraUserContext: group overage detected ... EnableGraph is false` | Graph is turned off, so the token can carry only a limited number of groups. A user in more than ~200 groups gets no `groups` claim. With Graph off, sign-in can also fall back to the implicit flow, where Entra sends a `hasgroups` marker instead of the groups once a user is in more than **5** groups. | Set `EnableGraph: true` (the default) and complete [App registration Steps 4–5](entra-appreg.md#step-4-create-a-client-secret) (client secret and Graph permission), then have affected users sign out and back in. |
| `EntraGroupResolver: '<entry>' is not a GUID; matching it as an app role value or UPN.` (Trace) | Informational. If `<entry>` was meant to be a group, it didn't resolve. | Use the group's object ID. Windows-style entries never match; see [Switching from Windows mode](#switching-from-windows-mode). |
| Menu is empty, no errors | The user's token has none of the `PermittedGroups` entries. | Check that the groups claim is configured ([Step 3](entra-appreg.md#step-3-add-the-groups-claim)) and that the user signed in again after the change. With [advanced options](entra-advanced.md), also check assignments on the enterprise application and that the user is a **direct** member of any assigned group. |

## Related

- [Entra App Registration](entra-appreg.md): step-by-step portal setup
- [Advanced Entra Options](entra-advanced.md): app roles, groups assigned to the application, required assignment
- [Authentication overview](authentication.md)
- [Windows mode](windows.md) for on-premises deployments
- [Development auto-login](dev.md) for local testing
- [Docker](docker.md) for container deployment and environment-variable configuration
