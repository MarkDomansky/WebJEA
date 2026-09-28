# WebJEA Integration Test psake definition
# Invoked via TestDeploy.ps1 or Invoke-IntegrationTests.ps1
# Each wrapper passes properties and calls a specific top-level task.

Properties {
    $dcStartupDelay = 60
    $timeout = 900
    $doNotRunDeploy = $false
    $resetVM = $false
    # Which PowerShell edition runs Deploy.ps1 on the VM. The script supports both
    # (#Requires -Version 5.1, no DSC or other edition-specific dependencies); this
    # switches the deploy task between them so both paths are actually covered.
    # 'WindowsPowerShell' (the default) runs it in the PowerShell Direct session
    # itself; 'PowerShell7' shells out to pwsh.exe on the VM, which must be installed
    # there.
    $deployEdition = 'WindowsPowerShell'
}

# ---------------------------------------------------------------------------
#  Shared tasks
# ---------------------------------------------------------------------------
#
Task Init {
    $script:configPath = $ConfigPath
    $script:helpersPath = $helpersPath
    $script:UseGitHubBuild = $UseGitHubBuild
    $script:quickBuild = $QuickBuild
    $script:doNotRunDeploy = $DoNotRunDeploy
    $script:resetVM = $ResetVM
    $script:deployEdition = if ($DeployEdition) { $DeployEdition } else { 'WindowsPowerShell' }
    Assert ($script:deployEdition -in @('WindowsPowerShell', 'PowerShell7')) "DeployEdition '$script:deployEdition' is not valid. Use 'WindowsPowerShell' or 'PowerShell7'."
    Assert ($script:configPath -and (Test-Path $script:configPath)) "ConfigPath '$script:configPath' not found."
    Assert ($script:helpersPath -and (Test-Path $script:helpersPath)) "HelpersPath '$script:helpersPath' not found."

    # Bootstrap Write-Log into the global scope so helper scripts can use it
    $writeLogContent = Get-Content -Path ("$script:helpersPath\Write-Log.ps1") -Raw
    $writeLogContent = "function global:Write-Log { $writeLogContent }"
    [scriptblock]::Create($writeLogContent).Invoke()

    $script:buildOutputDir = "$env:temp\WebJEABuild"
    $script:buildInfoFile = "$script:buildOutputDir\build-info.json"

    Write-Log '=== WebJEA Integration (psake) ==='
    Write-Log ''
    Write-Log 'Build Properties:' -Level Information
    Write-Log "  Config Path:          $script:configPath"
    Write-Log "  Helpers Path:         $script:helpersPath"
    Write-Log "  Use GitHub Build:     $script:useGitHubBuild"
    Write-Log "  Quick Build:          $script:quickBuild"
    Write-Log "  Do Not Run Deploy:    $script:doNotRunDeploy"
    Write-Log "  Deploy Edition:       $script:deployEdition"
    Write-Log "  Reset VM:             $script:resetVM"
    Write-Log "  BuildOutputDir:       $script:buildOutputDir"
    Write-Log "  BuildInfoFile:        $script:buildInfoFile"
    Write-Log "  Tags:                 $(if ($script:tags.Count -gt 0) { ($script:tags -join ', ') } else { '(none)' })"
    Write-Log "  Exclude Tags:         $(if ($script:excludeTags.Count -gt 0) { ($script:excludeTags -join ', ') } else { '(none)' })"
    Write-Log "  Output Path:          $(if ($script:outputPath) { $script:outputPath } else { '(default)' })"
    Write-Log ''

    $script:config = Get-Content -Path $script:configPath -Raw | ConvertFrom-Json #-ashashtable is PS7 only
    Assert $script:config "Failed to load configuration from $script:configPath"

    $script:webVMName = $script:config.HyperV.WebServerVMName
    $script:dcVMName = $script:config.HyperV.DomainControllerVMName
    $script:dcStartupDelay = if ($script:config.HyperV.DCStartupDelay) { $script:config.HyperV.DCStartupDelay } else { 60 }
    $script:timeout = if ($script:config.HyperV.VMOperationTimeout) { $script:config.HyperV.VMOperationTimeout } else { 300 }
    $script:snapshotName = $script:config.HyperV.SnapshotName

    Write-Log 'Resolved Configuration:' -Level Information
    Write-Log "  WebServer VM:         $script:webVMName"
    Write-Log "  DC VM:                $script:dcVMName"
    Write-Log "  Snapshot Name:        $script:snapshotName"
    Write-Log "  DC Startup Delay:     $script:dcStartupDelay seconds"
    Write-Log "  VM Operation Timeout: $script:timeout seconds"
    Write-Log ''
}

