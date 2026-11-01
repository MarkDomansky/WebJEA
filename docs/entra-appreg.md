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
| Client credential: certificate subject (recommended) **or** client secret **Value** | [Step 4](#step-4-add-a-client-credential) | `appsettings.Production.json` `AzureAd:ClientCredentials` (certificate) or `AzureAd:ClientSecret` |
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

## Step 4: Add a client credential

WebJEA proves its identity to Entra with a client credential when it calls Microsoft Graph ([Step 5](#step-5-grant-microsoft-graph-permission)). Choose **one**:

| | [Option A: Certificate](#option-a-certificate-recommended) | [Option B: Client secret](#option-b-client-secret) |
|---|---|---|
| Recommended for | Production | Labs, quick evaluation |
| What WebJEA holds | A private key that never leaves the server | A shared password |
| Renewal | Import a new certificate with the same subject; no config change | Paste a new value into the config |

Set only one of them. If both are configured, the certificate is used.

### Option A: Certificate (recommended)

A self-signed certificate is fine. Entra only checks that the public key matches one uploaded to the app registration.

1. **Create the certificate.**

   **Windows service:** on the WebJEA server, in an elevated PowerShell:

   ```powershell
   $cert = New-SelfSignedCertificate -Subject 'CN=WebJEA-Graph' -CertStoreLocation 'Cert:\LocalMachine\My' `
       -KeySpec Signature -KeyAlgorithm RSA -KeyLength 2048 -KeyExportPolicy NonExportable `
       -NotAfter (Get-Date).AddYears(2)
   Export-Certificate -Cert $cert -FilePath .\WebJEA-Graph.cer
   ```

   **Docker:** create a PFX file instead, e.g. with OpenSSL:

   ```bash
   openssl req -x509 -newkey rsa:2048 -nodes -keyout graph.key -out WebJEA-Graph.cer -subj "/CN=WebJEA-Graph" -days 730
   openssl pkcs12 -export -inkey graph.key -in WebJEA-Graph.cer -out graph.pfx
   rm graph.key
   ```

2. **Upload the public key.** In the app registration, go to **Certificates & secrets** → **Certificates** → **Upload certificate**, select `WebJEA-Graph.cer`, and click **Add**. The `.cer` holds only the public key, so it's safe to copy around.

3. **Let the service account read the private key** (Windows service only). Windows grants only SYSTEM and Administrators access to a machine certificate's private key.
   - **GUI:** run `certlm.msc` → **Personal** → **Certificates** → right-click **WebJEA-Graph** → **All Tasks** → **Manage Private Keys** → **Add** the WebJEA service account (for a gMSA, include the trailing `$` and select **Service Accounts** under **Object Types**) → allow **Read** only.
   - **PowerShell**, for a certificate created with the command above:

     ```powershell
     $account = 'CONTOSO\gmsa-webjea$'   # the WebJEA service account
     $cert = Get-ChildItem Cert:\LocalMachine\My | Where-Object Subject -eq 'CN=WebJEA-Graph' |
         Sort-Object NotBefore -Descending | Select-Object -First 1
     $key  = [System.Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($cert)
     $file = "$env:ProgramData\Microsoft\Crypto\Keys\$($key.Key.UniqueName)"
     $acl  = Get-Acl $file
     $acl.AddAccessRule([System.Security.AccessControl.FileSystemAccessRule]::new($account, 'Read', 'Allow'))
     Set-Acl $file $acl
     ```

   > [!IMPORTANT]
   > `Deploy.ps1` grants this permission only for the **HTTPS** certificate (`CertSubject`). It doesn't know about the Graph certificate, so you have to grant it yourself, including after every renewal, because a renewed certificate has a new key file. Without it, Graph calls fail with `Keyset does not exist` ([Troubleshooting](entra.md#troubleshooting)).

4. **Record the subject** (`CN=WebJEA-Graph`). It becomes `AzureAd:ClientCredentials` (see [Configure WebJEA](entra.md#configure-webjea)). For Docker, keep `graph.pfx` and its password out of version control.

**Renewal:** before the certificate expires, create a new one with the **same subject**, upload its `.cer`, and grant key access again. WebJEA uses the newest valid certificate with a matching subject, so the configuration doesn't change; restart WebJEA to switch to the new certificate. For Docker, replace `graph.pfx` and restart the container. Delete the old certificate from the app registration once it has expired.

### Option B: Client secret

1. Go to **Certificates & secrets** → **Client secrets** → **+ New client secret**.
2. Enter a description (e.g., "WebJEA prod") and an expiration (e.g., 12–24 months). Put a reminder in your calendar, because Graph lookups break when it lapses.
3. Click **Add** and **copy the Value column immediately** (not the Secret ID). It is never shown again. It becomes `AzureAd:ClientSecret`.

Keep the secret out of version control: use environment variables, a compose `.env` file, or a secret store.

## Step 5: Grant Microsoft Graph permission

WebJEA uses Graph, authenticating with the credential from [Step 4](#step-4-add-a-client-credential), to look up a user's groups when they're missing from the token (see the note below).

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
