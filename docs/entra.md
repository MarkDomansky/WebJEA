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

For the most robust, performant setup, use **group object IDs**, **UPNs**, and **`*`**. These need nothing beyond the [app registration](entra-appreg.md): no premium license and no extra Entra configuration. Group entries include nested group membership, and Microsoft Graph covers users in more than 200 groups. With a client credential configured, group changes and disabled or revoked accounts take effect **before the user's next script run** ([Near-real-time revocation](#near-real-time-revocation)); without one, they take effect at the user's next sign-in, because claims are fixed in the sign-in token. Matching is case-insensitive.

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

WebJEA looks up each entry that isn't a GUID or a UPN in Microsoft Graph, using the signed-in user's token, and matches the object ID of the group it finds. This is on by default (`ResolveGroupNamesViaGraph: true`). Entries that match no group fall back to app role matching. Three kinds of entry are never looked up: UPN-shaped entries (`user@domain.tld`), Windows-style entries containing `\` (`DOMAIN\group`, `.\Administrators`), and entries starting with the [`AppRolePrefix`](#configure-webjea), if you set one.

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
- **Non-GUID** entries match app role values from the `roles` claim and the user's UPN. With `ResolveGroupNamesViaGraph: true` (the default), entries that aren't UPNs, don't contain `\` and don't start with `AppRolePrefix` are first looked up as [group display names](#group-display-names).
- `*` matches any authenticated user.

## Configure WebJEA

Put the values from the [app registration](entra-appreg.md#what-youll-collect) in `appsettings.Production.json` in the site folder. With a **certificate** ([Option A](entra-appreg.md#option-a-certificate-recommended), recommended):

```json
{
  "Authentication": { "Mode": "Entra", "Entra": { "EnableGraph": true, "ResolveGroupNamesViaGraph": true, "AppRolePrefix": "WebJEA." } },
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "<tenant-guid>",
    "ClientId": "<app-registration-client-id>",
    "ClientCredentials": [
      {
        "SourceType": "StoreWithDistinguishedName",
        "CertificateStorePath": "LocalMachine/My",
        "CertificateDistinguishedName": "CN=WebJEA-Graph"
      }
    ]
  }
}
```

With a **client secret** ([Option B](entra-appreg.md#option-b-client-secret)), replace `ClientCredentials` with:

```json
    "ClientSecret": "<client-secret-value>"