Task GetCredential -Depends Init {
    Write-Log 'Retrieving VM credential...'
    $script:credential = & $script:helpersPath\Get-VMCredential.ps1 `
        -CredentialConfig $script:config.Credentials `
        -NonInteractive:$true
    Assert $script:credential 'Failed to retrieve VM credentials.'
    Write-Log 'Credentials retrieved' -Level Success
}

Task StartVM_DC -Depends Init {
    write-log 'Starting DC...'
    & $script:helpersPath\StartVM.ps1 -VMName $script:dcVMName -StartupDelay $script:dcStartupDelay -TimeoutSeconds $script:timeout
}

Task StartVM_Web -Depends StartVM_DC {
    write-log 'Starting Web Server...'
    & $script:helpersPath\StartVM.ps1 -VMName $script:webVMName -StartupDelay 0 -TimeoutSeconds $script:timeout
}

Task RestartVM_Web -Depends StartVM_DC {
    write-log 'Restarting Web Server...'
    & $script:helpersPath\StartVM.ps1 -VMName $script:webVMName -StartupDelay 0 -TimeoutSeconds $script:timeout
}

Task StartVMs -Depends StartVM_DC, StartVM_Web {}

Task StopVM_Web -Depends Init {
    Write-Log "Stopping Web Server VM '$script:webVMName' for snapshot revert..."
    #request shutdown
    Stop-VM -Name $script:webVMName -TurnOff

    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    while ((Get-VM -Name $script:webVMName).State -ne 'Off' -and $stopwatch.Elapsed.TotalSeconds -lt 300)
    {
        Start-Sleep -Seconds 5
    }
    #force shutdown
    Stop-VM -Name $script:webVMName -TurnOff -Force
    while ((Get-VM -Name $script:webVMName).State -ne 'Off' -and $stopwatch.Elapsed.TotalSeconds -lt 450)
    {
        Start-Sleep -Seconds 5
    }

    if ((Get-VM -Name $script:webVMName).State -ne 'Off')
    {
        Write-Log "Failed to stop Web Server VM '$script:webVMName' within timeout" -Level Error
        throw "Unable to stop Web Server VM '$script:webVMName'"
    }
    Write-Log 'Web Server VM stopped successfully' -Level Success
}

# ---------------------------------------------------------------------------
#  Deploy tasks
# ---------------------------------------------------------------------------
#TODO Create a certificate on the remote server, update the settings file with the thumbprint

Task CleanBuildOutput -Depends Init -PreCondition { -not $script:useGithubBuild -and -not $script:quickBuild } {
    if (-not (Test-Path $script:buildInfoFile)) { New-Item -Path $script:buildOutputDir -ItemType directory -Force | Out-Null }

    Write-Log "Cleaning existing build output directory: $script:buildOutputDir"
    Get-ChildItem $script:buildOutputDir | Remove-Item -Recurse -Force

}

Task BuildPackage -Depends CleanBuildOutput -PreCondition { -not $script:useGithubBuild -and -not $script:quickBuild } {
    $buildScriptPath = Resolve-Path -LiteralPath (Join-Path (Split-Path $script:configPath) '..\..\.github\workflows')
    $buildScriptFilePath = "$buildScriptPath\build.ps1"
    $outputDir = $script:buildOutputDir
    Write-Log "Resolved build script path: $buildScriptFilePath"
    write-log "Moving to build script path: $buildScriptPath"
    Push-Location $buildScriptPath #script needs to run from the correct folder

    write-log "OutputPath: $outputDir"
    Write-Log "Executing: $buildScriptFilePath"
    $SblockStr = "& '$buildScriptFilePath' -CreateZip -outputPath '$buildOutputDir' -BuildConfiguration Release"
    write-log "Build command: $SblockStr"
    $job = start-threadjob -ScriptBlock ([scriptblock]::Create($SblockStr))
    $job | Wait-Job -Timeout 1800 | Out-Null
    Receive-Job -Job $job -ov BuildResult
    # $buildResult = invoke-command -ScriptBlock $SBlock -ErrorAction Stop

    Assert $buildResult.ZipPath 'Build failed or did not return ZipPath'
    Write-Log "Build completed. Zip: $($buildResult.ZipPath)"

    write-log "Moving back to original location"
    Pop-Location
}

