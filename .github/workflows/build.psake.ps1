# WebJEA psake build definition
Properties {
    #These will all be built during Init (or later)
}

FormatTaskName {
    param($taskName)
    Write-Host ">>>>> Executing $taskName <<<<<" -ForegroundColor cyan
}

Task default -Depends Summary

Task Init {
    #copy from parameters passed by the invoking script to script-scoped variables for easier access in tasks
    $script:repoRoot = $repoRoot
    $script:outputPath = $OutputPath
    $script:buildInfoPath = $buildInfoPath
    $script:buildConfiguration = $buildConfiguration
    $script:createZip = $CreateZip
    $script:skipNuGetRestore = $SkipNuGetRestore
    $script:templatePath = $TemplatePath

    #calculate a couple variables
    $script:solutionsPath = "$script:repoRoot\WebJEA"
    $script:solutionFilePath = "$script:solutionsPath\WebJEA.sln"
    $script:projectFilePath = "$script:solutionsPath\WebJEA.csproj"
    $script:testProjectFilePath = "$script:repoRoot\Test\Unit\WebJEA.Tests.csproj"
    # The version is passed in by build.ps1 (from semantic-release in CI); this
    # script never invents one. dotnet accepts semver here: -p:Version=2100.0.0-alpha.1
    # yields AssemblyVersion 2100.0.0.0 and InformationalVersion 2100.0.0-alpha.1.
    $script:version = if ($Version) { $Version } else { '0.0.0-local' }

    Write-Host 'Resolved Paths:'
    Write-Host "  repoRoot:            $script:repoRoot"
    Write-Host "  solutionsPath:       $script:solutionsPath"
    Write-Host "  solutionFilePath:    $script:solutionFilePath"
    Write-Host "  projectFilePath:     $script:projectFilePath"
    Write-Host "  testProjectFilePath: $script:testProjectFilePath"
    Write-Host "  templatePath:        $script:templatePath"
    Write-Host "  outputPath:          $script:outputPath"
    Write-Host "  buildInfoPath:       $script:buildInfoPath"
    Write-Host 'Build Properties:'
    Write-Host "  Build Configuration: $script:buildConfiguration"
    Write-Host "  Create Zip:          $script:createZip"
    Write-Host "  Skip Restore:        $script:skipNuGetRestore"
    Write-Host "  version:             $script:version"
    Write-Host ''
}

Task GenerateAwsSecrets -Depends Init {
    $templatePath = "$script:solutionsPath\Telemetry\AwsSecrets.template.cs"
    $outputPath = "$script:solutionsPath\Telemetry\AwsSecrets.cs"

    if (-not (Test-Path $templatePath))
    {
        throw "AWS secrets template not found: $templatePath"
    }

    $envMap = [ordered]@{
        '{{AWS_KEY}}'       = $env:AWS_KEY
        '{{AWS_KEYSEC}}'    = $env:AWS_KEYSEC
        '{{AWS_QUEUE_URL}}' = $env:AWS_QUEUE_URL
    }

    $providedCount = ($envMap.Values | Where-Object { $_ }).Count

    if ($providedCount -eq $envMap.Count)
    {
        $content = Get-Content $templatePath -Raw -Encoding UTF8
        foreach ($placeholder in $envMap.Keys)
        {
            $content = $content.Replace($placeholder, $envMap[$placeholder])
        }
        $content.Trim() | Out-File $outputPath -Encoding UTF8
        Write-Host "Generated AwsSecrets.cs from template with CI secrets: $outputPath"
    }
    elseif (Test-Path $outputPath)
    {
        Write-Host "Using existing AwsSecrets.cs (local development): $outputPath"
    }
    else
    {
        # The csproj compiles the template's placeholder values when AwsSecrets.cs is
        # absent, so the build still succeeds; telemetry sends will silently fail.
        Write-Host 'AWS environment variables not set and no AwsSecrets.cs present; building with placeholder template values.' -ForegroundColor Yellow
    }
}