```

`ClientCredentials` is Microsoft.Identity.Web's standard credential list, so other certificate sources work too:

| `SourceType` | Other keys | Use |
|---|---|---|
| `StoreWithDistinguishedName` | `CertificateStorePath`, `CertificateDistinguishedName` (exact subject, e.g. `CN=WebJEA-Graph`) | Windows service. The newest valid matching certificate is used, so renewals need no config change. |
| `StoreWithThumbprint` | `CertificateStorePath`, `CertificateThumbprint` | Windows, pinned to one certificate. Update it on every renewal. |
| `Path` | `CertificateDiskPath`, `CertificatePassword` | Docker: a `.pfx` file mounted read-only ([Docker](docker.md#entra-id-app-registration-setup)). |
| `Base64Encoded` | `Base64EncodedValue` (the whole `.pfx`, Base64), `CertificatePassword` | Docker or a secret store that injects environment variables. |

WebJEA logs which credential it found at startup (`Entra: Graph client credential = certificate (StoreWithDistinguishedName)`), and warns if `EnableGraph` is on but none is configured. It also logs whether [revalidation before each script run](#near-real-time-revocation) is on.

- `EnableGraph`: `true` (the default) lets WebJEA look up the groups of users in more than 200 groups ([Group overage](#group-overage-users-in-more-than-200-groups)), resolve [group display names](#group-display-names), and, with a client credential, [revalidate users before each script run](#near-real-time-revocation). It needs a client credential and the Graph permission from [App registration Steps 4–5](entra-appreg.md#step-4-add-a-client-credential). Set it to `false` only if you have no client credential; group display names, overage handling and revalidation then stop working.
- `ResolveGroupNamesViaGraph`: `true` (the default) allows [group display names](#group-display-names) in `PermittedGroups`. Object IDs, UPNs, entries containing `\` and `*` are never looked up. Set it to `false` if you don't use display names.
- `AppRolePrefix`: entries starting with this prefix (case-insensitive) are treated as [app role](entra-advanced.md#app-roles) values and never looked up in Graph. There is no default. We recommend `WebJEA.`, with every app role value named to match (`WebJEA.HR`, `WebJEA.Admin`). **Groups can't use the prefix:** a group whose display name starts with it can only be referenced by object ID.
- `RevalidateCacheSeconds`: how long a successful [revalidation](#near-real-time-revocation) is reused for the same user. Default `300` (5 minutes). `0` checks with Graph on every script run.
- `RevalidateFailOpen`: what happens when Graph can't be reached during revalidation. `false` (the default) refuses to run the script. `true` runs it on the user's sign-in groups instead and logs a warning.

Any of these can also be set as environment variables using `__` separators, which is how the [Docker images](docker.md) are configured. `ClientCredentials` entries are numbered from `0` (e.g. `AzureAd__ClientCredentials__0__SourceType=Path`), and a secret is `AzureAd__ClientSecret`. Keep client secrets, PFX files and PFX passwords out of version control.

The `AzureAd` section name comes from Microsoft.Identity.Web, the library WebJEA uses for sign-in, and matches Microsoft's own documentation and samples.

## Group overage (users in more than 200 groups)

When a user belongs to more than ~200 groups, counting nested groups, Entra omits the `groups` claim entirely and sends an overage marker instead. With `EnableGraph: true`, WebJEA detects this and calls `GET /me/transitiveMemberOf` on each page and API request to fetch the user's full list of group object IDs. That adds some latency to each request (typically under 500 ms).

Without Graph, WebJEA can't see any of that user's groups, even ones they're a direct member of, and logs a warning. UPN, app role and `*` entries still work. Turning Graph off can also lower the limit to as few as 5 groups ([Troubleshooting](#troubleshooting)), so keep the default `EnableGraph: true` and complete the [app registration](entra-appreg.md).

## Near-real-time revocation

Sign-in claims are fixed for the life of the user's session, which is typically about a day. Without a check, a user who is disabled, has their sessions revoked, or is removed from a group could keep running scripts until the session ends. So when `EnableGraph` is on **and** a client credential is configured, WebJEA checks the user with Microsoft Graph immediately before every script run. There is nothing to turn on.

The check is one call to `GET /me/transitiveMemberOf`, made with the user's own token. It does two things:

- **Proves the sign-in is still valid.** WebJEA declares itself capable of [Continuous Access Evaluation](https://learn.microsoft.com/entra/identity/conditional-access/concept-continuous-access-evaluation) (`cp1`), so Graph rejects the token in near real time after a critical event: the account is disabled or deleted, its sessions are revoked, its password is reset, high user risk is detected, or the user signs in from an IP address that a location-based Conditional Access policy blocks. WebJEA then signs the user out, refuses the script, and shows **Please sign in again.** with a Reload button.
- **Fetches the user's current groups.** These groups, not the ones in the sign-in token, decide whether the script may run. A user added to a group can run its commands straight away. A user removed from a group sees **Your access has changed.**, and reloading the page updates their menu.

A successful check is reused for `RevalidateCacheSeconds` (default 5 minutes), so the delay before a change takes effect is the time Entra takes to send the event (usually a few minutes) plus up to that long.

If Graph can't be reached, is throttling, or denies access (for example, admin consent is missing), the script is refused with **Couldn't verify your sign-in.** and the log says why. To run scripts on the sign-in groups instead, set `RevalidateFailOpen: true`.

Limitations:

- **App roles** come from the sign-in token. A change to a user's role assignment takes effect at their next sign-in. Disabled or revoked users are still blocked.
- **The menu and command pages** use the groups from the user's last revalidation while it's cached, and otherwise use the token. They don't call Graph themselves.
- **After WebJEA restarts**, the first script run asks every user to sign in again, because tokens are cached in memory. Reload signs them back in without a prompt.
- **Without a client credential**, or with `EnableGraph: false`, there is no check. Startup logs `Entra: sign-in revalidation before execute is off`.

## Switching from Windows mode

By default, Windows-style `PermittedGroups` entries never match in Entra mode. That includes `DOMAIN\group`, `.\Administrators` (used by the starter `webjea.json`), `BUILTIN\...` and SIDs, because the `groups` claim carries Entra object IDs, not AD names. Entries containing `\` are skipped without a Graph lookup, so they cost nothing, but they still never match. Before switching `Authentication:Mode` to `Entra`, replace them with group object IDs or UPNs. For groups synced from on-premises AD, use the synced group's object ID in Entra.

## WEBJEAUSERNAME

In Entra mode, scripts receive the **UPN** (`user@tenant.com`) in `WEBJEAUSERNAME` instead of `DOMAIN\user`. Audit logs use the same value.

If your scripts parse `DOMAIN\user`, update them before switching to Entra mode.

## Troubleshooting

Entra errors are written to the WebJEA log. Most authorization problems show up as a partial menu, or as the "Not available" message instead of a menu. The log records why ([What users see when something goes wrong](Usage.md#what-users-see-when-something-goes-wrong)).

| Log message | Cause | Fix |
|---|---|---|
| `Entra: Microsoft Graph denied access (AADSTS65001)` (Warn, logged once until Graph works again), possibly alongside `IDW10502: An MsalUiRequiredException was thrown` | `GroupMember.Read.All` has not been granted admin consent. Group display names and the overage lookup are skipped until it is, and scripts are refused with "Couldn't verify your sign-in." ([Near-real-time revocation](#near-real-time-revocation)). | Grant admin consent ([Step 5](entra-appreg.md#step-5-grant-microsoft-graph-permission)), then have affected users sign out and back in. |
| `AADSTS700054: response_type 'id_token' is not enabled` | ID tokens not enabled on the app registration. | [Step 2](entra-appreg.md#step-2-enable-id-tokens). |
| `AADSTS50011: The redirect URI ... does not match` | The URL users browse to (or the proxy's external URL) isn't registered. | Add `https://<host>/signin-oidc` under **Authentication** ([Step 1](entra-appreg.md#step-1-create-the-app-registration)). |
| `AADSTS7000215: Invalid client secret` | Wrong value copied (Secret ID instead of Value) or the secret expired. | Create a new secret and copy the **Value** column ([Step 4, Option B](entra-appreg.md#option-b-client-secret)). |
| `Entra: Authentication:Entra:EnableGraph is true but no client credential is configured` (at startup) | Neither `AzureAd:ClientCredentials` nor `AzureAd:ClientSecret` is set (or the environment variables didn't reach the app). [Revalidation before each script run](#near-real-time-revocation) is off. | Configure a credential ([Configure WebJEA](#configure-webjea)), or set `EnableGraph: false` if you can't. |
| `Entra: signed out <user> before execute: sign-in no longer valid (<code>)` | The user was disabled, their sessions were revoked, or their password was reset. `user_null` means WebJEA restarted and has no cached token for the user. | None needed. The user reloads and signs in again, or is blocked by Entra. |
| `Entra: couldn't revalidate <user> (<reason>); execute refused.` | Graph couldn't be reached, is throttling (`Graph HTTP 429`), or denied access (`AADSTS65001`: missing admin consent). | Check outbound access to `graph.microsoft.com` and admin consent ([Step 5](entra-appreg.md#step-5-grant-microsoft-graph-permission)). To run scripts anyway during an outage, set `RevalidateFailOpen: true`. |
| `IDW10109: All client certificates passed to the configuration have expired or can't be loaded` | No valid certificate matched: wrong `CertificateDistinguishedName`/thumbprint or store, the certificate expired, or the PFX path or password is wrong. The full message lists the reason for each certificate. | Check the subject matches exactly (`CN=WebJEA-Graph`) and the certificate is in `LocalMachine\My` and not expired. In Docker, check the mount and `CertificatePassword`. |
| `Keyset does not exist` / `CryptographicException` while loading the certificate | The service account can't read the certificate's private key. Common after a renewal, because the new key file has default permissions. | Grant the service account **Read** on the private key ([Step 4, Option A](entra-appreg.md#option-a-certificate-recommended), step 3). In Docker, make the PFX readable by the container's `app` user. |
| `AADSTS700027: The certificate with identifier used to sign the client assertion is not registered on application` | The certificate WebJEA loaded isn't uploaded to the app registration, e.g. a renewed certificate whose `.cer` wasn't uploaded. | Upload the `.cer` under **Certificates & secrets** → **Certificates** ([Step 4, Option A](entra-appreg.md#option-a-certificate-recommended)). |
| `EntraUserContext: group overage detected ... EnableGraph is false` | Graph is turned off, so the token can carry only a limited number of groups. A user in more than ~200 groups gets no `groups` claim. With Graph off, sign-in can also fall back to the implicit flow, where Entra sends a `hasgroups` marker instead of the groups once a user is in more than **5** groups. | Set `EnableGraph: true` (the default) and complete [App registration Steps 4–5](entra-appreg.md#step-4-add-a-client-credential) (client credential and Graph permission), then have affected users sign out and back in. |
| `EntraGroupResolver: '<entry>' is not a GUID; matching it as an app role value or UPN.` (Trace) | Informational. If `<entry>` was meant to be a group, it didn't resolve. | Use the group's object ID. Windows-style entries never match; see [Switching from Windows mode](#switching-from-windows-mode). |
| Users see "Not available" instead of a menu; the log shows `API: Menu empty for <user>: permitted on 0 of N commands` | The user's token has none of the `PermittedGroups` entries. | Check that the groups claim is configured ([Step 3](entra-appreg.md#step-3-add-the-groups-claim)) and that the user signed in again after the change. With [advanced options](entra-advanced.md), also check assignments on the enterprise application and that the user is a **direct** member of any assigned group. |

## Related

- [Entra App Registration](entra-appreg.md): step-by-step portal setup
- [Advanced Entra Options](entra-advanced.md): app roles, groups assigned to the application, required assignment
- [Authentication overview](authentication.md)
- [Windows mode](windows.md) for on-premises deployments
- [Development auto-login](dev.md) for local testing
- [Docker](docker.md) for container deployment and environment-variable configuration
