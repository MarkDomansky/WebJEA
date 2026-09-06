# Releasing WebJEA

WebJEA is released by [semantic-release](https://semantic-release.gitbook.io/)
from `.github/workflows/release.yml`, configured in `.releaserc.cjs`. Versions
are **never** set by hand: the commit messages on the branch that was pushed
decide the next version, the workflow builds and tests that exact version, and
only then creates the tag and the GitHub release with `webjea-<version>.zip`
attached. `CONTRIBUTING.md` at the repo root is the short form of this page.

## Release lines

| Branch      | Cuts                | Purpose                                                      |
| ----------- | ------------------- | ------------------------------------------------------------ |
| `alpha`     | `2100.0.0-alpha.N`  | The .NET rewrite. Day-to-day work for the new app lands here. |
| `beta`      | `2100.0.0-beta.N`   | Staging for the rewrite. Fed only by merging `alpha`.         |
| `master`    | GA                  | What users download. `2026.9.x` (VB.NET app) until `beta` is merged, `2100.x` afterwards. |
| `beta-2026` | `2026.9.x-rc.N`     | Staging for hotfixes to the shipping VB.NET app.              |
| `2026.x`    | `2026.9.x` GA       | Maintenance line for the VB.NET app, created only when `master` becomes `2100`. |

```text
                 topic branch  type/short-description
                       |  squash-merge PR; PR title = Conventional Commit = version knob
        +--------------+--------------+
        v                             v
   alpha  -> v2100.0.0-alpha.N     beta-2026 -> v2026.9.x-rc.N      (VB.NET hotfix staging)
        | PR alpha -> beta                 | PR beta-2026 -> master  (gh pr merge --merge)
        v (gh pr merge --merge)            v
   beta   -> v2100.0.0-beta.N         master -> v2026.9.x GA
        | PR beta -> master                | back-merge master -> beta-2026 [skip ci]
        v (gh pr merge --merge)            |
   master -> v2100.0.0 GA  <---------------+  at this promotion: create 2026.x from the last
        |                                     2026 GA commit; beta-2026 PRs then target 2026.x
        v back-merge master -> beta -> alpha [skip ci] so the GA tag is reachable
```

## How the version is computed

- On a **release** branch (`master`, `2026.x`) the last release is the highest
  non-prerelease tag reachable from the branch. The next version is that plus
  the bump the commits since it call for.
- On a **prerelease** branch the last release is the highest tag that is either
  a prerelease of the same channel or any non-prerelease. The first release on
  a channel is `<bumped version>-<id>.1`; later ones increment the counter.
- Bumps: `feat!:` / `BREAKING CHANGE:` footer -> major, `feat:` -> minor,
  anything else -> patch. A commit with `[skip ci]` in the message is not
  released at all. Pushes that only touch docs, Markdown, `.vscode`,
  `Test/Integration`, `Test/Dev` or `Test/Scripts` cut no release either.

| Event                                              | Result                     |
| -------------------------------------------------- | -------------------------- |
| First `master` release after the `v2026.9.0` seed  | `2026.9.1`                 |
| `fix:` squash-merged into `beta-2026`              | `2026.9.2-rc.1`, `-rc.2` … |
| `beta-2026` merged into `master`                   | `2026.9.2`                 |
| Rewrite import (`feat!:`) pushed on `alpha`        | `2100.0.0-alpha.1`         |
| Any later PR into `alpha`                          | `2100.0.0-alpha.2` …       |
| `beta` created from `alpha`                        | `2100.0.0-beta.1`          |
| `beta` merged into `master`                        | `2100.0.0`                 |
| Hotfix on `2026.x` after `master` is `2100`        | `2026.9.3`                 |
| `fix:` into `beta` after the `2100.0.0` back-merge | `2100.0.1-beta.1`          |

## Landing a change

1. Start on the line the change belongs to (`alpha` for the rewrite,
   `beta-2026` for a VB.NET hotfix). Never commit to a release line directly.
2. Create a topic branch named `type/short-description`
   (`feat/entra-app-roles`, `fix/https-redirect-loop`).
3. Open a pull request back into that line and **squash-merge** it.
4. **The PR title is the version knob.** The squash commit subject is the PR
   title, and that subject is the only message semantic-release analyzes.
   Write it as a [Conventional Commit](https://www.conventionalcommits.org/).
   Put a `BREAKING CHANGE:` footer in the squash body when the title carries `!`.

## Promoting between lines

Promotions are pull requests merged with a **real merge commit**
(`gh pr merge --merge`), never a squash. A squash would collapse every released
commit into one message; the analyzer would pick one bump from it - usually the
wrong one - and the release notes would lose the individual changes.

- `alpha` -> `beta`: the first time, `beta` is created from `alpha`
  (`git push origin alpha:beta`); afterwards open a PR `alpha` -> `beta`.
- `beta` -> `master`: cuts the `2100.0.0` GA. `master` still contains the
  VB.NET app that `beta` deleted, so expect *modify/delete* conflicts on any
  VB.NET file that was hot-fixed on `master`. Resolve locally, keeping the
  deletions:

  ```powershell
  git switch -c promote/2100 master
  git merge beta
  git status --porcelain | Select-String '^(DU|UD)' | ForEach-Object { git rm -q ($_ -replace '^.. ', '') }
  git commit
  git push -u origin promote/2100
  gh pr create --base master --title 'Promote 2100.0.0 to master' --body '...'
  gh pr merge --merge
  ```

  Before merging, note the current `master` tip: that commit is the last 2026
  GA. Create the maintenance line from it (`git push origin <sha>:2026.x`) and
  point `beta-2026` PRs at `2026.x` from then on. Never merge `2026.x` or
  `beta-2026` into `master` again.
- `beta-2026` -> `master` (until `master` is `2100`), then `beta-2026` -> `2026.x`.

**After every GA, merge the GA branch back** into the line(s) that feed it -
`master` -> `beta` -> `alpha`, or `2026.x` -> `beta-2026` - with `[skip ci]` in
the merge message:

```powershell
git switch beta
git merge master -m "chore: sync master after v2100.0.0 [skip ci]"
git push
```

Without the back-merge the new GA tag is not reachable from the prerelease
line, and it keeps numbering against the previous release.

## Version seeds

Two hand-made tags exist and must never be deleted or recreated:

- `v2026.9.0` marks the last date-versioned build of the VB.NET app on
  `master`. The older `vYYYY.M.D.HHmm` tags are not valid semver and are ignored.
- `v2099.0.0` sits on an empty commit that is reachable from `alpha` but not
  from `master`, and has no GitHub release. The breaking import commit after it
  is what makes the rewrite `2100.0.0`. Until `beta` is merged into `master`,
  `master` cannot see that tag and keeps counting `2026.9.x`.

Never create any other `v*` tag by hand and never edit a version string in the
source: the workflow stamps the version into the build with `build.ps1 -Version`,
and the app reads it back from the assembly's informational version
(`/api/appinfo` and the page footer show e.g. `v2100.0.0-alpha.1`).

## Checking a release

```powershell
gh run list --workflow=release.yml --branch alpha --limit 1
gh release view v2100.0.0-alpha.1 --json name,isPrerelease,assets
```

The release must carry `webjea-<version>.zip`; the publish job fails if the
version semantic-release published differs from the one the build stamped.
