# Entra App Registration

WebJEA needs one app registration in your Entra ID tenant. This page walks through creating it. All five steps are required.

App roles, groups assigned to the application, and required assignment need extra steps that are covered in [Advanced Entra Options](entra-advanced.md). Do those after this page.

> [!TIP]
> You can reuse one registration across environments (dev, staging, production) by adding one redirect URI per host.

## Before you start

You'll need:

- **The host name(s) users browse to.** For example, `webjea.contoso.com` or each `SiteFQDNs` entry from the Windows installer. Each one becomes a redirect URI: `https://<host>/signin-oidc`. Entra requires `https://`; only `http://localhost` is exempt. Behind a reverse proxy or load balancer, use the **external** host name.
- **Admin roles in the tenant:**
  - **Application Developer** (or higher) to create the app registration.
  - **Privileged Role Administrator**, **Cloud Application Administrator**, or **Application Administrator** to grant admin consent for Microsoft Graph ([Step 5](#step-5-grant-microsoft-graph-permission)).

## What you'll collect

Write these down as you go. They go into `appsettings.Production.json` (or environment variables) and `webjea.json`. See [Configure WebJEA](entra.md#configure-webjea).

| Value | Where you get it | Goes into |
|---|---|---|
| Directory (tenant) ID | [Step 1](#step-1-create-the-app-registration), **Overview** page | `appsettings.Production.json` `AzureAd:TenantId` |
| Application (client) ID | [Step 1](#step-1-create-the-app-registration), **Overview** page | `appsettings.Production.json` `AzureAd:ClientId` |
| Client secret **Value** | [Step 4](#step-4-create-a-client-secret) | `appsettings.Production.json` `AzureAd:ClientSecret` |
| Group object IDs | **Entra ID** → **Groups** → the group → **Overview** → **Object ID** | `webjea.json` `PermittedGroups` |

## Step 1: Create the app registration

1. Sign in to [entra.microsoft.com](https://entra.microsoft.com).
2. Navigate to **Entra ID** → **App registrations** → **+ New registration**.
3. **Name**: e.g., "WebJEA". Users may see this name on the sign-in page.
4. **Supported account types**: leave as **Accounts in this organizational directory only** (single tenant). WebJEA authorizes against your tenant's groups and roles; multi-tenant sign-in is not supported.
5. Under **Redirect URI**, select **Web** and enter `https://<your-host>/signin-oidc`, using the exact host name users browse to.
6. Click **Register**.
7. On the **Overview** page, copy the **Application (client) ID** → `AzureAd:ClientId` and the **Directory (tenant) ID** → `AzureAd:TenantId`.

For more hosts or environments, go to **Authentication** → **+ Add URI** and add one per host. Behind a reverse proxy, the proxy must also forward `X-Forwarded-Proto`/`X-Forwarded-For` (see [docker.md](docker.md#configuration)).

## Step 2: Enable ID tokens

1. Go to **Authentication** → scroll to **Implicit grant and hybrid flows**.
2. Check **ID tokens (used for implicit and hybrid flows)**.
3. Click **Save**.

> [!WARNING]
> Without this, sign-in can fail with `AADSTS700054: response_type 'id_token' is not enabled for the application`.

## Step 3: Add the groups claim

This puts the user's groups in the sign-in token, where WebJEA matches them against `PermittedGroups`.

1. Go to **Token configuration** → **+ Add groups claim**. If a `groups` claim already exists, click its **⋯** → **Edit** instead.
2. Select **Security groups**. This sends every security group the user belongs to, **including nested groups**.
3. Expand **ID** and leave the format as **Group ID** (the default). WebJEA matches group object IDs, and reads the ID token, so the Access and SAML settings don't apply.
4. Leave **Emit groups as role claims** unchecked. It would hide app roles from the token.
5. Click **Add** (or **Save**). Users get the new claim at their next sign-in.

## Step 4: Create a client secret

WebJEA uses the secret to call Microsoft Graph ([Step 5](#step-5-grant-microsoft-graph-permission)).

1. Go to **Certificates & secrets** → **Client secrets** → **+ New client secret**.
2. Enter a description (e.g., "WebJEA prod") and an expiration (e.g., 12–24 months). Put a reminder in your calendar, because Graph lookups break when it lapses.
3. Click **Add** and **copy the Value column immediately** (not the Secret ID). It is never shown again. It becomes `AzureAd:ClientSecret`.

Keep the secret out of version control: use environment variables, a compose `.env` file, or a secret store. For production, a **certificate** uploaded under **Certificates** is stronger than a shared secret.

## Step 5: Grant Microsoft Graph permission

WebJEA uses Graph to look up a user's groups when they're missing from the token (see the note below).

1. Go to **API permissions** → **+ Add a permission** → **Microsoft Graph** → **Delegated permissions**.
2. Search for and select **GroupMember.Read.All**.
3. Click **Add permissions**.
4. Click **Grant admin consent for [Your Tenant]**.
5. Confirm the **Status** column shows a green check and **Granted for [Your Tenant]**.

> [!IMPORTANT]
>
> - **Why Graph is required: the ~200-group limit.** Entra omits the `groups` claim entirely for a user in more than 200 groups, counting nested groups. Without Graph, every group entry fails for that user, even groups they are a direct member of. This happens silently, and large or long-lived tenants hit it more often than you might expect. With Graph, WebJEA fetches the user's full membership instead. See [Group overage](entra.md#group-overage-users-in-more-than-200-groups).
> - **Admin consent is required, not optional.** WebJEA acquires the Graph token silently in the background, so users never see a consent prompt, and `GroupMember.Read.All` needs admin consent anyway. Without consent, sign-in still succeeds, but every Graph call fails and WebJEA quietly falls back to token-only matching. Users then see an empty or partial menu. See [Troubleshooting](entra.md#troubleshooting).

## Next

Put the values you collected into WebJEA's configuration. See [Configure WebJEA](entra.md#configure-webjea).
