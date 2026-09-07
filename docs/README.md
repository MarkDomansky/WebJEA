# WebJEA Documentation

WebJEA dynamically builds web forms for any PowerShell script — parsing the script for
description, parameters, and validation — and runs it under a controlled service
identity for users you authorize.

## Getting started

* [System Requirements](System-Requirements.md)
* [Installation](Installation.md)
* [Usage](Usage.md)
* [Tips](Tips.md)

## Deployment and hosting

* [Deployment guide](deployment-migration.md) — the Windows service (`Deploy.ps1`),
  Linux hosting, and migrating from the classic IIS/WebForms releases
* [Docker](docker.md) — Linux and Windows container images

## Authentication

* [Overview](authentication.md) — choosing between Windows and Entra ID modes
* [Windows mode](windows.md) — Kerberos/NTLM, SPNs, AD group resolution
* [Entra ID mode](entra.md) — OIDC sign-in, groups, app roles, Microsoft Graph
* [Development auto-login](dev.md) — local testing without an identity provider

## Scripts

* [PowerShell 7 compatibility](powershell7.md) — differences from Windows
  PowerShell 5.1 for scripts you expose through WebJEA

## Contributing

* [Releasing](Releasing.md) — release lines (`alpha`, `beta`, `master`,
  `beta-2026`), how PR titles decide versions, promotions and back-merges

## About these documents

This folder is the source of truth for WebJEA's documentation. The
[project wiki](https://github.com/markdomansky/WebJEA/wiki) is a generated,
read-only mirror of this folder as it stands on `master`, republished
automatically by `.github/workflows/wiki-sync.yml` — so it shows the docs for
the release you can actually download. **Edit the files here, not the wiki**;
wiki edits are overwritten by the next sync. See
[Documentation](../CONTRIBUTING.md#documentation) in CONTRIBUTING.md.