Task Restore -Depends Init -PreCondition { -not $script:skipNuGetRestore } {
    & dotnet restore $script:solutionFilePath
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE" }
}

Task Compile -Depends GenerateAwsSecrets, Restore {
    & dotnet build $script:solutionFilePath --configuration $script:buildConfiguration --no-restore -p:Version=$script:version
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE" }
}

Task Test -Depends Compile {
    & dotnet test $script:testProjectFilePath --configuration $script:buildConfiguration --no-build --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw "dotnet test failed with exit code $LASTEXITCODE" }

    # Jest front-end tests (repo-root package.json)
    $npm = Get-Command npm -ErrorAction SilentlyContinue
    if ($npm)
    {
        Push-Location $script:repoRoot
        try
        {
            # npm and jest write normal, non-error diagnostics (npm progress/warnings and
            # jest's "PASS <file>" reporter lines) to stderr. PowerShell turns native
            # stderr into ErrorRecords; under $ErrorActionPreference='Stop' (Windows
            # PowerShell 5.1 especially) those become terminating NativeCommandErrors, and
            # even under 'Continue' they linger in the error stream where Start-ThreadJob
            # collects them and Receive-Job later re-throws them (see the integration
            # BuildPackage task, which runs this build in a thread job). So both relax the
            # preference AND redirect each native call's stderr into the success stream,
            # writing it to the host, so nothing is left in the error stream. Real failures
            # are still caught by the $LASTEXITCODE checks below.
            $ErrorActionPreference = 'Continue'

            # Clean up any locked node_modules from previous failed runs (Windows EBUSY,
            # npm errno -4082). Windows deletes are asynchronous: files stay delete-pending
            # while antivirus/indexer handles are open, so a single silent Remove-Item can
            # leave a partial tree that npm ci then fails to rmdir. Retry until the tree is
            # actually gone, and fail loudly if it never is.
            $nodeModulesPath = "$script:repoRoot\node_modules"
            for ($attempt = 1; $attempt -le 5 -and (Test-Path $nodeModulesPath); $attempt++)
            {
                Remove-Item -Path $nodeModulesPath -Recurse -Force -ErrorAction SilentlyContinue
                if (Test-Path $nodeModulesPath)
                {
                    Write-Host "node_modules still locked after delete attempt $attempt; waiting and retrying..."
                    Start-Sleep -Seconds ($attempt * 2)
                }
            }
            if (Test-Path $nodeModulesPath)
            {
                throw "Unable to remove locked node_modules at $nodeModulesPath after 5 attempts; close any process holding files open (editor, node, antivirus scan) and re-run."
            }

            if (Test-Path "$script:repoRoot\package-lock.json") { & npm ci --no-audit --no-fund --silent 2>&1 | ForEach-Object { Write-Host "$_" } }
            else { & npm install --no-audit --no-fund --silent 2>&1 | ForEach-Object { Write-Host "$_" } }
            if ($LASTEXITCODE -ne 0) { throw "npm install failed with exit code $LASTEXITCODE" }

            & npm test --silent 2>&1 | ForEach-Object { Write-Host "$_" }
            if ($LASTEXITCODE -ne 0) { throw "npm test (jest) failed with exit code $LASTEXITCODE" }
        }
        finally
        {
            Pop-Location
        }
    }
    else
    {
        Write-Host 'npm not found; skipping Jest front-end tests.' -ForegroundColor Yellow
    }
}

