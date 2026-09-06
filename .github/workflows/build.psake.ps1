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
    $script:projectFilePath = "$script:solutionsPath\WebJEA.vbproj"
    $script:assemblyInfoPath = "$script:solutionsPath\My Project\AssemblyInfo.vb"
    # The version is passed in by build.ps1 (from semantic-release in CI); this
    # script never invents one.
    $script:version = if ($Version) { $Version } else { '0.0.0-local' }

    $projectXml = [xml](Get-Content -Path $script:projectFilePath)

    # Filter PropertyGroup by the condition matching buildConfiguration
    $buildOutputPathRel = $projectXml.Project.PropertyGroup |
        Where-Object { $_.Condition -match $script:buildConfiguration } |
        Select-Object -First 1 -expand OutputPath
    $script:buildOutputPath = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($script:solutionsPath, $buildOutputPathRel))

    Write-Host 'Resolved Paths:'
    Write-Host "  repoRoot:            $script:repoRoot"
    Write-Host "  solutionsPath:       $script:solutionsPath"
    Write-Host "  assemblyInfoPath:    $script:assemblyInfoPath"
    Write-Host "  projectFilePath:     $script:projectFilePath"
    Write-Host "  templatePath:        $script:templatePath"
    Write-Host "  buildOutputPath:     $script:buildOutputPath"
    Write-Host "  outputPath:          $script:outputPath"
    Write-Host "  buildInfoPath:       $script:buildInfoPath"
    Write-Host 'Build Properties:'
    Write-Host "  Build Configuration: $script:buildConfiguration"
    Write-Host "  Create Zip:          $script:createZip"
    Write-Host "  Skip NuGet Restore:  $script:skipNuGetRestore"
    Write-Host "  version:             $script:version"
    Write-Host ''

}

Task UpdateAssemblyInfo -Depends Init {
    if (-not $script:assemblyInfoPath)
    {
        throw 'AssemblyInfo path not resolved. Cannot update assembly version info.'
    }
    if (-not (Test-Path $script:assemblyInfoPath))
    {
        throw "AssemblyInfo file not found at path: $script:assemblyInfoPath"
    }
    # AssemblyVersion/AssemblyFileVersion must be numeric; drop any semver
    # prerelease suffix (2100.0.0-alpha.1 -> 2100.0.0) before stamping.
    $numericVersion = ($script:version -split '-')[0]
    $content = Get-Content $script:assemblyInfoPath -Raw -Encoding UTF8
    $content = $content -replace '<Assembly: AssemblyVersion\(".*"\)>', '<Assembly: AssemblyVersion("{0}")>' -f $numericVersion
    $content = $content -replace '<Assembly: AssemblyFileVersion\(".*"\)>', '<Assembly: AssemblyFileVersion("{0}")>' -f $numericVersion
    $year = (Get-Date).Year
    $content = $content -replace '(<Assembly: AssemblyCopyright.*) \d{4}("\))', ('$1 {0}$2' -f $year)
    $content = $content.trim()
    # write-host -ForegroundColor darkgray $content
    $content | Out-File $script:assemblyInfoPath -Encoding UTF8
}

Task GenerateAwsSecrets -Depends Init {
    $templatePath = "$script:solutionsPath\AwsSecrets.template.vb"
    $outputPath = "$script:solutionsPath\AwsSecrets.vb"

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
        Write-Host "Generated AwsSecrets.vb from template with CI secrets: $outputPath"
    }
    elseif (Test-Path $outputPath)
    {
        Write-Host "Using existing AwsSecrets.vb (local development): $outputPath"
    }
    else
    {
        throw 'AwsSecrets.vb not found and AWS environment variables not set. Copy AwsSecrets.template.vb to AwsSecrets.vb and fill in your values.'
    }
}

