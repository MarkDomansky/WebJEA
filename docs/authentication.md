# WebJEA Authentication

WebJEA supports two authentication modes, selected by `Authentication:Mode` in
`appsettings.json` (or the `Authentication__Mode` environment variable):

| Mode | Hosts | Identity source | Authorization |
|---|---|---|---|
| `Windows` | Windows only | Kerberos/NTLM | Active Directory groups/users |
| `Entra` | Windows or Linux | Entra ID (Azure AD) OIDC | Entra groups, users, or app roles |

## Mode selection

- **Windows** is the default and recommended for domain-joined Windows Server
  deployments (self-hosted as a Windows service on Kestrel) with on-premises AD.
  Requires a Windows host.
- **Entra** is required for Linux deployments and is recommended for cloud-hosted
  scenarios. Works on Windows or Linux.
- Development: use **DevAutoLogin** mode for local testing without an identity provider
  (Development environment only).

## Detailed guides

- [Windows mode](windows.md) — Kerberos/NTLM via Negotiate on Kestrel, SPN
  requirements, AD group resolution
- [Entra ID mode](entra.md) — OIDC sign-in, group object IDs, app roles, Microsoft Graph
  integration
- [Development auto-login](dev.md) — Local development without an identity provider

## Linux hosting

`Authentication:Mode` must be `Entra` on Linux — Windows/AD mode requires a Windows
host and refuses to start elsewhere. Everything else (PowerShell 7 hosting, the API,
the front end) is cross-platform. Note that scripts run under `pwsh` on the host OS, so
Windows-specific modules (ActiveDirectory, IIS, etc.) are unavailable on Linux workers.
