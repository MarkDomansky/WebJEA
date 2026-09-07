# Contributing and releasing

WebJEA is released by [semantic-release](https://semantic-release.gitbook.io/)
from `.github/workflows/release.yml`, configured in `.releaserc.cjs`. Versions
are **never** set by hand: the commit messages on the branch that was pushed
decide the next version, the workflow builds and tests that exact version, and
only then creates the tag and the GitHub release with `webjea-<version>.zip`
attached.

## Release lines

| Branch      | Cuts                | Purpose                                                      |
| ----------- | ------------------- | ------------------------------------------------------------ |
| `alpha`     | `2100.0.0-alpha.N`  | The .NET rewrite. Day-to-day work for the new app lands here. |
| `beta`      | `2100.0.0-beta.N`   | Staging for the rewrite. Fed only by merging `alpha`.         |
| `master`    | GA                  | What users download. `2026.9.x` (VB.NET app) until `beta` is merged, `2100.x` afterwards. |
| `beta-2026` | `2026.9.x-rc.N`     | Staging for hotfixes to the shipping VB.NET app.              |
| `2026.x`    | `2026.9.x` GA       | Maintenance line for the VB.NET app, created only when `master` becomes `2100`. |

`alpha`, `beta` and `beta-2026` are prerelease channels; their releases are
marked *Pre-release* on GitHub. `master` (and later `2026.x`) publish full
releases.

## Landing a change

1. Start on the line the change belongs to (`alpha` for the rewrite,
   `beta-2026` for a VB.NET hotfix). Never commit to a release line directly.
2. Create a topic branch named `type/short-description`
   (`feat/entra-app-roles`, `fix/https-redirect-loop`).
3. Open a pull request back into that line and **squash-merge** it.
4. **The PR title is the version knob.** The squash commit subject is the PR
   title, and that subject is the only message semantic-release analyzes.
   Write it as a [Conventional Commit](https://www.conventionalcommits.org/):

   | Title                             | Bump  |
   | --------------------------------- | ----- |
   | `fix: ...`, `perf: ...`, anything else | patch |
   | `feat: ...`                       | minor |
   | `feat!: ...` or a `BREAKING CHANGE:` footer in the squash body | major |

   Add `[skip ci]` to the title to land a change without cutting a release
   (docs-only and test-harness-only changes already skip via `paths-ignore`).

## Promoting between lines

Promotions are pull requests merged with a **real merge commit**
(`gh pr merge --merge`), never a squash. A squash would collapse every released
commit into one message; the analyzer would pick one bump from it - usually the
wrong one - and the release notes would lose the individual changes.

- `alpha` → `beta`: the first time, `beta` is created from `alpha`
  (`git push origin alpha:beta`); afterwards open a PR `alpha` → `beta`.
- `beta` → `master`: cuts the `2100.0.0` GA. Because `master` deleted nothing
  while `beta` deleted the VB.NET app, expect *modify/delete* conflicts on any
  VB.NET file that was hot-fixed on `master`; resolve locally by keeping the
  deletion, push the merge on a `promote/...` branch, and merge that PR with
  `--merge`.
- `beta-2026` → `master` (until master is `2100`), then `beta-2026` → `2026.x`.

**After every GA, merge the GA branch back** into the line(s) that feed it -
`master` → `beta` → `alpha`, or `2026.x` → `beta-2026` - with `[skip ci]` in the
merge message:

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
source: the workflow stamps the version into the build with `build.ps1 -Version`.

## Documentation

User documentation lives in [`docs/`](docs/README.md) and is edited there, the
same way as code: topic branch, pull request, squash-merge. Docs-only changes
cut no release - `release.yml` and `test.yml` both ignore `docs/**` and `**/*.md`.

The [wiki](https://github.com/markdomansky/WebJEA/wiki) is **generated**, not
authored. On every push to `master` that touches `docs/`,
`.github/workflows/wiki-sync.yml` runs `.github/workflows/sync-wiki.ps1`, which
renders `docs/` into the wiki repository and force-replaces its contents. Any
page edited in the wiki UI is silently overwritten on the next sync, and a page
the docs no longer produce is deleted. Because the trigger is `master` only, the
wiki always describes the release users can download - docs written on `alpha`
appear when that line is promoted.

What the renderer does to each file (details in the script's comment header):

- `docs/README.md` becomes `Home.md`; other file names become wiki page titles,
  with readable names for the lowercase ones supplied by `$PageNameOverrides` in
  the script. **Adding a docs file with a lowercase or unclear name means adding
  an entry there**, otherwise it publishes as e.g. "Powershell7".
- Links between docs lose their `.md` (`[x](Usage.md)` → `[x](Usage)`), anchors
  preserved. A `*.md` link that resolves to no docs file **fails the workflow** -
  that is the guard against typos and stale links.
- Links reaching outside `docs/` (`../readme.md`, `../docker/examples/…`) become
  absolute `github.com` URLs on `master`, since the wiki cannot see the code.
- Every page gets a "this page is generated" banner, plus a `_Sidebar.md` built
  from the headings and links in `docs/README.md` and a `_Footer.md`.

Preview the output before pushing - this renders to a temp folder and changes
nothing:

```powershell
.\.github\workflows\sync-wiki.ps1 -WikiPath . -TestOnly
```

Use the workflow's **Run workflow** button to repair the wiki after someone
edits it by hand, or after changing the renderer. The workflow pushes with
`secrets.WIKI_TOKEN` when that secret exists (a PAT with `repo`, or
fine-grained Contents: write) and falls back to `GITHUB_TOKEN`.
