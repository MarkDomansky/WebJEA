# Development Auto-Login

For local development only, use `DevAutoLogin` mode to authenticate every request as a
configured test user without needing an identity provider or app registration. This is
inert outside the Development environment.

## Configuration

```json
{
  "WebJEA": { "DevAutoLogin": true },
  "DevUser": { 
    "Name": "DEV\\tester", 
    "Sids": [ "Domain Admins", "Finance" ] 
  }
}
```

Required (in the Development environment only):

- `WebJEA:DevAutoLogin: true` — enables the mode.
- `ASPNETCORE_ENVIRONMENT=Development` — the feature is inert in other environments.
- `DevUser:Name` — the username to assign to every request (e.g., `DEV\tester`).
- `DevUser:Sids` — a list of group/role names for authorization testing.

## How it works

Every HTTP request is authenticated as the configured `DevUser`, regardless of any actual
identity provider. The configured `Name` and `Sids` are used for authorization checks
against your `config.json` `PermittedGroups`.

## Authorization matching

In this mode, group names from `PermittedGroups` in your `config.json` are matched
**literally** against the entries in `DevUser:Sids`. For example:

```json
{
  "DevUser": { "Name": "DEV\\tester", "Sids": [ "Domain Admins", "Finance" ] }
}
```

```javascript
// config.json
{
  "PermittedGroups": [ "Domain Admins", "Finance", "HR" ]
}
```

The user matches `Domain Admins` and `Finance` (in `Sids`), so they have access to commands
authorized for those groups. `HR` is not in `Sids`, so access is denied for HR-only
commands.

The wildcard `*` in `PermittedGroups` grants access to any authenticated user and is
honored at the command level, just like production.

## Example launchSettings.json

A typical development setup in `Properties/launchSettings.json` (WebJEA always runs on
Kestrel, including under `dotnet run`/F5 in the IDE — there is no IIS Express profile):

```json
{
  "profiles": {
    "WebJEA": {
      "commandName": "Project",
      "launchBrowser": true,
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      },
      "applicationUrl": "https://localhost:55043;http://localhost:55044"
    }
  }
}
```

And `appsettings.Development.json`:

```json
{
  "WebJEA": { "DevAutoLogin": true },
  "DevUser": { 
    "Name": "DEV\\alice", 
    "Sids": [ "Domain Admins" ] 
  },
  "Authentication": { "Mode": "Windows" }
}
```

Now every request signs in as `DEV\alice` with `Domain Admins` membership, and you can
test authorization without deploying the Windows service or configuring Entra ID.

## Limitations

- **Development only.** The feature is completely inert (`ASPNETCORE_ENVIRONMENT` is not
  `Development`). It cannot be enabled in staging or production.
- **No actual authentication.** There is no credential validation — every request gets the
  same test identity.
- **Literal group matching.** Group names are matched exactly; no AD or Entra lookup
  occurs.

## Related

- [Windows mode](windows.md) for production deployments
- [Entra ID mode](entra.md) for cloud deployments
