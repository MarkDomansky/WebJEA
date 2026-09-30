# WebJEA Authentication

WebJEA supports two authentication modes, selected by `Authentication:Mode` in `appsettings.json` (or the `Authentication__Mode` environment variable):

| Mode | Hosts | Identity source | Authorization |
|---|---|---|---|
| `Windows` | Windows only | Kerberos/NTLM | Active Directory groups/users |
| `Entra` | Windows, or Linux containers | Entra ID (Azure AD) OIDC | Entra groups, users, or app roles |

## Mode selection

- **Windows** is the default and recommended for domain-joined Windows Server deployments (self-hosted as a Windows service on Kestrel) with on-premises AD. Requires a Windows host.
- **Entra** is required for Linux containers and is recommended for cloud-hosted scenarios. Works on Windows or in Linux containers.
- Development: use **DevAutoLogin** mode for local testing without an identity provider (Development environment only).

## Detailed guides

- [Windows mode](windows.md) — Kerberos/NTLM via Negotiate on Kestrel, SPN requirements, AD group resolution
- [Entra ID mode](entra.md) — OIDC sign-in, group object IDs, app roles, Microsoft Graph integration
- [Development auto-login](dev.md) — Local development without an identity provider

## Linux containers

`Authentication:Mode` must be `Entra` in a Linux container — Windows/AD mode requires a Windows host and refuses to start elsewhere. Scripts run under `pwsh` on Linux, so Windows-specific modules (ActiveDirectory, IIS, etc.) are unavailable; see [docker.md](docker.md) and [powershell7.md](powershell7.md).
