<#
.SYNOPSIS
    Generates a Mermaid flowchart from integration.psake.ps1 task dependencies.

.DESCRIPTION
    Parses all Task definitions in integration.psake.ps1 and the wrapper invocation
    scripts (TestDeploy.ps1, Invoke-IntegrationTests.ps1)
    to produce a Mermaid-formatted flowchart saved as integration.psake.md.

    The diagram shows:
      - Every psake task and its dependency edges
      - Which caller script invokes which entry task
      - Tasks carrying a -PreCondition guard (may be skipped at runtime)
      - Visual grouping by workflow: Deploy, Snapshot, Integration Tests, Shared

.PARAMETER PsakePath
    Path to integration.psake.ps1. Defaults to the file in the script directory.

.PARAMETER OutputPath
    Path for the generated .md file. Defaults to integration.psake.md in the script directory.

.EXAMPLE
    .\Export-PsakeDiagram.ps1

.EXAMPLE
    .\Export-PsakeDiagram.ps1 -Verbose

.EXAMPLE
    .\Export-PsakeDiagram.ps1 -OutputPath .\docs\tasks.md

.NOTES
    Requires PowerShell 5.1.
    Output requires a Mermaid-compatible renderer (GitHub, VS Code Markdown Preview
    Mermaid Support extension, etc.).
#>
#Requires -Version 5.1

[CmdletBinding()]
param(
    [Parameter()]
    [ValidateScript({ Test-Path $_ -PathType Leaf })]
    [string]$PsakePath = (Join-Path $PSScriptRoot 'integration.psake.ps1'),

    [Parameter()]
    [string]$OutputPath = (Join-Path $PSScriptRoot 'integration.psake.md')
)

begin {
    $script:PSDefaultParameterValues = @{ 'Write-Log.ps1:Stream' = 'Verbose' }

    # Parse all Task definitions from psake file content.
    # Returns an ordered hashtable keyed by task name.
    function ParseTasks {
        param([string]$Content)

        $tasks = [ordered]@{}
        $matchList = [regex]::Matches($Content, '(?m)^Task\s+(\w+)([^\r\n]*)')

        foreach ($m in $matchList) {
            $name       = $m.Groups[1].Value.Trim()
            $headerRest = $m.Groups[2].Value

            $deps = @()
            $depsMatch = [regex]::Match($headerRest, '-Depends\s+([\w\s,]+?)(?=\s+-\w|\s*\{|\s*$)')
            if ($depsMatch.Success) {
                $deps = $depsMatch.Groups[1].Value -split ',' |
                    ForEach-Object { $_.Trim() } |
                    Where-Object { $_ }
            }

            $tasks[$name] = @{
                Name       = $name
                Depends    = $deps
                HasPreCond = $headerRest -match '-PreCondition'
            }
        }

        return $tasks
    }

    # Scan wrapper scripts for their psake taskList entry point.
    # Returns a list of @{ Script; EntryTask } hashtables in declaration order.
    function ParseCallerScripts {
        param([string]$ScriptDir)

        $callers     = [System.Collections.Generic.List[hashtable]]::new()
        $callerFiles = 'TestDeploy.ps1', 'Invoke-IntegrationTests.ps1'

        foreach ($fileName in $callerFiles) {
            $filePath = Join-Path $ScriptDir $fileName
            if (-not (Test-Path $filePath -Verbose:$false)) { continue }

            $fileContent = Get-Content -Path $filePath -Raw -Verbose:$false
            if ($fileContent -match "taskList\s*=\s*@\(\s*'([^']+)'") {
                $callers.Add(@{ Script = $fileName; EntryTask = $Matches[1] })
            }
        }

        return $callers
    }

    function GetNodeId       { param([string]$Name)     "T_$Name" }
    function GetCallerNodeId { param([string]$FileName) "S_$($FileName -replace '[.\-]', '_')" }

    $TaskGroups = [ordered]@{
        Deploy    = @{
            Label = 'Deploy Workflow'
            Tasks = @('CleanBuildOutput', 'BuildPackage', 'ResolveBuildPackage',
                      'ResolveSettings', 'StageDeployment', 'RunDeployScript',
                      'DeployToVM', 'Deploy')
        }
        Snapshot  = @{
            Label = 'Snapshot'
            Tasks = @('RevertSnapshot_Web', 'RevertVMs')
        }
        IntegTest = @{
            Label = 'Integration Tests'
            Tasks = @('ConfigureTestOutput', 'GetTestCredentials', 'ConfigureSsl',
                      'RunPesterTests', 'IntegrationTest')
        }
        Shared    = @{
            Label = 'Shared Tasks'
            Tasks = @('Init', 'GetCredential', 'StartVM_DC', 'StartVM_Web',
                      'RestartVM_Web', 'StartVMs', 'StopVM_Web')
        }
    }

    $CallerDescriptions = @{
        'TestDeploy.ps1'              = 'Build (optional), stage, and deploy WebJEA to the test VM'
        'Invoke-IntegrationTests.ps1' = 'Run Pester integration tests against the deployed WebJEA site'
    }
}

