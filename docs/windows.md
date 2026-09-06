# Windows Authentication

Windows mode uses Kerberos or NTLM to authenticate users, with group membership resolved
from Active Directory. This is the default mode and matches the classic WebJEA deployment.

## Configuration

```json
{ "Authentication": { "Mode": "Windows" } }
```

## Windows service (Kestrel)

WebJEA installs as a self-hosted Windows service running on Kestrel — IIS is not used.
See [deployment-migration.md](deployment-migration.md#windows-deployps1-windows-service)
if you're upgrading an existing IIS install.

Negotiate authentication (Kerberos/NTLM) is automatically active when
`Authentication:Mode` is `Windows` — there is no IIS Windows Authentication feature to
enable. The service account must have **read** permissions on your AD to resolve groups
(via `GroupPrincipal` and `UserPrincipal` lookups). This is the same credential
requirement as the classic WebForms deployment.

### Kerberos requires an SPN

Kernel-mode HTTP.sys authentication (and the SPN handling that came with it) is gone
along with IIS. Without a **Service Principal Name** registered on the service account,
clients silently fall back to NTLM — the request still succeeds, so a missing SPN is
easy to miss unless you check for it explicitly. Register one `HTTP/<fqdn>` SPN per host
name WebJEA answers on (every `SiteFQDNs` entry):

```text
setspn -S HTTP/webjea.example.com DOMAIN\gmsaname$
```

`Deploy.ps1` verifies these SPNs (and attempts to register any that are missing) during
install, and stops the deploy if one can't be confirmed. Pass `-SkipSpnCheck` to
explicitly accept NTLM-only authentication instead.

### Confirming Kerberos vs. NTLM

- On a client, browse to the site, then run `klist`. A ticket for
  `HTTP/webjea.example.com` (the FQDN you browsed to) confirms Kerberos is in use; its
  absence means the client authenticated with NTLM instead.
- On the server, check the Security event log for event ID **4624** ("An account was
  successfully logged on") and inspect the **Authentication Package** field —
  `Kerberos` vs `NTLM`.

## Group and user resolution

Authorization via `PermittedGroups` in your `config.json` works exactly as before:

- Each entry is resolved to an AD **SID** using `GroupPrincipal` or `UserPrincipal`
  lookups. Entry formats:
  - `DOMAIN\GroupName` — domain group
  - `groupname@domain` — UPN style
  - `LocalGroup` — machine-local group
  - `.` prefix — machine-local (e.g., `.\Administrators`)
  - `*` — any authenticated user
- Matched against the SIDs in the user's token. Non-existent groups log a warning and
  deny access.

## WEBJEAUSERNAME

Scripts receive the username in familiar **`DOMAIN\user`** format (e.g.,
`CONTOSO\alice`). Audit logs use the same format.

## Related

- [Entra ID mode](entra.md) for cloud deployments
- [Development auto-login](dev.md) for local testing