Task QuickCopyDeployScript -Depends Init -PreCondition { $script:quickBuild -and -not $script:useGitHubBuild } {
    $releaseFilesPath = Resolve-Path -LiteralPath (Join-Path (Split-Path $script:configPath) '..\..\ReleaseFiles')
    $deployScriptSource = Join-Path $releaseFilesPath 'Deploy.ps1'
    Assert (Test-Path $deployScriptSource) "Deploy.ps1 not found at: $deployScriptSource"

    if (-not (Test-Path $script:buildOutputDir)) {
        New-Item -Path $script:buildOutputDir -ItemType Directory -Force | Out-Null
    }

    Write-Log 'Quick Build: Copying ReleaseFiles to build folder...'
    Write-Log "  Source:      $releaseFilesPath"
    Write-Log "  Destination: $script:buildOutputDir"
    Copy-Item -Path (Join-Path $releaseFilesPath '*') -Destination $script:buildOutputDir -Recurse -Force
    Write-Log 'ReleaseFiles copied successfully' -Level Success
}

Task ResolveBuildPackage -Depends Init, BuildPackage, QuickCopyDeployScript {
    if ($script:useGitHubBuild)
    {
        Write-Log 'Using GitHub release (via -UseGitHubBuild)'
        $release = & $script:helpersPath\Get-GitHubRelease.ps1 `
            -Owner $script:config.GitHub.Owner `
            -Repository $script:config.GitHub.Repository `
            -Tag $script:config.GitHub.ReleaseTag `
            -AssetPattern $script:config.GitHub.AssetNamePattern

        Write-Log "Release: $($release.TagName) - Asset: $($release.AssetName)"

        $script:deploymentPackage = @{
            DownloadUrl = $release.DownloadUrl
            ZipName     = $release.AssetName
            Version     = $release.TagName
            IsLocal     = $false
        }
    }
    else
    {
        Write-Log 'Using local build for deployment (default)'

        if (-not (Test-Path $script:buildInfoFile)) { throw "Build info file not found: $script:buildInfoFile. Run Build failed?  Use -UseGitHubBuild to use public release." }

        Write-Log 'Using existing build output. Loading build-info.json...'
        $buildResult = Get-Content -Path $script:buildInfoFile -Raw | ConvertFrom-Json

        Assert (Test-Path $buildResult.ZipPath) "Zip file not found: $($buildResult.ZipPath). Delete build-output and retry."

        $script:deploymentPackage = @{
            ZipPath = $buildResult.ZipPath
            ZipName = Split-Path -Leaf $buildResult.ZipPath
            Version = $buildResult.Version
            IsLocal = $true
        }

        Write-Log "Package: $($script:deploymentPackage.ZipName) (Version: $($script:deploymentPackage.Version))"
    }
}

Task ResolveSettings -Depends Init {
    $settingsSourcePath = $script:config.Deployment.LocalSettingsPath
    if (-not [System.IO.Path]::IsPathRooted($settingsSourcePath))
    {
        $settingsSourcePath = Join-Path (Split-Path $script:configPath) $settingsSourcePath
    }
    Assert (Test-Path $settingsSourcePath) "Settings file not found: $settingsSourcePath"
    $script:settingsSourcePath = $settingsSourcePath
    Write-Log "Using settings file: $settingsSourcePath"
}

