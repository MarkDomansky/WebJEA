# Advanced Entra Options

These options extend [Entra ID mode](entra.md) beyond the recommended setup of group object IDs, UPNs, and `*`. They need extra configuration in Entra, and several need an **Entra ID P1 or P2** license. Complete the [app registration](entra-appreg.md) first.

| Option | `PermittedGroups` entry | License | Main caveat |
|---|---|---|---|
| [App roles](#app-roles) | Role value, e.g. `WebJEA.HR` | Free for users; **P1/P2** for groups | Group assignment honors direct members only |
| [Groups assigned to the application](#groups-assigned-to-the-application) | Group object ID | **P1/P2** | Every permitted group must be assigned; direct members only |
| [Require assignment](#require-assignment) | (any) | Free for users; **P1/P2** for groups | Blocks sign-in for anyone not assigned |

## Nested groups and app assignment

Several options here work by **assigning** users or groups to the WebJEA enterprise application. Entra applies two rules to every group assignment:

- **Assigning groups requires Entra ID P1 or P2.** Assigning individual users works on the free tier, but you then maintain every user by hand.
- **Only direct members count.** A group assigned to the app grants nothing to members of groups nested inside it, and nothing warns you. Assign the nested groups too, or flatten the membership, for example with a dynamic group.

This differs from the `groups` claim used by the [recommended options](entra.md#recommended-entries), which **does** include nested groups.

### Assign users and groups

Assignments live on the **enterprise application** (the service principal), not the app registration:

1. Go to **Entra ID** → **Enterprise applications** → your WebJEA app → **Users and groups** → **+ Add user/group**.
2. Pick the users or groups.
3. Pick the role. If the app defines no app roles, the role is **Default Access**.
4. Click **Assign**. Repeat for each user/group and role combination. A user may hold several roles.

You need **Cloud Application Administrator** or **Application Administrator**. Users can't self-assign. Changes take effect at the user's next sign-in.

## App roles

App roles are WebJEA-specific roles defined on the app registration, such as `WebJEA.Admin` or `WebJEA.HR`. You assign groups (or users) to them on the enterprise app, and the role **values** arrive in the token's `roles` claim. `webjea.json` stays readable with no GUIDs, and role claims never hit the 200-group limit.

- **Assigning groups to roles** is the intended use: membership follows groups you already manage, and one assignment covers the whole group. It **requires P1/P2**, and only **direct** members of an assigned group receive the role.
- **Assigning individual users** works on the free tier, but each user is assigned by hand, and the list doesn't follow any group membership. It suits small audiences.

### Create the roles

1. In your **app registration**, go to **App roles** → **+ Create app role**.
2. Fill in:
   - **Display name**: e.g., "WebJEA HR" (what admins see when assigning).
   - **Allowed member types**: **Users/Groups**.
   - **Value**: e.g., `WebJEA.HR`. This exact string goes in `PermittedGroups`. No spaces allowed.
   - **Description**: e.g., "Can run the HR scripts".
   - **Do you want to enable this app role?**: checked.
3. Click **Apply**.
4. Repeat for each role. A useful pattern is one role per command group (e.g. `WebJEA.Admin`, `WebJEA.HR`, `WebJEA.Finance`), mirroring your per-command `PermittedGroups`.

Then [assign groups or users](#assign-users-and-groups) to each role.

### Set the app role prefix

Start every role **Value** with the same prefix, such as `WebJEA.`, and set it in `appsettings.Production.json`:

```json
{ "Authentication": { "Entra": { "AppRolePrefix": "WebJEA." } } }
```

With the prefix set, WebJEA knows those entries are app roles and skips the Graph display-name lookup it would otherwise make for each one on every request ([Group display names](entra.md#group-display-names)). Matching is case-insensitive.

> [!IMPORTANT]
> Groups can't use the prefix. A group whose display name starts with `WebJEA.` is treated as an app role and never resolved by name, so reference it by object ID instead. Pick a prefix no group display name in your tenant starts with.

### Use the role values

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

Users holding `WebJEA.Admin` see everything; users holding only `WebJEA.HR` see just the New User command.

Trade-offs versus groups:

- **Pro**: readable config, per-app scoping, no group limit, and all assignments visible in one place (the enterprise app's **Users and groups**).
- **Con**: role values are free text. A typo in `webjea.json` or in the role's **Value** silently denies access. There is no "unknown role" warning, because WebJEA can't tell a role value from a UPN.

## Groups assigned to the application

**Requires P1/P2.** Instead of sending every group the user is in, Entra sends only the groups that are **assigned to the WebJEA enterprise app** and that the user is a **direct** member of. The token stays small, so the 200-group limit effectively disappears.

1. [Assign](#assign-users-and-groups) each group your `webjea.json` references to the enterprise app.
2. In the app registration, go to **Token configuration**, click the **⋯** next to the `groups` claim ([Step 3](entra-appreg.md#step-3-add-the-groups-claim)) → **Edit**, select **Groups assigned to the application** instead of **Security groups**, and click **Save**.

`PermittedGroups` entries are then group object IDs, as with the recommended setup.

Limitations:

- **Every group in `PermittedGroups` must be assigned to the app**, including per-command entries. A forgotten group isn't in the token, so its users are silently denied.
- **Direct members only.** Nested groups aren't included.

## Require assignment

Makes Entra reject sign-ins from anyone not [assigned](#assign-users-and-groups) to WebJEA. This is defense in depth on top of `PermittedGroups`. It also narrows `*` from "anyone in the tenant" to "anyone assigned to WebJEA".

1. Go to **Entra ID** → **Enterprise applications** → your WebJEA app → **Properties**.
2. Set **Assignment required?** to **Yes** and save.
3. Make sure every permitted user or group is assigned under **Users and groups**. Group assignment requires P1/P2 and honors direct members only.

## Related

- [Entra ID mode](entra.md)
- [Entra App Registration](entra-appreg.md)