Task RestoreNuGet -Depends Init -PreCondition { -not $script:skipNuGetRestore } {
    $nugetExe = Get-Command nuget.exe -ErrorAction SilentlyContinue
    if (-not $nugetExe) { throw 'NuGet.exe not found in PATH. Please install NuGet or add it to your PATH.' }

    # Restore directly from packages.config to avoid MSBuild version compatibility issues.
    # Newer NuGet.exe generates PackageReference-style temp targets that old MSBuild (v4) cannot parse.
    $packagesConfig = "$script:solutionsPath\packages.config"
    $packagesDir = "$script:solutionsPath\packages"

    & nuget restore $packagesConfig -PackagesDirectory $packagesDir -SolutionDirectory $script:solutionsPath -NonInteractive
    if ($LASTEXITCODE -ne 0) { throw "NuGet restore failed with exit code $LASTEXITCODE" }
}

Task CleanBuildPath -Depends Init {
    if (Test-Path $script:buildOutputPath)
    {
        Write-Host "Cleaning build output path: $script:buildOutputPath"
        #We don't want to delete the folder, just the contents
        Get-ChildItem $script:buildOutputPath | Remove-Item -Recurse -Force
    }
}

Task OutputBuildStructure -Depends RestoreNuGet, CleanBuildPath {
    #Needed this temporarily to look for files.  Disable for now but may be useful in the future for debugging build structure issues.
    Get-ChildItem $script:repoRoot -Recurse | Select-Object -expand fullname
}

Task Compile -Depends UpdateAssemblyInfo, GenerateAwsSecrets, RestoreNuGet, CleanBuildPath, OutputBuildStructure {
    $msbuildExe = $null
    $vsToolsPath = $null

    # Prefer vswhere-discovered MSBuild (VS 2017+) so we get modern targets over the
    # legacy .NET Framework MSBuild that may be first on PATH (v4.0.30319).
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere)
    {
        $vsInstallPath = & $vswhere -latest -requires Microsoft.Component.MSBuild -property installationPath 2>$null | Select-Object -First 1
        $msbuildPath = & $vswhere -latest -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' 2>$null | Select-Object -First 1

        if ($msbuildPath -and (Test-Path $msbuildPath))
        {
            $msbuildExe = $msbuildPath
        }

        # Locate the WebApplication targets shipped with this VS install and pass
        # VSToolsPath explicitly so MSBuild does not fall back to the v11.0 path.
        if ($vsInstallPath)
        {
            $targetsRoot = "$vsInstallPath\MSBuild\Microsoft\VisualStudio"
            $versionDir = Get-ChildItem -Path $targetsRoot -Directory -ErrorAction SilentlyContinue |
                Where-Object { Test-Path ("$($_.FullName)\WebApplications\Microsoft.WebApplication.targets") } |
                Sort-Object Name -Descending | Select-Object -First 1
            if ($versionDir) { $vsToolsPath = $versionDir.FullName }
        }
    }

    # Fall back to whatever msbuild.exe is on PATH (last resort).
    if (-not $msbuildExe)
    {
        $msbuildFromPath = Get-Command msbuild.exe -ErrorAction SilentlyContinue
        if ($msbuildFromPath) { $msbuildExe = $msbuildFromPath.Source }
    }

    if (-not $msbuildExe)
    {
        throw 'MSBuild.exe not found. Please ensure Visual Studio Build Tools or Visual Studio is installed.'
    }

    $msbuildArgs = @(
        $script:projectFilePath
        "/p:Configuration=$script:build"
        '/m'
        '/verbosity:minimal'
        '/p:WarningLevel=0'
    )
    if ($vsToolsPath)
    {
        $msbuildArgs += "/p:VSToolsPath=$vsToolsPath"
        Write-Host "Using VSToolsPath: $vsToolsPath"
    }

    #the output folder is defined in the project file
    Write-Host "Using MSBuild: $msbuildExe"
    & $msbuildExe @msbuildArgs

    if ($LASTEXITCODE -ne 0)
    {
        throw "MSBuild failed with exit code $LASTEXITCODE"
    }
}

