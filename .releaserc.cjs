/**
 * semantic-release configuration for WebJEA.
 *
 * Release lines (branch -> version it cuts):
 *
 *   alpha      -> 2100.0.0-alpha.N  the .NET rewrite; every squash-merged PR lands here
 *   beta       -> 2100.0.0-beta.N   staging for the rewrite; fed by merging alpha
 *   master     -> GA                2026.9.x (VB.NET app) today; 2100.0.0 once beta is merged
 *   beta-2026  -> 2026.9.x-rc.N     staging for hotfixes to the shipping VB.NET app
 *   2026.x     -> 2026.9.x GA       maintenance line, created only when master becomes 2100
 *
 * Every prerelease branch needs a UNIQUE identifier, which is why the legacy
 * staging line uses `rc` instead of a second `beta`. The major number already
 * says which product a tag belongs to (2026 = VB.NET app, 2100 = rewrite).
 *
 * Seeds - two hand-made tags that must never be deleted:
 *
 *   v2026.9.0  on master's last date-versioned commit. semantic-release ignores
 *              the old vYYYY.M.D.HHmm tags (four components are not semver) and
 *              would otherwise start over at 1.0.0.
 *   v2099.0.0  on an empty commit reachable from alpha but NOT from master. It has
 *              no GitHub release. The import commit after it carries a BREAKING
 *              CHANGE footer, so the major bump lands on 2100.0.0 - first as
 *              -alpha.N, then -beta.N when alpha is merged into beta, then GA when
 *              beta is merged into master. Until that merge master keeps counting
 *              2026.9.x because the seed is not in its history.
 *
 * Because the seed relies on a major bump, `breaking` stays mapped to major.
 * Do not copy the breaking->minor remap used by 0.x projects.
 *
 * Promotions (alpha -> beta, beta -> master, beta-2026 -> master or 2026.x) must
 * be REAL merge commits, never squashes: the analyzer needs the individual
 * conventional commits (and the import's BREAKING CHANGE footer) to pick the
 * bump. After every GA, merge the GA branch back into the line that feeds it
 * with `[skip ci]` in the message so the new tag becomes reachable there.
 *
 * No plugin below writes back to the repo (no @semantic-release/git, no
 * changelog file), so releasing never moves a branch. Branches listed here that
 * do not exist on the remote are skipped silently (2026.x until it is created).
 *
 * This must stay a .cjs file, not YAML: cosmiconfig searches .releaserc.yaml/.yml
 * BEFORE .releaserc.js/.cjs, so a leftover .releaserc.yml would silently win.
 */

module.exports = {
  branches: [
    'master',
    '2026.x',
    { name: 'beta', prerelease: 'beta' },
    { name: 'beta-2026', prerelease: 'rc' },
    { name: 'alpha', prerelease: 'alpha' },
  ],

  tagFormat: 'v${version}',

  plugins: [
    // breaking -> major, `feat` -> minor, everything else -> patch. The `**`
    // catch-all makes any other commit a patch, so free-form commits still cut
    // a release instead of silently releasing nothing. To land a commit without
    // releasing, put [skip ci] in the message.
    //
    // The catch-all matches `header` (the first line), not `message`: the rule
    // is a micromatch glob and `**` does not match across newlines, so a
    // `message: '**'` rule silently ignores every commit that has a body -
    // which is every squash merge whose PR body was kept.
    //
    // The breaking rule is spelled out because commit-analyzer stops consulting
    // its default rules as soon as any custom rule matches - and with the
    // catch-all present, every commit matches one.
    //
    // preset: the default (angular) preset does NOT understand the `feat!:`
    // shorthand - such a commit fails to parse and drops to the catch-all as a
    // patch. The conventionalcommits preset handles it. It is not bundled with
    // semantic-release, so both workflow steps install it via extra_plugins.
    [
      '@semantic-release/commit-analyzer',
      {
        preset: 'conventionalcommits',
        releaseRules: [
          { breaking: true, release: 'major' },
          { type: 'feat', release: 'minor' },
          { revert: true, release: 'patch' },
          { header: '**', release: 'patch' },
        ],
      },
    ],

    ['@semantic-release/release-notes-generator', { preset: 'conventionalcommits' }],

    // Creates the tag and the GitHub release and attaches the zip the build job
    // already produced for this exact version. Prerelease flag comes from the
    // branch config above.
    [
      '@semantic-release/github',
      {
        assets: [{ path: 'dist/webjea-*.zip' }],
        releaseNameTemplate: 'WebJEA <%= nextRelease.version %>',
        releaseBodyTemplate:
          '<%= nextRelease.notes %>\n\n[Installation Guide](https://github.com/markdomansky/WebJEA/blob/master/docs/Installation.md)',
        successComment: false,
        failComment: false,
        labels: false,
        releasedLabels: false,
      },
    ],
  ],
};