Task StageDeployment -Depends GetCredential, StartVMs, ResolveBuildPackage, ResolveSettings {
    $pkg = $script:deploymentPackage

    Write-Log "Establishing PowerShell Direct session to VM '$script:webVMName'..."
    $session = New-PSSession -VMName $script:webVMName -Credential $script:credential -ErrorAction Stop
    Write-Log 'Session established successfully' -Level Success

    try
    {
        $vmExtractPath = $script:config.Deployment.ExtractPath
        $vmDeployFolder = Join-Path $vmExtractPath 'WebJEA-Deploy'
        $vmZipPath = Join-Path $vmExtractPath $pkg.ZipName
        $vmSettingsPath = Join-Path $vmExtractPath $script:config.Deployment.SettingsFileName

        # Prepare directories on VM
        Write-Log 'Preparing deployment directory on VM...'
        Invoke-Command -Session $session -ScriptBlock {
            param($ExtractPath, $DeployFolder)
            if (-not (Test-Path $ExtractPath))
            {
                Write-Host "Creating extract directory: $ExtractPath" -ForegroundColor Yellow
                New-Item -Path $ExtractPath -ItemType Directory -Force | Out-Null
            }
            if (Test-Path $DeployFolder)
            {
                Write-Host "Cleaning existing deployment folder: $DeployFolder" -ForegroundColor Yellow
                Remove-Item -Path $DeployFolder -Recurse -Force
            }
        } -ArgumentList $vmExtractPath, $vmDeployFolder

        # Transfer package
        if ($pkg.IsLocal)
        {
            Write-Log 'Copying local build zip to VM...'
            Write-Log "Source: $($pkg.ZipPath)"
            Write-Log "Destination: $script:webVMName > $vmZipPath"
            Copy-Item -Path $pkg.ZipPath -Destination $vmZipPath -ToSession $session -Force
            Write-Log 'Package copied successfully' -Level Success
        }
        else
        {
            Write-Log 'Downloading release from GitHub to VM...'
            Write-Log "Source: $($pkg.DownloadUrl)"
            Write-Log "Destination: $script:webVMName > $vmZipPath"
            Invoke-Command -Session $session -ScriptBlock {
                param($DownloadUrl, $ZipPath)
                [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
                $webClient = New-Object System.Net.WebClient
                $webClient.DownloadFile($DownloadUrl, $ZipPath)
                Write-Output "Download complete. Size: $((Get-Item $ZipPath).Length) bytes"
            } -ArgumentList $pkg.DownloadUrl, $vmZipPath
            Write-Log 'Package downloaded successfully' -Level Success
        }

        # Transfer settings
        Write-Log 'Copying settings file to VM...'
        Copy-Item -Path $script:settingsSourcePath -Destination $vmSettingsPath -ToSession $session -Force
        Write-Log 'Settings file copied successfully' -Level Success

        # Extract package and locate Deploy.ps1 on VM
        Write-Log 'Extracting deployment package on VM...'
        $staged = Invoke-Command -Session $session -ScriptBlock {
            param($DeployFolder, $ZipPath, $SettingsPath, $SettingsFileName)
            $ErrorActionPreference = 'Stop'

            Write-Host "Extracting $ZipPath to $DeployFolder"
            Expand-Archive -Path $ZipPath -DestinationPath $DeployFolder -Force

            $found = Get-ChildItem -Path $DeployFolder -Filter 'Deploy.ps1' -Recurse | Select-Object -First 1
            if (-not $found) { throw 'Deploy.ps1 not found in extracted content' }
            $deployRoot = $found.Directory.FullName

            Write-Host "Deployment root: $deployRoot"

            $targetSettingsPath = Join-Path $deployRoot $SettingsFileName
            Write-Host "Copying settings file $SettingsPath to $targetSettingsPath"
            Copy-Item -Path $SettingsPath -Destination $targetSettingsPath -Force

            return @{
                DeployRoot   = $deployRoot
                SettingsPath = $targetSettingsPath
            }
        } -ArgumentList $vmDeployFolder, $vmZipPath, $vmSettingsPath, $script:config.Deployment.SettingsFileName

        $script:stagedDeployRoot   = $staged.DeployRoot
        $script:stagedSettingsPath = $staged.SettingsPath

        if ($script:quickBuild) {
            Write-Log 'Quick Build: Overriding ReleaseFiles in VM deploy root...'
            $localReleaseContents = Join-Path $script:buildOutputDir '*'
            Copy-Item -Path $localReleaseContents -Destination $script:stagedDeployRoot -ToSession $session -Recurse -Force
            Write-Log 'ReleaseFiles overridden successfully' -Level Success
        }

        Write-Log "Staging complete. Deploy root on VM: $script:stagedDeployRoot" -Level Success
    }
    finally
    {
        if ($session)
        {
            Write-Log 'Closing PowerShell Direct session...'
            Remove-PSSession -Session $session -ErrorAction SilentlyContinue
        }
    }
}

Task RunDeployScript -Depends GetCredential, StageDeployment -PreCondition { -not $script:doNotRunDeploy } {
    Write-Log "Establishing PowerShell Direct session to VM '$script:webVMName'..."
    $session = New-PSSession -VMName $script:webVMName -Credential $script:credential -ErrorAction Stop
    Write-Log 'Session established successfully' -Level Success

    try
    {
        Write-Log 'Executing Deploy.ps1 on VM...'
        Write-Log "  Deploy root:   $script:stagedDeployRoot"
        Write-Log "  Settings file: $script:stagedSettingsPath"
        Write-Log "  Edition:       $script:deployEdition"

        $result = Invoke-Command -Session $session -ScriptBlock {
            param($DeployRoot, $SettingsPath, $Edition)
            $ErrorActionPreference = 'Stop'

            $deployScript = Get-ChildItem -Path $DeployRoot -Filter 'Deploy.ps1' -File | Select-Object -First 1
            if (-not $deployScript) { throw "Deploy.ps1 not found in: $DeployRoot" }

            # Ensure this process allows running the deploy script (temporary for this process only)
            Set-ExecutionPolicy -ExecutionPolicy Bypass -Scope Process -Force

            if ($Edition -eq 'PowerShell7')
            {
                # PowerShell Direct lands in Windows PowerShell 5.1, so the PS7 pass has to
                # shell out. Deploy.ps1 supports both editions; this is what proves it.
                $pwsh = Get-Command pwsh.exe -ErrorAction SilentlyContinue
                if (-not $pwsh)
                {
                    throw 'DeployEdition PowerShell7 was requested but pwsh.exe is not on the VM. Install PowerShell 7 there, or run with the default WindowsPowerShell edition.'
                }
                Write-Host "Executing $($deployScript.FullName) under $($pwsh.Source)..."
                $deployResult = & $pwsh.Source -NoProfile -ExecutionPolicy Bypass `
                    -File $deployScript.FullName -SettingsFile $SettingsPath -Verbose 2>&1
                # A child process reports failure through its exit code, not a thrown error.
                if ($LASTEXITCODE -ne 0)
                {
                    throw "Deploy.ps1 under pwsh.exe exited with code $LASTEXITCODE.`n$($deployResult | Out-String)"
                }
                $engine = 'pwsh.exe'
            }
            else
            {
                Write-Host "Executing $($deployScript.FullName) under Windows PowerShell $($PSVersionTable.PSVersion)..."
                $deployResult = & $deployScript.FullName -SettingsFile $SettingsPath -Verbose
                $engine = "Windows PowerShell $($PSVersionTable.PSVersion)"
            }

            return @{
                Success      = $true
                DeployScript = $deployScript.FullName
                Engine       = $engine
                Output       = $deployResult | Out-String
            }
        } -ArgumentList $script:stagedDeployRoot, $script:stagedSettingsPath, $script:deployEdition

        write-host ($result | convertto-json -depth 5)
        Assert $result.Success "Deployment failed: $($result.Output)"

        Write-Log "Deployment completed successfully! (ran under $($result.Engine))" -Level Success
        if ($result.Output)
        {
            Write-Log 'Deployment output:'
            $result.Output -split "`n" | ForEach-Object {
                if ($_) { Write-Log "  $_" }
            }
        }
    }
    finally
    {
        if ($session)
        {
            Write-Log 'Closing PowerShell Direct session...'
            Remove-PSSession -Session $session -ErrorAction SilentlyContinue
        }
    }
}

