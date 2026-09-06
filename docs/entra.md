# Entra ID Authentication

Entra ID mode uses OpenID Connect (OIDC) to authenticate users via your Entra ID
(Azure AD) tenant, with authorization via Entra groups, individual users, or app roles.
This mode works on both Windows (container and server) and Linux.

## Configuration

```json
{
  "Authentication": { "Mode": "Entra", "Entra": { "EnableGraph": false, "ResolveGroupNamesViaGraph": false } },
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "<tenant-guid>",
    "ClientId": "<app-registration-client-id>",
    "ClientSecret": "<secret>",
    "CallbackPath": "/signin-oidc"
  }
}
```

- `EnableGraph`: `true` to use Microsoft Graph for group lookups (required for resolving
  group display names or handling users in >200 groups). Requires a client secret or
  certificate and delegated permission `GroupMember.Read.All`.
- `ResolveGroupNamesViaGraph`: `true` to allow group display names in `PermittedGroups`
  (requires `EnableGraph: true`). Without this, use group object IDs (GUIDs).

Any of these can also be set as environment variables using `__` separators
(e.g. `AzureAd__ClientId`), which is how the [Docker images](docker.md) are configured.

## App registration setup

WebJEA needs one app registration in your tenant. You can reuse it across
environments (dev, staging, production) by adding one redirect URI per host.

### Step 1: Create the app registration