Task Publish -Depends Test {
    $outSite = "$script:outputPath\site"

    if (Test-Path $script:outputPath)
    {
        Write-Host "Removing output directory contents: $script:outputPath"
        Get-ChildItem $script:outputPath | ForEach-Object { Remove-Item -Path $_.FullName -Recurse -Force }
    }

    @($script:outputPath, $outSite) | ForEach-Object {
        if (-not (Test-Path $_)) { New-Item -Path $_ -ItemType Directory -Force | Out-Null }
    }

    # Self-contained win-x64: the server needs no .NET runtime or Hosting Bundle; the
    # publish output contains WebJEA.exe that the Windows service points at directly.
    # --no-build is dropped: RID-specific publish must build RID-specific assets.
    & dotnet publish $script:projectFilePath --configuration $script:buildConfiguration -p:Version=$script:version --runtime win-x64 --self-contained true --output $outSite
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

    $fileCount = (Get-ChildItem -Path $outSite -File -Recurse).Count
    Write-Host "Publish complete: $fileCount files assembled at $outSite" -ForegroundColor Green

    $script:buildResult = @{
        Version       = $script:version
        OutputPath    = $script:outputPath
        buildInfoPath = $script:buildInfoPath
    }
}

Task CopyReleaseFiles -Depends Publish {

    if (-not (Test-Path -Path $script:templatePath -PathType Container))
    {
        throw "Template source path does not exist: $script:templatePath"
    }

    Get-ChildItem -Path $script:templatePath -File -Recurse | ForEach-Object {
        $relativePath = $_.FullName.Substring($script:templatePath.Length).TrimStart('\\')
        $destFile = Join-Path -Path $script:outputPath -ChildPath $relativePath
        $destDir = Split-Path -Path $destFile -Parent
        if (-not (Test-Path -Path $destDir)) { New-Item -Path $destDir -ItemType Directory -Force | Out-Null }
        Copy-Item -Path $_.FullName -Destination $destFile -Force
        Write-Host "  $relativePath"
    }

    # The GPL v3 license and the third-party attributions must ship with every
    # release package. They live at the repo root; copy them into the package
    # root and into the site folder so they travel with the deployed app.
    @('LICENSE', 'LICENSE-attributions') | ForEach-Object {
        $sourceFile = Join-Path -Path $script:repoRoot -ChildPath $_
        if (-not (Test-Path -Path $sourceFile -PathType Leaf))
        {
            throw "Required license file not found: $sourceFile"
        }
        Copy-Item -Path $sourceFile -Destination (Join-Path -Path $script:outputPath -ChildPath $_) -Force
        Copy-Item -Path $sourceFile -Destination (Join-Path -Path "$script:outputPath\site" -ChildPath $_) -Force
        Write-Host "  $_ (package root + site)"
    }
}

Task PackageComplete -Depends Publish, CopyReleaseFiles { }

Task CreateZip -Depends PackageComplete -PreCondition { $createZip } {
    $zipName = "webjea-$script:version.zip"
    $zipPath = "$script:outputPath\$zipName"
    Write-Host "Compressing $script:outputPath"
    Write-Host "to $zipPath..."
    if (Test-Path $zipPath) { Remove-Item -Path $zipPath -Force }

    Compress-Archive -Path "$script:outputPath\*" -DestinationPath $zipPath -Force

    $zipInfo = Get-Item $zipPath
    Write-Host "Created release archive: $zipName ($([Math]::Round($zipInfo.Length / 1MB, 2)) MB)" -ForegroundColor Green

    $script:buildResult.ZipPath = $zipPath
}

Task SaveBuildInfo -Depends PackageComplete, CreateZip {
    $script:buildInfoPath = "$($script:buildResult.OutputPath)\build-info.json"
    $script:buildResult | ConvertTo-Json | Out-File -FilePath $script:buildInfoPath -Encoding utf8 -Force
    Write-Host "buildResult:$($script:buildResult | out-string)"
    Write-Host "Build info saved: $script:buildInfoPath" -ForegroundColor Green

    if ($script:buildResult.ZipPath)
    {
        Write-Host ("Archive: $($script:buildResult.ZipPath)")
    }
}

Task Summary -Depends SaveBuildInfo {
    #empty, just here to to be used by the default task
}
