# API Access

WebJEA's web pages are built on a small JSON API under `/api`. Scripts, scheduled tasks and pipelines can call the same API directly to list the commands they're allowed to run, read a command's parameters, and run it. Calls are authorized exactly like the web UI: the caller must match the command's `PermittedGroups`.

How a non-interactive caller authenticates depends on the [authentication mode](authentication.md):

| Mode | Caller authenticates with | `PermittedGroups` entry |
|---|---|---|
| Windows | Its Windows account (Kerberos/NTLM): `-UseDefaultCredentials` | The account or one of its AD groups |
| Entra | An **app-only** access token from its own app registration (client credentials) | An [app role](entra-appreg.md#optional-api-access-for-automation-app-only) assigned to the calling app |

> [!NOTE]
> In Entra mode, WebJEA's API accepts only app-only tokens. A token issued to a **user** (a delegated token, with an `scp` claim) is rejected. People use the web UI.

## Endpoints

Paths are relative to the site, including any path base: for a sub-site at `https://host/hr`, the menu is `https://host/hr/api/menu`.

| Method and path | Returns |
|---|---|
| `GET /api/menu` | The commands the caller may run: `[{ id, displayName, description, synopsis, uri }]` |
| `GET /api/command/{cmdid}` | One command and its parameters: `{ id, title, displayName, synopsis, description, hasScript, hasOnload, showVerbose, renderMode, parameters: [{ name, type, isMandatory, isMultiValued, allowedValues, defaultValue, helpMessage, validation, ... }] }` |
| `GET /api/config` | Site settings and who WebJEA thinks the caller is: `{ title, version, isGlobalUser, userName, ... }` |
| `POST /api/execute` | Runs a command (see below) |

GET errors return `{ "error": "...", "requestId": "..." }`. The `requestId` appears on every matching line of `webjea.log`.

### Run a command

`POST /api/execute` with `Content-Type: application/json` and this body:

| Property | Required | Meaning |
|---|---|---|
| `cmdid` | Yes | The command's `Id` from `webjea.json` |
| `parameters` | No | An object of parameter name → value. Names are case-insensitive. Use JSON strings, numbers, `true`/`false` for switches, and arrays for multi-valued parameters. Omitted optional parameters keep the script's default. `WEBJEAUSERNAME` and `WEBJEAHOSTNAME` are set by WebJEA and can't be passed. |
| `runOnload` | No | `true` runs the command's `OnloadScript` instead of its `Script` |
| `verbose` | No | `true` includes the verbose stream. Honored only for global users when `ShowVerbose` is on. |

The response is:

```json
{
  "status": 200,
  "statusmessage": "OK",
  "requestId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-00",
  "output": { "Name": "jdoe", "Enabled": true },
  "messages": [ { "stream": "info", "message": "Account updated" } ]
}
```

- `output` is the script's output objects: `null` for none, the object itself for one, or an array for several.
- `messages` holds the other streams in order. `stream` is `output`, `info`, `warn`, `err`, `verbose` or `debug`.
- A script that writes errors still returns HTTP 200, with `statusmessage` set to `Completed with errors in stream.` Check `messages` for `err` entries.

| HTTP status | Meaning |
|---|---|
| 200 | The script ran |
| 400 | Bad body, or parameter validation failed; `messages` lists each problem |
| 401 | No credentials, or the token was rejected ([Troubleshooting](#troubleshooting)) |
| 403 | The command doesn't exist or the caller isn't permitted on it. The response is the same for both; the log says which. |
| 405 | Not a POST |
| 415 | `Content-Type` isn't `application/json` |
| 500 | Server or configuration error; look up the `requestId` in `webjea.log` |

## Entra mode: get an app-only token

First set up the WebJEA app role and the calling app's registration: [API access for automation](entra-appreg.md#optional-api-access-for-automation-app-only). You need:

- `$tenant`: the directory (tenant) ID.
- `$webjeaId`: WebJEA's **Application (client) ID**. The token is requested for `api://<webjeaId>/.default`.
- `$clientId` and a credential of the **calling** app registration.

Tokens last about an hour. Request a new one per run, or when the old one expires.

### With a client secret

```powershell
$tenant   = '<tenant-id>'
$webjeaId = '<webjea-client-id>'
$clientId = '<calling-app-client-id>'
$secret   = '<calling-app-secret>'   # load from a secret store, not the script

$token = (Invoke-RestMethod -Method Post -Uri "https://login.microsoftonline.com/$tenant/oauth2/v2.0/token" -Body @{
    client_id     = $clientId
    client_secret = $secret
    scope         = "api://$webjeaId/.default"
    grant_type    = 'client_credentials'
}).access_token
```

### With a certificate (recommended)

The calling machine holds the certificate's private key, and its public key (`.cer`) is uploaded to the **calling** app registration. The script signs a short-lived client assertion with the key. It works in Windows PowerShell 5.1 and PowerShell 7.

```powershell
$tenant   = '<tenant-id>'
$webjeaId = '<webjea-client-id>'
$clientId = '<calling-app-client-id>'
$cert     = Get-Item 'Cert:\CurrentUser\My\<thumbprint>'   # or Cert:\LocalMachine\My\...

function ConvertTo-Base64Url([byte[]]$Bytes) { [Convert]::ToBase64String($Bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_') }

$tokenUrl = "https://login.microsoftonline.com/$tenant/oauth2/v2.0/token"
$now      = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$header   = @{ alg = 'RS256'; typ = 'JWT'; x5t = ConvertTo-Base64Url $cert.GetCertHash() } | ConvertTo-Json -Compress
$payload  = @{ aud = $tokenUrl; iss = $clientId; sub = $clientId; jti = [guid]::NewGuid().ToString(); nbf = $now; exp = $now + 600 } | ConvertTo-Json -Compress
$unsigned = (ConvertTo-Base64Url ([Text.Encoding]::UTF8.GetBytes($header))) + '.' + (ConvertTo-Base64Url ([Text.Encoding]::UTF8.GetBytes($payload)))
$key       = [Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($cert)
$signature = $key.SignData([Text.Encoding]::UTF8.GetBytes($unsigned), [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)

$token = (Invoke-RestMethod -Method Post -Uri $tokenUrl -Body @{
    client_id             = $clientId
    client_assertion_type = 'urn:ietf:params:oauth:client-assertion-type:jwt-bearer'
    client_assertion      = $unsigned + '.' + (ConvertTo-Base64Url $signature)
    scope                 = "api://$webjeaId/.default"
    grant_type            = 'client_credentials'
}).access_token
```

Any library that supports the client credentials flow works as well, such as MSAL or `Get-AzAccessToken` after `Connect-AzAccount -ServicePrincipal`. Request the resource `api://<webjeaId>`.

## Call the API

With `$token` from above:

```powershell
$webjea  = 'https://webjea.contoso.com'
$headers = @{ Authorization = "Bearer $token" }

# What may this caller run?
Invoke-RestMethod "$webjea/api/menu" -Headers $headers | Format-Table id, displayName

# What does a command take?
$cmd = Invoke-RestMethod "$webjea/api/command/payroll-sync" -Headers $headers
$cmd.parameters | Format-Table name, type, isMandatory, allowedValues

# Run it
$body = @{
    cmdid      = 'payroll-sync'
    parameters = @{ Department = 'Finance'; Force = $true }
} | ConvertTo-Json -Depth 5

$result = Invoke-RestMethod -Method Post "$webjea/api/execute" -Headers $headers -ContentType 'application/json' -Body $body
$result.output
$errors = $result.messages | Where-Object stream -eq 'err'
if ($errors) { throw "payroll-sync reported errors: $($errors.message -join '; ')" }
```

`Invoke-RestMethod` throws on a non-2xx status. In PowerShell 7, add `-SkipHttpErrorCheck -StatusCodeVariable status` to inspect the error body yourself, e.g. a 400's `messages`.

### Windows mode

Windows mode needs no token. The caller runs as an account in `PermittedGroups`, such as a gMSA for a scheduled task, and sends its Windows credentials:

```powershell
Invoke-RestMethod "$webjea/api/menu" -UseDefaultCredentials
Invoke-RestMethod -Method Post "$webjea/api/execute" -UseDefaultCredentials -ContentType 'application/json' -Body $body
```

## What scripts see

In Entra mode, an app caller's `WEBJEAUSERNAME` (and `$env:WebJEAUserName`) is `app:<calling-app-client-id>`, and the usage log records `User=app:<client-id>`. In Windows mode it's `DOMAIN\account` as usual. See [WEBJEAUSERNAME](entra.md#webjeausername).

## Troubleshooting

Rejected tokens get a 401 with `Invalid or expired token, or the calling app has no WebJEA app role.` The reason is in `webjea.log` as `Entra: bearer token rejected for <path>: <reason>` (Info):

| Log reason, or token endpoint error | Cause | Fix |
|---|---|---|
| `IDW10201: Neither scope nor roles claim was found in the bearer token` | The calling app holds no WebJEA app role, or admin consent wasn't granted | [Automation step 4](entra-appreg.md#optional-api-access-for-automation-app-only): add the application permission and grant admin consent, then get a new token |
| `IDX10214: Audience validation failed` | The token was requested for a different resource | Request `scope=api://<webjea-client-id>/.default`, using WebJEA's client ID, not the caller's |
| `Delegated (user) tokens aren't supported` | The token was issued to a user | Use the client credentials flow, as above |
| `IDX10223: Lifetime validation failed. The token is expired.` | The token is more than about an hour old | Request a new token |
| `IDX10511: Signature validation failed`, or an issuer validation error | The token is from another tenant or was altered | Request the token from WebJEA's tenant |
| Token endpoint: `AADSTS500011: The resource principal named api://... was not found` | WebJEA's Application ID URI isn't set, or `$webjeaId` is wrong | [Automation step 1](entra-appreg.md#optional-api-access-for-automation-app-only) |
| Token endpoint: `AADSTS7000215: Invalid client secret` / `AADSTS700027` (certificate not registered) | The calling app's credential is wrong or expired | Fix the **calling** app's credential under **Certificates & secrets** |
| HTTP 403; `webjea.log`: `API: User app:<id> denied cmdid <id>: not in any permitted group` | The app's role isn't in the command's `PermittedGroups` | Add the role value to `webjea.json` ([Automation step 5](entra-appreg.md#optional-api-access-for-automation-app-only)) |
| HTTP 415 | `Content-Type` isn't `application/json` | Pass `-ContentType 'application/json'` |

## Related

- [Entra App Registration: API access for automation](entra-appreg.md#optional-api-access-for-automation-app-only)
- [Entra ID mode](entra.md)
- [Windows mode](windows.md)
- [Usage](Usage.md)