1. Sign in to [entra.microsoft.com](https://entra.microsoft.com) (or
   [portal.azure.com](https://portal.azure.com)) as a user who can create app
   registrations (Application Developer role or higher).
2. Navigate to **Entra ID** → **App registrations** → **+ New registration**.
3. **Name**: e.g., "WebJEA". Users may see this name on the consent/sign-in page.
4. **Supported account types**: leave as **Accounts in this organizational directory
   only** (single tenant). WebJEA authorizes against your tenant's groups and roles;
   multi-tenant sign-in is not supported.
5. Under **Redirect URI**, select **Web** and enter `https://<your-host>/signin-oidc` —
   the exact public host name users will browse to. The path must match
   `AzureAd:CallbackPath` (default `/signin-oidc`).
6. Click **Register**.
7. On the **Overview** page, copy the **Application (client) ID** → `AzureAd:ClientId`
   and the **Directory (tenant) ID** → `AzureAd:TenantId`.

If WebJEA sits behind a reverse proxy or load balancer, the redirect URI is the
**external** URL users see, and the proxy must forward `X-Forwarded-Proto`/`X-Forwarded-For`
(see [docker.md](docker.md#configuration)). Additional environments: **Authentication** →
**+ Add URI**, one per host. Entra requires `https://` for all redirect URIs (only
`http://localhost` is exempt).

### Step 2: Enable ID tokens

1. Go to **Authentication** → scroll to **Implicit grant and hybrid flows**.
2. Check **ID tokens (used for implicit and hybrid flows)**.
3. Click **Save**.

Without this, sign-in fails with `AADSTS700054: response_type 'id_token' is not enabled
for the application` unless Graph token acquisition is configured. Checking it is safe
in all configurations.

### Step 3: Add the groups claim

Required only for group-based authorization (skip if you use app roles or individual
user grants exclusively):

1. Go to **Token configuration** → **+ Add groups claim**.
2. Under "Which groups associated with the user should be returned in the claim?",
   select **Security groups**.
3. Click **Add**.

This makes Entra include the user's group **object IDs** in the sign-in token as the
`groups` claim, which WebJEA matches against `PermittedGroups`. (Equivalently, set
`"groupMembershipClaims": "SecurityGroup"` in the app manifest.)

### Step 4: Create a client secret (or certificate)

Required only when `EnableGraph: true`; skip otherwise.

1. Go to **Certificates & secrets** → **Client secrets** → **+ New client secret**.
2. Enter a description (e.g., "WebJEA prod") and an expiration (e.g., 12–24 months —
   put a reminder in your calendar; sign-in breaks when it lapses).
3. Click **Add** and **copy the Value column immediately** (not the Secret ID) — it is
   never shown again. It becomes `AzureAd:ClientSecret`.

Keep the secret out of version control: use environment variables, a compose `.env`
file, or a secret store. For production, a **certificate** uploaded under
**Certificates** is stronger than a shared secret.

### Step 5: (Optional) Grant Microsoft Graph permission

Only needed for `EnableGraph: true` (group overage handling and/or
`ResolveGroupNamesViaGraph`):

1. Go to **API permissions** → **+ Add a permission** → **Microsoft Graph** →
   **Delegated permissions**.
2. Search for and select **GroupMember.Read.All**.
3. Click **Add permissions**.
4. Click **Grant admin consent for [Your Tenant]** (requires a privileged admin;
   without consent, every user would be prompted and the permission requires admin
   consent anyway).

### Step 6: (Optional) Require assignment

To make Entra reject sign-ins from anyone not explicitly assigned to WebJEA (defense in
depth on top of `PermittedGroups`):

1. Go to **Entra ID** → **Enterprise applications** → your WebJEA app → **Properties**.
2. Set **Assignment required?** to **Yes** and save.
3. Assign the permitted users/groups under **Users and groups** (the same place app
   roles are assigned — see below).

## Authorization modes

`PermittedGroups` (global and per-command in `config.json`) accepts three kinds of
entries. Choose one or combine them. All matching is case-insensitive.

### Mode 1: Entra security groups

Most common. Requires the **groups claim** from
[Step 3](#step-3-add-the-groups-claim) above.

In `config.json`, use the Entra group **object IDs** (the GUID on the group's overview
page):

```json
{
  "PermittedGroups": [
    "550e8400-e29b-41d4-a716-446655440000",
    "550e8400-e29b-41d4-a716-446655440001"
  ]
}
```

The user's group memberships from the token's `groups` claim are checked against these
IDs.

Limitations:

- Group **display names** only work with `ResolveGroupNamesViaGraph: true` (see
  [below](#group-name-resolution-via-display-name)). Without it, a display name is
  treated as an app role value and will never match a group.
- If a user belongs to more than ~200 groups, Entra omits the claim entirely — see
  [Group overage](#group-overage-users-in-200-groups).

### Mode 2: Individual users

Grant access to specific users by **UPN** (user principal name) or **user object ID**:

```json
{
  "PermittedGroups": [
    "alice@contoso.com",
    "6f9619ff-8b86-d011-b42d-00cf4fc964ff"
  ]
}
```

The signed-in user's UPN and object ID are matched against these entries. Useful for
granting a handful of users access without creating a group.

### Mode 3: App roles (recommended)

App roles let you define WebJEA-specific authorization directly in the app registration
— no tenant-wide groups, no GUIDs in `config.json`, and no group-overage problems (app
role claims are always emitted, regardless of how many groups the user is in).

#### Create the roles

1. In your **app registration**, go to **App roles** → **+ Create app role**.
2. Fill in:
   - **Display name**: e.g., "Script Executor" (what admins see when assigning).
   - **Allowed member types**: **Users/Groups**.
   - **Value**: e.g., `ScriptExecutor` — this exact string is what goes in
     `PermittedGroups`. No spaces allowed.
   - **Description**: e.g., "Can run WebJEA scripts".
   - **Do you want to enable this app role?**: checked.
3. Click **Apply**.
4. Repeat per role. A useful pattern is one role per command group, e.g.
   `WebJEA.Admin`, `WebJEA.HR`, `WebJEA.Finance`, mirroring your per-command
   `PermittedGroups`.

#### Assign users and groups to the roles

Assignments live on the **enterprise application** (the service principal), not the app
registration:

1. Go to **Entra ID** → **Enterprise applications** → your WebJEA app →
   **Users and groups** → **+ Add user/group**.
2. Pick the user (or group), pick the role, click **Assign**.
3. Repeat for each user/group + role combination. A user may hold multiple roles.

Notes:

- Assigning **groups** to app roles requires an Entra ID **P1** license or higher, and
  only direct members of the assigned group receive the role — nested group members do
  not.
- Assignment requires an admin (Application Administrator or higher); users cannot
  self-assign.
- No token configuration is needed: assigned role **values** automatically appear in
  the token's `roles` claim at the user's next sign-in.

#### Use the role values in config.json

```json
{
  "PermittedGroups": [ "WebJEA.Admin" ],
  "Commands": [
    {
      "Id": "newuser",
      "DisplayName": "New User",
      "Script": "newuser.ps1",
      "PermittedGroups": [ "WebJEA.HR" ]
    }
  ]
}
```

Users holding `WebJEA.Admin` see everything; users holding only `WebJEA.HR` see just
the New User command.

WebJEA matches the `roles` claim values against `PermittedGroups` entries
(case-insensitively).

Trade-offs versus groups:

- **Pro**: readable config (no GUIDs), per-app scoping, immune to group overage, and
  role assignments are visible in one place (the enterprise app).
- **Con**: role values are free-text — a typo in `config.json` or in the role's
  **Value** silently denies access; there is no "unknown role" warning because WebJEA
  cannot distinguish a role value from a UPN.

### Combining modes

Mix entries freely in a single `PermittedGroups` list:

```json
{
  "PermittedGroups": [
    "550e8400-e29b-41d4-a716-446655440000",
    "alice@contoso.com",
    "ScriptExecutor",
    "*"
  ]
}
```

Matching rules:

- **GUID** entries match the `groups` claim (group object IDs) and the user's own
  object ID.
- **Non-GUID** entries match app role values from the `roles` claim and the signed-in
  user's UPN — or, with `ResolveGroupNamesViaGraph: true`, are first resolved as group
  display names via Graph.
- `*` matches any authenticated user (honored globally and at the command level).

## Group overage (users in >200 groups)

Entra ID omits the `groups` claim when a user belongs to more than ~200 groups and
instead emits an overage marker (`_claim_names`/`_claim_sources`). Without help, WebJEA
cannot see those users' groups (app role and UPN matching still work).

### Option 1: Enable Microsoft Graph

Complete [Step 4](#step-4-create-a-client-secret-or-certificate) and
[Step 5](#step-5-optional-grant-microsoft-graph-permission) above, then set:

```json
{
  "Authentication": { "Mode": "Entra", "Entra": { "EnableGraph": true } }
}
```

When WebJEA detects overage it queries `GET /me/transitiveMemberOf` via Graph to fetch
the user's full group list at sign-in. Trade-off: adds latency at sign-in (typically
<500 ms) and requires the app to hold credentials.

### Option 2: Use app roles

App role assignments never overage — the `roles` claim is always emitted. See
[Mode 3](#mode-3-app-roles-recommended).

If neither is in place, overage users fail group-based authorization and a warning is
logged.

## Group name resolution via display name

By default, group entries in `PermittedGroups` must be object IDs (GUIDs). To use
display names instead:

```json
{
  "Authentication": { "Mode": "Entra", "Entra": { "EnableGraph": true, "ResolveGroupNamesViaGraph": true } }
}
```

At config load, WebJEA queries Microsoft Graph for a group matching each non-GUID entry
and uses its object ID; entries that match no group fall back to app-role/UPN literal
matching.

**Trade-offs:** config load is slower (one Graph request per non-GUID entry), and
display names are not unique in Entra — two groups can share a name.

## WEBJEAUSERNAME

In Entra mode, scripts receive the **UPN** (`user@tenant.com`) in `WEBJEAUSERNAME`
instead of `DOMAIN\user`. Audit logs use the same value.

If your scripts parse `DOMAIN\user`, update them before switching to Entra mode.

## Related

- [Authentication overview](authentication.md)
- [Windows mode](windows.md) for on-premises deployments
- [Development auto-login](dev.md) for local testing
- [Docker](docker.md) for container deployment and environment-variable configuration