Task DeployToVM -Depends StageDeployment, RunDeployScript {}

Task Deploy -Depends DeployToVM {
    Write-Log 'WebJEA deployment completed successfully!' -Level Success
}

# ---------------------------------------------------------------------------
#  Snapshot tasks
# ---------------------------------------------------------------------------

Task RevertSnapshot_Web -Depends StopVM_Web {
    Write-Log "Reverting web server to snapshot '$script:snapshotName'..."

    $snapshot = Get-VMSnapshot -VMName $script:webVMName -Name $script:snapshotName -ErrorAction Stop
    Write-Log "Found snapshot: $script:snapshotName (Created: $($snapshot.CreationTime))"
    if (-not $snapshot)
    {
        throw "Snapshot '$script:snapshotName' not found for VM '$script:webVMName'"
    }

    Restore-VMSnapshot -VMName $script:webVMName -Name $script:snapshotName -Confirm:$false
    Write-Log 'Snapshot reverted successfully' -Level Success
}

Task RevertVMs -Depends RevertSnapshot_Web {
    Write-Log "VMs reverted to baseline snapshot '$script:snapshotName'" -Level Success
}

# ---------------------------------------------------------------------------
#  Integration test tasks
# ---------------------------------------------------------------------------

Task ConfigureTestOutput -Depends Init {
    if (-not $script:outputPath)
    {
        $script:outputPath = $script:config.IntegrationTests.OutputPath
        if (-not [System.IO.Path]::IsPathRooted($script:outputPath))
        {
            $script:outputPath = Join-Path (Split-Path $script:configPath) $script:outputPath
        }
    }

    if (-not (Test-Path $script:outputPath))
    {
        New-Item -Path $script:outputPath -ItemType Directory -Force | Out-Null
    }
    Write-Log "Test output: $script:outputPath"
}

