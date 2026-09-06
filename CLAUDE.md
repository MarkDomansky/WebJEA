# WebJEA - notes for Claude

## Commit messages decide the released version

Versions are **not** set by hand. `semantic-release` reads the commit messages
on the pushed branch and computes the next version
(`.github/workflows/release.yml`, configured by `.releaserc.cjs`). Never edit a
version string by hand and never create a `v*` tag manually - the release
workflow creates the tag itself, only after the build succeeds.

- Release lines are `alpha` (rewrite, `2100.0.0-alpha.N`), `beta` (rewrite
  staging), `master` (GA), `beta-2026` (VB.NET hotfix staging, `-rc.N`) and,
  later, `2026.x` (VB.NET maintenance). Never commit to any of them directly:
  branch `type/short-description`, open a PR, squash-merge. The PR title is
  the squash subject and therefore the version knob.
- Promotions between lines are PRs merged with `gh pr merge --merge`, never a
  squash. After a GA, back-merge the GA branch into the line that feeds it with
  `[skip ci]`.
- The tags `v2026.9.0` and `v2099.0.0` are deliberate seeds (see
  CONTRIBUTING.md). Do not delete, move or "fix" them, and do not remap
  `breaking` away from `major` in `.releaserc.cjs`: the `2100.0.0` line depends
  on that major bump.
- `.releaserc.cjs` must stay a `.cjs` file; a `.releaserc.yml` would shadow it.