Task CopyBuildFiles -Depends Compile {
    function Copy-SiteFile
    {
        param(
            [string]$Source,
            [string]$Destination
        )
        $destDir = Split-Path $Destination -Parent
        if (-not (Test-Path $destDir)) { New-Item -Path $destDir -ItemType Directory -Force | Out-Null }
        if (-not (Test-Path $Source)) { throw "Missing required file: $Source" }
        Copy-Item -Path $Source -Destination $Destination -Force
    }

    $src = $script:solutionsPath
    $binPath = $script:buildOutputPath
    $outSite = "$script:outputPath\site"

    Write-Host 'Package Paths:'
    Write-Host "  Source:       $src"
    Write-Host "  Build Bin:   $binPath"
    Write-Host "  Output:      $script:outputPath"
    Write-Host "  Site:        $outSite"

    if (Test-Path $script:outputPath)
    {
        Write-Host "Removing output directory contents: $script:outputPath"
        Get-ChildItem $script:outputPath | ForEach-Object { Remove-Item -Path $_.FullName -Recurse -Force }
    }

    @($script:outputPath, $outSite) | ForEach-Object {
        if (-not (Test-Path $_)) { New-Item -Path $_ -ItemType Directory -Force | Out-Null }
    }

    # -- bin\ : compiled assemblies and XML doc only --
    Write-Host 'Copying bin\ assemblies...'
    New-Item -Path "$outSite\bin" -ItemType Directory -Force | Out-Null
    Get-ChildItem -Path $binPath -File | Where-Object {
        $_.Extension -eq '.dll' -or $_.Name -eq 'WebJEA.xml'
    } | ForEach-Object {
        Copy-Item -Path $_.FullName -Destination "$outSite\bin\$($_.Name)" -Force
        Write-Host "  bin\$($_.Name)"
    }

    # ── Site files from source tree ──
    Write-Host 'Copying site files...'
    $siteFiles = @(
        '*.aspx'
        'resources\*'
        'Global.asax'
        'NLog.config'
        'Web.config'
    )

    foreach ($relPath in $siteFiles)
    {
        if ($relPath -match '\*')
        {
            $parentRel = Split-Path $relPath -Parent
            $filter = Split-Path $relPath -Leaf
            $sourceDir = if ($parentRel) { Join-Path $src $parentRel } else { $src }
            $files = @(Get-ChildItem -Path $sourceDir -Filter $filter -File -ErrorAction SilentlyContinue)
            if ($files.Count -eq 0) { throw "No files matched pattern: $relPath" }
            foreach ($file in $files)
            {
                $fileRel = $file.FullName.Substring($src.Length).TrimStart('\')
                Copy-SiteFile -Source $file.FullName -Destination "$outSite\$fileRel"
                Write-Host "  $fileRel"
            }
        }
        else
        {
            $sourceFull = "$src\$relPath"
            if (-not (Test-Path $sourceFull)) { throw "Missing required file: $sourceFull" }
            Copy-SiteFile -Source $sourceFull -Destination "$outSite\$relPath"
            Write-Host "  $relPath"
        }
    }

    $script:buildResult = @{
        Version       = $script:version
        OutputPath    = $script:outputPath
        buildInfoPath = $script:buildInfoPath
    }
}

Task CopyPackageFiles -Depends CopyBuildFiles {
    $packagesDir = "$script:solutionsPath\packages"
    $outSite = "$script:outputPath\site"

    # Package name → relative paths from the package content root.
    # Wildcard patterns are resolved via Get-ChildItem; slim builds are excluded.
    $packageFiles = [ordered]@{
        'bootstrap'               = @(
            @{ Src = 'content\Scripts\bootstrap.bundle.min.js'; Dest = 'Scripts' }
             @{ Src = 'content\Content\bootstrap.min.css'; Dest = 'Content' }
        )
        'jQuery'                  = @(
            @{ Src = 'Content\Scripts\jquery-*.js'; Dest = 'Scripts' }
        )
        'jQuery.UI.Combined'      = @(
            @{ Src = 'Content\Scripts\jquery-ui-*.min.js'; Dest = 'Scripts' }
             @{ Src = 'Content\Content\themes\base\jquery-ui.min.css'; Dest = 'Content\themes\base' }
             @{ Src = 'Content\Content\themes\base\images\*.png'; Dest = 'Content\themes\base\images' }
        )
        'jQuery-Timepicker-Addon' = @(
            @{ Src = 'content\Content\jquery-ui-timepicker-addon.min.css'; Dest = 'Content' }
            @{ Src = 'content\Scripts\jquery-ui-sliderAccess.js'; Dest = 'Scripts' }
            @{ Src = 'content\Scripts\jquery-ui-timepicker-addon.min.js'; Dest = 'Scripts' }
        )
        'popper.js'               = @(
            @{ Src = 'content\Scripts\popper.min.js'; Dest = 'Scripts' }
             @{ Src = 'content\Scripts\popper-utils.min.js'; Dest = 'Scripts' }
        )
    }

    foreach ($packageName in $packageFiles.Keys)
    {
        # Resolve the versioned package folder (e.g. jQuery.3.7.1)
        $packageDir = Get-ChildItem -Path $packagesDir -Directory |
            Where-Object { $_.Name -match "^$([regex]::Escape($packageName))\.\d" } |
            Sort-Object Name -Descending | Select-Object -First 1

        if (-not $packageDir)
        {
            throw "Package folder not found for '$packageName' in $packagesDir"
        }

        $contentRoot = $packageDir
        Write-Host "Package: $packageName ($($packageDir.Name))"

        foreach ($entry in $packageFiles[$packageName])
        {
            # Support plain string (src path == dest path) or @{ Src = '...'; Dest = '...' }
            # to handle packages where the NuGet content root has extra nesting.
            if ($entry -is [hashtable])
            {
                $srcRel  = $entry.Src
                $destRel = $entry.Dest
            }
            else
            {
                $srcRel  = $entry
                $destRel = $null
            }

            $sourcePath = Join-Path -Path $contentRoot.FullName -ChildPath $srcRel

            if ($srcRel -match '\*')
            {
                $parentDir = Split-Path -Path $sourcePath -Parent
                $filter    = Split-Path -Path $sourcePath -Leaf
                Write-Host "  [wildcard] searching '$parentDir' for '$filter'"
                $files = @(Get-ChildItem -Path $parentDir -Filter $filter -File -ErrorAction SilentlyContinue |
                    Where-Object { $_.Name -notmatch 'slim' })
                if ($files.Count -eq 0)
                {
                    $destLabel = if ($destRel) { $destRel } else { '(mirrored from contentRoot)' }
                    throw "No files matched pattern '$srcRel' in $($packageDir.Name)`n  Searched : $parentDir`n  Filter   : $filter`n  Dest     : $outSite\$destLabel"
                }
                foreach ($file in $files)
                {
                    $fileRelPath = if ($destRel) {
                        "$destRel\$($file.Name)"
                    } else {
                        $file.FullName.Substring($contentRoot.FullName.Length).TrimStart('\')
                    }
                    $destFile = Join-Path -Path $outSite -ChildPath $fileRelPath
                    $destDir  = Split-Path -Path $destFile -Parent
                    if (-not (Test-Path $destDir)) { New-Item -Path $destDir -ItemType Directory -Force | Out-Null }
                    Copy-Item -Path $file.FullName -Destination $destFile -Force
                    Write-Host "  $fileRelPath"
                }
            }
            else
            {
                if (-not (Test-Path $sourcePath)) { throw "Missing: $sourcePath" }
                $fileRelPath = if ($destRel) {
                    "$destRel\$(Split-Path $sourcePath -Leaf)"
                } else {
                    $srcRel
                }
                $destFile = Join-Path -Path $outSite -ChildPath $fileRelPath
                $destDir  = Split-Path -Path $destFile -Parent
                if (-not (Test-Path $destDir)) { New-Item -Path $destDir -ItemType Directory -Force | Out-Null }
                Copy-Item -Path $sourcePath -Destination $destFile -Force
                Write-Host "  $fileRelPath"
            }
        }
    }

    $fileCount = (Get-ChildItem -Path $outSite -File -Recurse).Count
    Write-Host "Package complete: $fileCount files assembled at $outSite" -ForegroundColor Green
}

Task UpdateWebConfig -Depends CopyBuildFiles {
    $packagesDir = "$script:solutionsPath\packages"
    $webConfigPath = "$script:outputPath\site\Web.config"

    if (-not (Test-Path $webConfigPath))
    {
        throw "Web.config not found in output at: $webConfigPath"
    }

    $jQueryDir = Get-ChildItem -Path $packagesDir -Directory |
        Where-Object { $_.Name -match '^jQuery\.\d' } |
        Sort-Object Name -Descending | Select-Object -First 1
    $jQueryUIDir = Get-ChildItem -Path $packagesDir -Directory |
        Where-Object { $_.Name -match '^jQuery\.UI\.Combined\.\d' } |
        Sort-Object Name -Descending | Select-Object -First 1

    if (-not $jQueryDir) { throw "jQuery package folder not found in $packagesDir" }
    if (-not $jQueryUIDir) { throw "jQuery.UI.Combined package folder not found in $packagesDir" }

    $jQueryVersion = $jQueryDir.Name -replace '^jQuery\.', ''
    $jQueryUIVersion = $jQueryUIDir.Name -replace '^jQuery\.UI\.Combined\.', ''

    $xml = [xml](Get-Content $webConfigPath -Raw -Encoding UTF8)
    $appSettings = $xml.configuration.appSettings.add

    ($appSettings | Where-Object { $_.key -eq 'jQueryVersion' }).value = $jQueryVersion
    ($appSettings | Where-Object { $_.key -eq 'jQueryUIVersion' }).value = $jQueryUIVersion

    $xml.Save($webConfigPath)
    Write-Host "Updated Web.config: jQueryVersion=$jQueryVersion, jQueryUIVersion=$jQueryUIVersion"
}

Task CopyReleaseFiles -Depends CopyBuildFiles {

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
}

Task PackageComplete -Depends CopyBuildFiles, CopyPackageFiles, CopyReleaseFiles, UpdateWebConfig { }

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

Task RevertAssemblyInfo -Depends PackageComplete {
    if (-not (Test-Path $script:assemblyInfoPath))
    {
        Write-Warning "AssemblyInfo not found at $script:assemblyInfoPath - skipping revert."
        return
    }
    $content = Get-Content $script:assemblyInfoPath -Raw -Encoding UTF8
    $content = $content -replace '<Assembly: AssemblyVersion\(".*"\)>', '<Assembly: AssemblyVersion("1.0.0.0")>'
    $content = $content -replace '<Assembly: AssemblyFileVersion\(".*"\)>', '<Assembly: AssemblyFileVersion("1.0.0.0")>'
    $content = $content.Trim()
    $content | Out-File $script:assemblyInfoPath -Encoding UTF8
    Write-Host 'Reverted AssemblyVersion and AssemblyFileVersion to 1.0.0.0'
}

Task SaveBuildInfo -Depends RevertAssemblyInfo, CreateZip {
    $script:buildInfoPath = "$($script:buildResult.OutputPath)\build-info.json"
    $script:buildResult | ConvertTo-Json | Out-File -FilePath $script:buildInfoPath -Encoding utf8 -Force
    Write-Host "buildResult:$script:buildResult"
    Write-Host "Build info saved: $script:buildInfoPath" -ForegroundColor Green

    if ($script:buildResult.ZipPath)
    {
        Write-Host ("Archive: $($script:buildResult.ZipPath)")
    }
}

Task Summary -Depends SaveBuildInfo {
    #empty, just here to to be used by the default task
}