Task GetTestCredentials -Depends Init {
    $script:testCredential = & $script:helpersPath\Get-VMCredential.ps1 -CredentialConfig $script:config.Credentials -NonInteractive:$false 2>$null
    if (-not $script:testCredential)
    {
        $script:testCredential = [System.Management.Automation.PSCredential]::Empty
    }
}

Task ConfigureSsl -Depends Init -PreCondition { $script:config.WebServer.IgnoreSslErrors } {
    Write-Log 'Configuring to ignore SSL certificate errors'
    if ($PSVersionTable.PSEdition -eq 'Core')
    {
        #pwsh's Invoke-WebRequest ignores ServicePointManager.CertificatePolicy; the
        #tests read WebServer.IgnoreSslErrors themselves and pass -SkipCertificateCheck.
        Write-Log 'PowerShell 7+: tests will use -SkipCertificateCheck'
        return
    }
    if (-not ([System.Management.Automation.PSTypeName]'TrustAllCertsPolicy').Type)
    {
        Add-Type @'
using System.Net;
using System.Security.Cryptography.X509Certificates;
public class TrustAllCertsPolicy : ICertificatePolicy {
    public bool CheckValidationResult(ServicePoint srvPoint, X509Certificate certificate,
        WebRequest request, int certificateProblem) { return true; }
}
'@
    }
    [System.Net.ServicePointManager]::CertificatePolicy = New-Object TrustAllCertsPolicy
    [System.Net.ServicePointManager]::SecurityProtocol = [System.Net.SecurityProtocolType]::Tls12
}

Task RunPesterTests -Depends ConfigureTestOutput, GetTestCredentials, ConfigureSsl {
    $testFile = Join-Path (Split-Path $script:configPath) 'WebJEA.Tests.ps1'
    Assert (Test-Path $testFile) "Test file not found: $testFile"

    $pesterConfig = New-PesterConfiguration

    $testContainer = New-PesterContainer -Path $testFile -Data @{
        Config     = $script:config
        Credential = $script:testCredential
    }
    $pesterConfig.Run.Container = $testContainer
    $pesterConfig.Run.PassThru = $true

    # Tags
    if ($script:tags -and $script:tags.Count -gt 0)
    {
        $pesterConfig.Filter.Tag = $script:tags
    }
    elseif ($script:config.IntegrationTests.IncludeTags.Count -gt 0)
    {
        $pesterConfig.Filter.Tag = $script:config.IntegrationTests.IncludeTags
    }

    if ($script:excludeTags -and $script:excludeTags.Count -gt 0)
    {
        $pesterConfig.Filter.ExcludeTag = $script:excludeTags
    }
    elseif ($script:config.IntegrationTests.ExcludeTags.Count -gt 0)
    {
        $pesterConfig.Filter.ExcludeTag = $script:config.IntegrationTests.ExcludeTags
    }

    # Output
    $outputFile = Join-Path $script:outputPath "TestResults_$(Get-Date -Format 'yyyyMMdd_HHmmss').xml"
    $pesterConfig.TestResult.Enabled = $true
    $pesterConfig.TestResult.OutputPath = $outputFile
    $pesterConfig.TestResult.OutputFormat = $script:config.IntegrationTests.OutputFormat
    $pesterConfig.Output.Verbosity = 'Detailed'

    Write-Log 'Starting integration tests...'
    Write-Log "Target: $($script:config.WebServer.BaseUrl)"
    Write-Log "Output: $outputFile"

    $script:testResult = Invoke-Pester -Configuration $pesterConfig

    Write-Log 'Test Results Summary:'
    Write-Log "  Total Tests: $($testResult.TotalCount)"
    Write-Log "  Passed: $($testResult.PassedCount)" -Level Success
    Write-Log "  Failed: $($testResult.FailedCount)" -Level $(if ($testResult.FailedCount -gt 0) { 'Error' } else { 'Information' })
    Write-Log "  Skipped: $($testResult.SkippedCount)" -Level Warning

    if ($testResult.FailedCount -gt 0)
    {
        Write-Log 'Failed Tests:' -Level Error
        $testResult.Failed | ForEach-Object {
            Write-Log "  - $($_.Name): $($_.ErrorRecord.Exception.Message)" -Level Error
        }
        throw "Integration tests failed: $($testResult.FailedCount) test(s) failed."
    }

    Write-Log 'All integration tests passed!' -Level Success
}

Task IntegrationTest -Depends RunPesterTests {
    Write-Log 'Integration test run completed successfully!' -Level Success
}

