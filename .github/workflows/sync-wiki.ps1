<#
.SYNOPSIS
    Renders docs\ into a GitHub wiki working tree.

.DESCRIPTION
    docs\ in this repo is the single source of truth for user documentation.
    The wiki at https://github.com/markdomansky/WebJEA/wiki is a generated
    mirror - nobody edits it directly. This script converts docs\ into the
    layout a wiki clone expects and is invoked by wiki-sync.yml on every push
    to master that touches docs\.

    Conversions applied:

    1. File name -> wiki page name. Wiki page titles come from the file name
       with hyphens shown as spaces, so docs\README.md becomes Home.md and the
       lowercase file names get readable titles via $PageNameOverrides.
    2. Intra-docs links. [x](Usage.md) and [x](docker.md#configuration) become
       [x](Usage) and [x](Docker#configuration) - wiki links carry no .md.
       An unresolvable *.md link is a hard error, which is how a typo or a
       deleted page gets caught before it reaches the wiki.
    3. Links out of docs\ (../readme.md, ../docker/examples/...) become
       absolute github.com/<repo>/blob|tree/<ref>/... URLs, since the wiki is
       a separate repository with no view of the code.
    4. A "generated - edit docs\ instead" banner is injected into every page,
       plus a _Sidebar.md built from docs\README.md and a _Footer.md.
    5. Non-markdown files under docs\ (images and the like) are copied through
       with their relative paths intact.

    Anything left in the wiki tree that this run did not produce is deleted, so
    the wiki is a true mirror rather than an accumulation.

.PARAMETER DocsPath
    Path to the docs folder to render. Default: docs\ beside this repo root.

.PARAMETER WikiPath
    Path to a checkout of the <repo>.wiki repository. Its tracked content is
    replaced by the rendered output.

.PARAMETER Repo
    owner/name used to build absolute links back into the code repository.
    Default: markdomansky/WebJEA.

.PARAMETER Ref
    Branch or tag the absolute links point at. Default: master.

.PARAMETER TestOnly
    Render into a temporary folder and report what would change, without
    touching WikiPath.

.EXAMPLE
    .\sync-wiki.ps1 -WikiPath ..\WebJEA.wiki -TestOnly

.EXAMPLE
    .\sync-wiki.ps1 -WikiPath $env:RUNNER_TEMP\wiki

.OUTPUTS
    Hashtable:
    - Pages:   rendered page file names
    - Assets:  copied non-markdown files
    - Removed: files deleted from the wiki tree
    - WikiPath: where the output was written
#>
[CmdletBinding()]
param(
    [Parameter()]
    [string]$DocsPath = (Join-Path $PSScriptRoot '..\..\docs'),

    [Parameter(Mandatory)]
    [string]$WikiPath,

    [Parameter()]
    [string]$Repo = 'markdomansky/WebJEA',

    [Parameter()]
    [string]$Ref = 'master',

    [Parameter()]
    [switch]$TestOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Wiki page name for a docs file base name. Anything not listed is title-cased
# per hyphen-separated word, which already reads correctly for the Title-Case
# file names (System-Requirements -> "System Requirements"). Add an entry here
# when a new docs file needs a title the file name cannot express.
$PageNameOverrides = @{
    'README'               = 'Home'
    'deployment-migration' = 'Deployment-and-Migration'
    'dev'                  = 'Development-Auto-Login'
    'entra'                = 'Entra-ID'
    'powershell7'          = 'PowerShell-7'
    'windows'              = 'Windows-Authentication'
}

function Get-PageName {
    param([Parameter(Mandatory)][string]$BaseName)

    if ($PageNameOverrides.ContainsKey($BaseName)) {
        return $PageNameOverrides[$BaseName]
    }
    # Title-case each word; leave the hyphens, GitHub renders them as spaces.
    return (($BaseName -split '-' | ForEach-Object {
        if ($_.Length -gt 0) { $_.Substring(0, 1).ToUpperInvariant() + $_.Substring(1) } else { $_ }
    }) -join '-')
}

$docsRoot = (Resolve-Path $DocsPath).Path
$repoUrl = "https://github.com/$Repo"

$markdown = Get-ChildItem -Path $docsRoot -Filter '*.md' -File | Sort-Object Name
if (-not $markdown) { throw "No markdown files found under '$docsRoot'." }

# Source file name (lowercased) -> wiki page name, for link rewriting.
$pageMap = @{}
foreach ($file in $markdown) {
    $pageMap[$file.Name.ToLowerInvariant()] = Get-PageName -BaseName $file.BaseName
}

function Convert-Links {
    param(
        [Parameter(Mandatory)][AllowEmptyString()][string]$Text,
        [Parameter(Mandatory)][string]$SourceName
    )

    $broken = [System.Collections.Generic.List[string]]::new()

    $result = [regex]::Replace($Text, '(?<=\]\()([^)\s]+)(?=\))', {
        param($m)
        $target = $m.Groups[1].Value

        # Absolute URLs, mailto: and same-page anchors need no change - wiki
        # anchors are generated from headings exactly as they are on github.com.
        if ($target -match '^([a-z][a-z0-9+.-]*:|#|//)') { return $target }

        # Anything reaching outside docs\ lives in the code repo, not the wiki.
        if ($target.StartsWith('../')) {
            $path = $target.Substring(3)
            $kind = if ($path.EndsWith('/')) { 'tree' } else { 'blob' }
            return "$repoUrl/$kind/$Ref/$($path.TrimEnd('/'))"
        }

        $file, $anchor = $target -split '#', 2
        if ($file -notmatch '\.md$') { return $target }

        $page = $pageMap[$file.ToLowerInvariant()]
        if (-not $page) {
            $broken.Add("$SourceName -> $target")
            return $target
        }
        if ($anchor) { return "$page#$anchor" }
        return $page
    })

    return [pscustomobject]@{ Text = $result; Broken = $broken }
}

function Add-Banner {
    param(
        [Parameter(Mandatory)][string]$Text,
        [Parameter(Mandatory)][string]$SourceName
    )

    $banner = "> **This page is generated.** It mirrors " +
        "[``docs/$SourceName``]($repoUrl/blob/$Ref/docs/$SourceName) in the " +
        "[WebJEA repository]($repoUrl). Edits made here are overwritten by the " +
        "next sync - please open a pull request against ``docs/`` instead."

    $lines = $Text -split "`r?`n"
    # Keep the H1 first so the page still reads correctly, banner right below it.
    if ($lines.Count -gt 0 -and $lines[0] -match '^#\s') {
        return (@($lines[0], '', $banner) + $lines[1..($lines.Count - 1)]) -join "`n"
    }
    return (@($banner, '') + $lines) -join "`n"
}

function New-Sidebar {
    param([Parameter(Mandatory)][string]$ReadmeText)

    # docs\README.md is already a complete, grouped index of every page, so the
    # sidebar is that index with the prose descriptions trimmed off.
    $out = [System.Collections.Generic.List[string]]::new()
    $out.Add("### [WebJEA]($($pageMap['readme.md']))")
    $out.Add('')

    # A heading is only emitted once a bullet turns up under it, so prose-only
    # sections of README (such as "About these documents") leave no empty group.
    $pending = $null
    foreach ($line in ($ReadmeText -split "`r?`n")) {
        if ($line -match '^##\s+(.+?)\s*$') {
            $pending = $Matches[1]
            continue
        }
        # Only the bullet's own line carries the link; wrapped description lines
        # are indented and get skipped, which is what we want in a sidebar.
        if ($line -match '^\*\s+(\[[^]]+\]\([^)]+\))') {
            if ($pending) {
                if ($out[$out.Count - 1] -ne '') { $out.Add('') }
                $out.Add("**$pending**")
                $pending = $null
            }
            $out.Add("* $($Matches[1])")
        }
    }

    return ($out -join "`n").Trim() + "`n"
}

$stage = Join-Path ([System.IO.Path]::GetTempPath()) ("webjea-wiki-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage -Force | Out-Null

try {
    $broken = [System.Collections.Generic.List[string]]::new()
    $pages = [System.Collections.Generic.List[string]]::new()
    $readmeRendered = $null

    foreach ($file in $markdown) {
        $page = $pageMap[$file.Name.ToLowerInvariant()]
        $raw = Get-Content -LiteralPath $file.FullName -Raw

        $converted = Convert-Links -Text $raw -SourceName $file.Name
        $broken.AddRange($converted.Broken)

        if ($file.Name -ieq 'README.md') { $readmeRendered = $converted.Text }

        $body = (Add-Banner -Text $converted.Text -SourceName $file.Name).TrimEnd() + "`n"
        Set-Content -LiteralPath (Join-Path $stage "$page.md") -Value $body -NoNewline -Encoding utf8
        $pages.Add("$page.md")
    }

    if ($broken.Count -gt 0) {
        throw "Unresolved documentation links (fix the link or add the page to docs\):`n  " +
            ($broken -join "`n  ")
    }

    if ($readmeRendered) {
        Set-Content -LiteralPath (Join-Path $stage '_Sidebar.md') `
            -Value (New-Sidebar -ReadmeText $readmeRendered) -NoNewline -Encoding utf8
        $pages.Add('_Sidebar.md')
    }

    $footer = "_Generated from [``docs/``]($repoUrl/tree/$Ref/docs) on the " +
        "``$Ref`` branch of the [WebJEA repository]($repoUrl). " +
        "Do not edit the wiki directly - changes here are overwritten._`n"
    Set-Content -LiteralPath (Join-Path $stage '_Footer.md') -Value $footer -NoNewline -Encoding utf8
    $pages.Add('_Footer.md')

    # Images and other attachments referenced by the docs, path preserved.
    $assets = [System.Collections.Generic.List[string]]::new()
    foreach ($asset in (Get-ChildItem -Path $docsRoot -File -Recurse | Where-Object { $_.Extension -ne '.md' })) {
        $relative = [System.IO.Path]::GetRelativePath($docsRoot, $asset.FullName)
        $destination = Join-Path $stage $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
        Copy-Item -LiteralPath $asset.FullName -Destination $destination -Force
        $assets.Add($relative)
    }

    if ($TestOnly) {
        Write-Host "Rendered $($pages.Count) pages and $($assets.Count) assets to '$stage' (test only)."
        return @{
            Pages    = $pages.ToArray()
            Assets   = $assets.ToArray()
            Removed  = @()
            WikiPath = $stage
        }
    }

    $wikiRoot = (Resolve-Path $WikiPath).Path

    # Replace the wiki's content wholesale: anything the docs no longer produce
    # is a stale page and must go. .git is the wiki clone's own metadata.
    $removed = [System.Collections.Generic.List[string]]::new()
    $keep = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($name in $pages) { $keep.Add($name) | Out-Null }
    foreach ($name in $assets) { $keep.Add($name) | Out-Null }

    foreach ($existing in (Get-ChildItem -Path $wikiRoot -File -Recurse -Force |
            Where-Object { $_.FullName -notlike (Join-Path $wikiRoot '.git*') })) {
        $relative = [System.IO.Path]::GetRelativePath($wikiRoot, $existing.FullName)
        if (-not $keep.Contains($relative)) {
            Remove-Item -LiteralPath $existing.FullName -Force
            $removed.Add($relative)
        }
    }

    Copy-Item -Path (Join-Path $stage '*') -Destination $wikiRoot -Recurse -Force

    Write-Host "Wiki tree at '$wikiRoot': $($pages.Count) pages, $($assets.Count) assets, $($removed.Count) removed."

    return @{
        Pages    = $pages.ToArray()
        Assets   = $assets.ToArray()
        Removed  = $removed.ToArray()
        WikiPath = $wikiRoot
    }
} finally {
    if (-not $TestOnly) { Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction Ignore }
}
