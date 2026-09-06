<#
.SYNOPSIS
    Builds WebJEA project and prepares a release package.

.DESCRIPTION
    Entry point that invokes psake tasks defined in build.psake.ps1.
    This script preserves the same parameter interface and return contract
    used by downstream callers (Deploy-WebJEA, integration tests, CI).

    Build steps (psake tasks):
    1. Init              - Resolve paths, take the version passed in (-Version)
    2. GenerateAwsSecrets - Materialize AwsSecrets.cs from the template (CI secrets)
    3. Restore           - dotnet restore (skippable)
    4. Compile           - dotnet build with the calculated version
    5. Test              - dotnet test (xUnit) + npm test (Jest)
    6. Publish           - dotnet publish into the site folder
    7. CopyReleaseFiles  - Copy ReleaseFiles\* plus LICENSE and
                           LICENSE-attributions into the release package
    8. CreateZip         - Create zip archive (optional)
    9. SaveBuildInfo     - Write build-info.json

.PARAMETER OutputPath
    Path where the release package will be created. Default: .\build-output

.PARAMETER CreateZip
    If specified, creates a zip file of the release package.

.PARAMETER BuildConfiguration
    Build configuration. Default: Release

.PARAMETER SkipNuGetRestore
    If specified, skips the NuGet package restore step.

.PARAMETER Version
    Version to stamp into the build (semver, e.g. 2100.0.0-alpha.1 or 2100.0.0).
    CI passes the value semantic-release computed; defaults to 0.0.0-local.

.EXAMPLE
    .\Build.ps1 -OutputPath .\build-output

.EXAMPLE
    .\Build.ps1 -OutputPath "C:\Builds" -CreateZip

.EXAMPLE
    .\Build.ps1 -OutputPath .\build-output -BuildConfiguration Debug

.OUTPUTS
    Hashtable with build information:
    - Version: The version number used
    - OutputPath: Path to the release package
    - SettingsTemplatePath: Path to settings.template.conf
    - ZipPath: Path to the zip file (if -CreateZip was specified)
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$OutputPath,

    [Parameter()]
    [switch]$CreateZip,

    [Parameter()]
    [ValidateSet('Debug', 'Release')]
    [string]$BuildConfiguration = 'Release',

    [Parameter()]
    [switch]$SkipNuGetRestore,

    # Version to stamp into the build, e.g. 2100.0.0-alpha.1 or 2100.0.0.
    # CI passes the version semantic-release computed from the commit history;
    # local builds get a placeholder so nothing looks like a real release.
    [Parameter()]
    [string]$Version = '0.0.0-local'
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'Continue'

# Ensure psake is available
if (-not (Get-Module -ListAvailable -Name psake -verbose:$false))
{
    Write-Host 'psake module not found. Installing from PSGallery...' -ForegroundColor Yellow
    Install-Module -Name psake -Scope CurrentUser -Force -Verbose:$false
}
Import-Module psake -ErrorAction Stop -verbose:$false

$VerbosePreference = 'Continue' #Despite verbose being set to false for Install-Module and Import-Module, we don't turn on verbose until after.

#calculate the output path
if (-not (Test-Path -Path $OutputPath -IsValid)) { throw "Invalid OutputPath: $OutputPath" }
$outputPathStr = if ([System.IO.Path]::IsPathRooted($OutputPath))
{
    [System.IO.Path]::GetFullPath($OutputPath)
}
else
{
    [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($psscriptroot, $OutputPath))
}

#calculate the buildinfopath
$buildInfoPath = "$outputPathStr\build-info.json"

$TemplatePath = (Resolve-Path "$PSScriptRoot\..\..\ReleaseFiles").path

$psakeParams = @{
    buildFile  = "$PSScriptRoot\build.psake.ps1"
    parameters = @{
        BuildConfiguration = $BuildConfiguration
        outputPath         = $outputPathStr
        TemplatePath       = $TemplatePath
        repoRoot           = (Resolve-Path ("$PSScriptRoot\..\..")).Path
        createZip          = [bool]$CreateZip
        skipNuGetRestore   = [bool]$SkipNuGetRestore
        buildInfoPath      = "$outputPathStr\build-info.json"
        Version            = $Version
    }
    nologo     = $true
}
# Write-Host ($psakeparams | ConvertTo-Json | Out-String)

# Invoke psake with output not buffered to show task execution in real-time
# invoke-psake @psakeParams
$null = @(Invoke-psake @psakeParams 4>&1 | ForEach-Object { $_ | Out-Host; $_ })

if (-not $psake.build_success)
{
    Write-Host '=' * 80 -ForegroundColor Red
    Write-Host 'BUILD FAILED - Detailed Failure Information' -ForegroundColor Red
    Write-Host '=' * 80 -ForegroundColor Red

    Write-Host "`nBuild Status:" -ForegroundColor Yellow
    Write-Host "  build_success: $($psake.build_success)"
    Write-Host "  task_count: $($psake.build_script_task_count)"

    if ($psake.error_message)
    {
        Write-Host "`nError Message:" -ForegroundColor Yellow
        Write-Host "  $($psake.error_message)"
    }

    if ($psake.build_script_errors)
    {
        Write-Host "`nBuild Script Errors:" -ForegroundColor Yellow
        $psake.build_script_errors | ForEach-Object {
            Write-Host "  - $_" -ForegroundColor Red
        }
    }

    Write-Host "`npsake Object Details:" -ForegroundColor Yellow
    $psake | Format-List -Property * | Out-String | Write-Host

    Write-Host '=' * 80 -ForegroundColor Red
    throw 'Build failed. See detailed failure information above.'
}

if (-not (Test-Path $buildInfoPath))
{
    Write-Host 'ERROR: build-info.json not created' -ForegroundColor Red
    throw "build-info.json not found at $buildInfoPath after successful build."
}

$result = Get-Content -Path $buildInfoPath -Raw | ConvertFrom-Json
$result | ConvertTo-Json | Format-List
return $result