process {
    Write-Verbose "Parsing: $PsakePath"
    $psakeContent = Get-Content -Path $PsakePath -Raw -Verbose:$false
    $tasks        = ParseTasks -Content $psakeContent
    $callers      = ParseCallerScripts -ScriptDir (Split-Path $PsakePath)
    Write-Verbose "Found $($tasks.Count) tasks and $($callers.Count) caller scripts"

    $entryTaskNames = @($callers | ForEach-Object { $_.EntryTask })

    $L = [System.Collections.Generic.List[string]]::new()

    # ── Markdown header ───────────────────────────────────────────────────────
    $L.Add('# WebJEA Integration psake Task Diagram')
    $L.Add('')
    $L.Add('> Auto-generated by `Export-PsakeDiagram.ps1` from `integration.psake.ps1`.')
    $L.Add('> Arrows show **dependency order** — the tail node executes before the head node.')
    $L.Add('> Tasks marked `*` carry a `-PreCondition` and may be skipped at runtime.')
    $L.Add('')

    # ── Mermaid diagram ───────────────────────────────────────────────────────
    $L.Add('```mermaid')
    $L.Add('flowchart TD')
    $L.Add('')

    # CSS-like class definitions
    $L.Add('    classDef callerScript fill:#2d2d6b,stroke:#7777cc,color:#fff,font-weight:bold,font-style:italic')
    $L.Add('    classDef entryTask    fill:#145214,stroke:#4caf50,color:#fff,font-weight:bold')
    $L.Add('    classDef conditional  fill:#6b3a00,stroke:#ff9800,color:#ffe0b2')
    $L.Add('')

    # Caller script nodes — rendered as hexagons
    $L.Add('    %% -- Caller Scripts (hexagon = external entry point) --')
    foreach ($caller in $callers) {
        $id = GetCallerNodeId -FileName $caller.Script
        $L.Add("    $id{{""$($caller.Script)""}}")
    }
    $L.Add('')

    # Caller → entry-task invocation edges
    $L.Add('    %% -- Invocation Edges --')
    foreach ($caller in $callers) {
        $src  = GetCallerNodeId -FileName $caller.Script
        $dest = GetNodeId -Name $caller.EntryTask
        $L.Add("    $src -->|invokes| $dest")
    }
    $L.Add('')

    # Task nodes (flat — no subgraphs)
    $L.Add('    %% -- Task Nodes --')
    foreach ($task in $tasks.Values) {
        $nodeId = GetNodeId -Name $task.Name
        $label  = if ($task.HasPreCond) { "$($task.Name)*" } else { $task.Name }

        if ($entryTaskNames -contains $task.Name) {
            $L.Add("    $nodeId([""$label""])")
        } elseif ($task.HasPreCond) {
            $L.Add("    $nodeId[/""$label""/]")
        } else {
            $L.Add("    $nodeId[""$label""]")
        }
    }
    $L.Add('')

    # Dependency edges
    $L.Add('    %% -- Task Dependency Edges --')
    foreach ($task in $tasks.Values) {
        $dest = GetNodeId -Name $task.Name
        foreach ($dep in $task.Depends) {
            $L.Add("    $(GetNodeId -Name $dep) --> $dest")
        }
    }
    $L.Add('')

    # Apply classes
    $L.Add('    %% -- Styles --')
    foreach ($caller in $callers) {
        $L.Add("    class $(GetCallerNodeId -FileName $caller.Script) callerScript")
    }
    foreach ($et in $entryTaskNames) {
        $L.Add("    class $(GetNodeId -Name $et) entryTask")
    }
    foreach ($task in $tasks.Values) {
        if ($task.HasPreCond -and ($entryTaskNames -notcontains $task.Name)) {
            $L.Add("    class $(GetNodeId -Name $task.Name) conditional")
        }
    }

    $L.Add('```')
    $L.Add('')

    # ── Legend ────────────────────────────────────────────────────────────────
    $L.Add('## Legend')
    $L.Add('')
    $L.Add('| Shape / Style | Meaning |')
    $L.Add('|---|---|')
    $L.Add('| Dark-blue hexagon *(italic bold)* | Caller `.ps1` script — external entry point that invokes psake |')
    $L.Add('| Green stadium `([ ])` | Entry task directly targeted by a caller script |')
    $L.Add('| Orange parallelogram `[/ /]` | Task with `-PreCondition`; may be skipped at runtime |')
    $L.Add('| Rectangle `[ ]` | Regular task — always executes when reached in the dependency chain |')
    $L.Add('| `*` suffix | Short marker for a `-PreCondition` guard |')
    $L.Add('| `-->` arrow | Dependency edge: tail node runs before head node |')
    $L.Add('| `invokes` label on edge | Caller script targets this task; psake resolves its full dependency chain |')
    $L.Add('')

    # ── Caller summary ────────────────────────────────────────────────────────
    $L.Add('## Caller Entry Points')
    $L.Add('')
    $L.Add('| Script | psake Entry Task | Description |')
    $L.Add('|---|---|---|')
    foreach ($caller in $callers) {
        $desc = if ($CallerDescriptions.Contains($caller.Script)) { $CallerDescriptions[$caller.Script] } else { '' }
        $L.Add("| ``$($caller.Script)`` | ``$($caller.EntryTask)`` | $desc |")
    }
    $L.Add('')

    # ── Write file ────────────────────────────────────────────────────────────
    $outputContent = $L -join [System.Environment]::NewLine
    Set-Content -Path $OutputPath -Value $outputContent -Encoding UTF8 -Verbose:$false

    Write-Verbose "Written: $OutputPath"
    Write-Output $OutputPath
}

end {}

