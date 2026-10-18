<#
.SYNOPSIS
    Stores the VM admin password used by TestDeploy.ps1 and Invoke-IntegrationTests.ps1.

.DESCRIPTION
    Reads the Credentials section of config.json and prompts for the password of
    Domain\Username. By default it saves the password as a SecretManagement secret named
    PasswordSecretName, installing SecretManagement/SecretStore and registering a
    default vault first if needed. With -UseFile it exports the credential to
    CredentialFilePath instead (DPAPI-encrypted; only this user on this machine can read it).

.PARAMETER ConfigPath
    Path to the configuration JSON file. Defaults to config.json in the script directory.

.PARAMETER UseFile
    Export to CredentialFilePath with Export-Clixml instead of using SecretManagement.

.EXAMPLE
    .\Set-TestCredential.ps1

.EXAMPLE
    .\Set-TestCredential.ps1 -UseFile
#>
[CmdletBinding()]
param(
    [Parameter()]
    [ValidateScript({ Test-Path $_ -PathType Leaf })]
    [string]$ConfigPath = "$PSScriptRoot\config.json",

    [Parameter()]
    [switch]$UseFile
)

$ErrorActionPreference = 'Stop'

$creds = (Get-Content -Path $ConfigPath -Raw | ConvertFrom-Json).Credentials
$username = "$($creds.Domain)\$($creds.Username)"
$credential = Get-Credential -UserName $username -Message "Password for $username (VM access)"
if (-not $credential) { throw 'No credential entered.' }

if ($UseFile) {
    if (-not $creds.CredentialFilePath) { throw "Credentials.CredentialFilePath is not set in $ConfigPath." }
    # Get-VMCredential.ps1 resolves a relative path against the current directory, as here
    $credential | Export-Clixml -Path $creds.CredentialFilePath
    Write-Host "Credential saved to $($creds.CredentialFilePath)" -ForegroundColor Green
    return
}

if (-not $creds.PasswordSecretName) { throw "Credentials.PasswordSecretName is not set in $ConfigPath." }
foreach ($module in 'Microsoft.PowerShell.SecretManagement', 'Microsoft.PowerShell.SecretStore') {
    if (-not (Get-Module -ListAvailable -Name $module)) {
        Install-Module -Name $module -Scope CurrentUser -Force
    }
}
if (-not (Get-SecretVault -ErrorAction SilentlyContinue)) {
    Register-SecretVault -Name SecretStore -ModuleName Microsoft.PowerShell.SecretStore -DefaultVault
}

# Get-VMCredential.ps1 builds the credential from Domain\Username plus this secret, so store the password only
Set-Secret -Name $creds.PasswordSecretName -Secret $credential.Password
Write-Host "Password saved to secret '$($creds.PasswordSecretName)'" -ForegroundColor Green
if ($creds.CredentialFilePath -and (Test-Path $creds.CredentialFilePath)) {
    Write-Warning "$($creds.CredentialFilePath) exists and takes precedence over the secret. Delete it to use the secret."
}
