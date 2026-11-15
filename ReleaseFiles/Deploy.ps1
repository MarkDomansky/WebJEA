#Requires -RunAsAdministrator
#Requires -Version 5.1
[CmdletBinding(DefaultParameterSetName = 'Install')]
param (
    [Parameter(Mandatory, ParameterSetName = 'Install')]
    [Parameter(Mandatory, ParameterSetName = 'Test')]
    [Parameter(Mandatory, ParameterSetName = 'ReturnSteps')]
    [Parameter(Mandatory, ParameterSetName = 'ReturnSettings')]
    [ValidateScript({ Test-Path -Path $_ -PathType Leaf })]
    [string]$SettingsFile,

    [Parameter(Mandatory, ParameterSetName = 'Test')]
    [switch]$TestOnly,

    [Parameter(Mandatory, ParameterSetName = "ReturnSteps")]
    [switch]$OnlyReturnSteps,

    [Parameter(Mandatory, ParameterSetName = "ReturnSettings")]
    [switch]$OnlyReturnSettings,

    [Parameter()]
    [ValidateSet('Server', 'Service', 'WebJEA', 'Finalize', 'All')]
    [string[]]$OnlySections = 'All',

    [Parameter()]
    [switch]$IgnoreExistingIIS,

    [Parameter()]
    [switch]$SkipSpnCheck,

    [Parameter()]
    [switch]$AllowHttpWithoutRedirect,

    #Passwords are parameters only, never settings-file keys, so they are not stored next
    #to the release. -ServicePassword: for a regular (non-gMSA) ServiceUserName; needed
    #on the first deploy or when the account or its password changes. -CertPassword: for
    #a password-protected PFX at CertPath; later deploys reuse the stored one.
    #-GraphCertPassword: the same, for the Graph client certificate PFX at GraphCertPath.
    [Parameter()]
    [securestring]$ServicePassword,

    [Parameter()]
    [securestring]$CertPassword,

    [Parameter()]
    [securestring]$GraphCertPassword
)
begin
{
    $ErrorActionPreference = 'Stop'

    #Force Windows PowerShell 5.1-style native argument passing when running under
    #pwsh 7.2+, so the '""' empty-argument idiom (sc.exe password= for gMSA accounts)
    #behaves identically on both editions. On 5.1 this is an ordinary unused variable.
    $PSNativeCommandArgumentPassing = 'Legacy'

    #region Config
    function Step_CopySiteContent([string]$Source, [string]$Destination, [string]$ServiceName, [string]$Label)
    {
        #Shared publish-output copy step. A running service keeps its exe/DLLs locked;
        #stop it before robocopy overwrites the files. Finalize starts it again.
        @{
            Description = "$Label copied to $Destination"
            RunAlways   = $true # Always overwrite - no checksum comparison
            SetScript   = {
                if (-not (Test-Path $Destination)) { New-Item -Path $Destination -ItemType Directory -Force | Out-Null }

                $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
                if ($svc -and $svc.Status -eq 'Running')
                {
                    Stop-Service -Name $ServiceName -Force
                    $svc.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
                }

                # /E  - copy subdirectories including empty ones
                # /IS - include same files (force-overwrites even when content matches)
                # /IT - include tweaked files (same timestamp, different size)
                $params = @($Source, $Destination, '/E', '/IS', '/IT', '/NJH', '/NJS', '/NFL', '/NDL')
                Write-Verbose "robocopy.exe $($params -join ' ')"
                & robocopy.exe $params
                if ($LASTEXITCODE -ge 8) { throw "robocopy failed with exit code $LASTEXITCODE" }
            }.GetNewClosure()
        }
    }

    function Step_AppSettingsProperty([string]$SitePath, [string]$Section, [string]$Name, $Value)
    {
        #Writes one property into appsettings.Production.json in the site folder. The
        #file is not part of the shipped site contents, so it survives in-place
        #upgrades (the shipped web.config is only the ASP.NET Core Module handoff and
        #is never edited).
        @{
            Description = "appsettings.Production.json: $Section.$Name = $Value"
            TestScript  = {
                $path = "$SitePath\appsettings.Production.json"
                if (-not (Test-Path $path)) { return $false }
                try { $json = Get-Content -Path $path -Raw | ConvertFrom-Json }
                catch { return $false }
                $sectionObj = $json.$Section
                if ($null -eq $sectionObj) { return $false }
                return ($sectionObj.$Name -eq $Value)
            }.GetNewClosure()
            SetScript   = {
                $path = "$SitePath\appsettings.Production.json"
                $json = if (Test-Path $path) { Get-Content -Path $path -Raw | ConvertFrom-Json }
                else { [pscustomobject]@{} }
                if (-not $json.PSObject.Properties[$Section])
                {
                    $json | Add-Member -NotePropertyName $Section -NotePropertyValue ([pscustomobject]@{})
                }
                if (-not $json.$Section.PSObject.Properties[$Name])
                {
                    $json.$Section | Add-Member -NotePropertyName $Name -NotePropertyValue $null
                }
                $json.$Section.$Name = $Value
                $json | ConvertTo-Json -Depth 10 | Set-Content -Path $path -Encoding UTF8
            }.GetNewClosure()
        }
    }

    function Step_AppSettingsValue([string]$SitePath, [string[]]$KeyPath, $Value, [string]$Label)
    {
        #Sets one value at any depth in appsettings.Production.json (e.g. AllowedHosts, or
        #the whole Kestrel.Endpoints object), creating parent objects as needed and leaving
        #sibling keys alone - an admin's Kestrel.Limits survives an Endpoints update. Current
        #and desired values are compared as compressed JSON so an object is checked as a
        #whole; callers pass [ordered] hashtables so the key order matches what is read back.
        $desired = ConvertTo-Json -InputObject $Value -Depth 10 -Compress
        @{
            Description = "appsettings.Production.json: $Label"
            TestScript  = {
                $path = "$SitePath\appsettings.Production.json"
                if (-not (Test-Path $path)) { return $false }
                try { $node = Get-Content -Path $path -Raw | ConvertFrom-Json } catch { return $false }
                foreach ($key in $KeyPath)
                {
                    if ($null -eq $node -or -not $node.PSObject.Properties[$key]) { return $false }
                    $node = $node.$key
                }
                return ((ConvertTo-Json -InputObject $node -Depth 10 -Compress) -eq $desired)
            }.GetNewClosure()
            SetScript   = {
                $path = "$SitePath\appsettings.Production.json"
                $json = if (Test-Path $path) { Get-Content -Path $path -Raw | ConvertFrom-Json }
                else { [pscustomobject]@{} }
                $parent = $json
                for ($i = 0; $i -lt $KeyPath.Count - 1; $i++)
                {
                    $key = $KeyPath[$i]
                    if (-not ($parent.PSObject.Properties[$key] -and $parent.$key -is [pscustomobject]))
                    {
                        $parent | Add-Member -NotePropertyName $key -NotePropertyValue ([pscustomobject]@{}) -Force
                    }
                    $parent = $parent.$key
                }
                #Stored through a JSON round-trip so it has the same shape (pscustomobject,
                #not hashtable) the TestScript reads back. An array value (ClientCredentials)
                #is re-collected with @(): pwsh 7 enumerates a one-element array out of
                #ConvertFrom-Json (it would be written as a bare object), and 5.1 hands back a
                #wrapped array that ConvertTo-Json writes as {"value":[...],"Count":1}.
                $stored = $desired | ConvertFrom-Json
                if ($Value -is [array]) { $stored = @($stored) }
                $parent | Add-Member -NotePropertyName $KeyPath[-1] -NotePropertyValue $stored -Force
                $json | ConvertTo-Json -Depth 10 | Set-Content -Path $path -Encoding UTF8
            }.GetNewClosure()
        }
    }

    function Step_XmlAttribute([string]$Path, [string]$XPath, [string]$Name, [string]$Value, [string]$Label)
    {
        #Sets one attribute on one element. NLog.config declares a DEFAULT namespace
        #(xmlns="http://www.nlog-project.org/schemas/NLog.xsd"), so an ordinary
        #/nlog/targets/... XPath matches nothing - every unprefixed step in the path would
        #have to be bound to that URI through an XmlNamespaceManager. Callers pass
        #local-name() predicates instead, which match with or without the namespace and
        #keep working if a future NLog schema changes the URI.
        #PreserveWhitespace keeps the indentation and every comment in the body of the
        #file. It does NOT preserve whitespace INSIDE a tag, so the first write reflows
        #the root <nlog> element's attributes onto one line and normalises '<x/>' to
        #'<x />' - cosmetic, one-time, and the same in both editions.
        @{
            Description = $Label
            TestScript  = {
                if (-not (Test-Path $Path)) { return $false }
                $xml = New-Object System.Xml.XmlDocument
                $xml.PreserveWhitespace = $true
                $xml.Load($Path)
                $node = $xml.SelectSingleNode($XPath)
                return ($null -ne $node -and $node.GetAttribute($Name) -eq $Value)
            }.GetNewClosure()
            SetScript   = {
                $xml = New-Object System.Xml.XmlDocument
                $xml.PreserveWhitespace = $true
                $xml.Load($Path)
                $node = $xml.SelectSingleNode($XPath)
                if ($null -eq $node)
                {
                    throw "No element matched '$XPath' in $Path, so $Name could not be set. The file is not the one shipped with this release."
                }
                $node.SetAttribute($Name, $Value)
                #Save through an explicit writer: XmlDocument.Save(path) would add a UTF-8
                #BOM the shipped file does not have. Indent is off because
                #PreserveWhitespace already carries the original layout.
                $xmlSettings = New-Object System.Xml.XmlWriterSettings
                $xmlSettings.Encoding = New-Object System.Text.UTF8Encoding $false
                $xmlSettings.Indent = $false
                $writer = [System.Xml.XmlWriter]::Create($Path, $xmlSettings)
                try { $xml.Save($writer) } finally { $writer.Dispose() }
            }.GetNewClosure()
        }
    }

    function Step_JsonStringValue([string]$Path, [string]$Key, [string]$Value, [string]$Label)
    {
        #Sets one top-level string key in a JSON file, editing the TEXT rather than
        #round-tripping the object. webjea.json is the admin's file - they add commands,
        #comments and their own formatting to it - and ConvertFrom-Json | ConvertTo-Json
        #would reformat the whole thing on every change, and silently flatten anything
        #nested deeper than -Depth into literal "System.Object[]" strings. A targeted
        #replacement of the one value leaves every other byte (BOM included) alone.
        #The key is matched case-insensitively on purpose: files from older releases spell it
        #'basepath' while this script asks for 'BasePath', and JSON itself is
        #case-sensitive - matching exactly would append a SECOND key rather than update
        #the existing one.
        $pattern = '(?i)("' + [regex]::Escape($Key) + '"\s*:\s*)"(?:[^"\\]|\\.)*"'
        @{
            Description = $Label
            TestScript  = {
                if (-not (Test-Path $Path)) { return $false }
                try { $json = Get-Content -Path $Path -Raw | ConvertFrom-Json } catch { return $false }
                #Read through the parsed object so the comparison sees the unescaped value.
                $property = $json.PSObject.Properties | Where-Object { $_.Name -eq $Key } | Select-Object -First 1
                return ($null -ne $property -and $property.Value -eq $Value)
            }.GetNewClosure()
            SetScript   = {
                $raw = Get-Content -Path $Path -Raw
                #JSON string escaping: backslashes first, then quotes. Windows paths are
                #full of the former ("c:\scripts" must reach the file as "c:\\scripts").
                $encoded = $Value.Replace('\', '\\').Replace('"', '\"')
                if ($raw -match $pattern)
                {
                    $updated = [regex]::Replace($raw, $pattern, { param($m) $m.Groups[1].Value + '"' + $encoded + '"' })
                }
                else
                {
                    #Key absent (an admin removed it). Insert it as the first member rather
                    #than rebuild the document.
                    $brace = $raw.IndexOf('{')
                    if ($brace -lt 0) { throw "$Path does not contain a JSON object, so $Key could not be set." }
                    $updated = $raw.Insert($brace + 1, [Environment]::NewLine + '    "' + $Key + '": "' + $encoded + '",')
                }
                #WriteAllText, not Set-Content: -Encoding UTF8 writes a BOM on Windows
                #PowerShell 5.1 and none on PowerShell 7, so the file would churn between
                #editions. Both editions write the same bytes this way.
                [System.IO.File]::WriteAllText($Path, $updated, (New-Object System.Text.UTF8Encoding $false))
            }.GetNewClosure()
        }
    }

    function Step_UserRight([string]$Constant, [string]$Account, [string]$Sid, [string]$Label)
    {
        #LSA account rights have no cmdlet in either PowerShell edition; secedit.exe is the
        #in-box tool for them. Export the current USER_RIGHTS area, add this SID to the
        #right's line, and configure the result back. Everything else in the exported area
        #is re-applied exactly as it was read, so the write is limited to this one addition.
        #Compared by SID because that is the form secedit exports, and it survives the
        #account being renamed.
        @{
            Description = "$Account has the $Label right ($Constant)"
            TestScript  = {
                $export = Join-Path $env:TEMP "webjea-secedit-export-$PID.inf"
                try
                {
                    #secedit writes the .inf as UTF-16; Get-Content follows its BOM.
                    & secedit.exe /export /areas USER_RIGHTS /cfg $export /quiet | Out-Null
                    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $export))
                    {
                        throw "secedit.exe /export failed with exit code $LASTEXITCODE, so the $Constant right could not be checked."
                    }
                    $line = @(Get-Content -Path $export | Where-Object { $_ -match "^\s*$Constant\s*=" })
                    #Anchor on the delimiters so *S-1-5-21-...-1001 doesn't satisfy a test
                    #for *S-1-5-21-...-100.
                    return [bool]($line -match ('(?:=|,)\s*\*' + [regex]::Escape($Sid) + '\s*(?:,|$)'))
                }
                finally { Remove-Item -Path $export -Force -ErrorAction SilentlyContinue }
            }.GetNewClosure()
            SetScript   = {
                $export = Join-Path $env:TEMP "webjea-secedit-export-$PID.inf"
                $import = Join-Path $env:TEMP "webjea-secedit-import-$PID.inf"
                $db = Join-Path $env:TEMP "webjea-secedit-$PID.sdb"
                try
                {
                    & secedit.exe /export /areas USER_RIGHTS /cfg $export /quiet | Out-Null
                    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $export))
                    {
                        throw "secedit.exe /export failed with exit code $LASTEXITCODE, so the $Constant right could not be granted."
                    }

                    $lines = @(Get-Content -Path $export)
                    $index = -1
                    for ($i = 0; $i -lt $lines.Count; $i++)
                    {
                        if ($lines[$i] -match "^\s*$Constant\s*=") { $index = $i; break }
                    }
                    if ($index -ge 0)
                    {
                        #Rebuild the list rather than appending ",*$Sid" to the raw text: a
                        #line with an empty or trailing-comma value would otherwise produce
                        #"SeServiceLogonRight =,*S-1-..." and secedit would reject the file.
                        $current = @(($lines[$index] -split '=', 2)[1] -split ',' |
                                ForEach-Object { $_.Trim() } | Where-Object { $_ })
                        $lines[$index] = "$Constant = " + (@($current) + @("*$Sid") -join ',')
                    }
                    else
                    {
                        #Nobody currently holds the right, so the export has no line for it.
                        #Add one under [Privilege Rights], which the USER_RIGHTS area always
                        #emits.
                        $section = -1
                        for ($i = 0; $i -lt $lines.Count; $i++)
                        {
                            if ($lines[$i] -match '^\s*\[Privilege Rights\]\s*$') { $section = $i; break }
                        }
                        if ($section -lt 0) { throw "secedit.exe export from $export has no [Privilege Rights] section; cannot grant $Constant." }
                        #$lines[($section+1)..($lines.Count-1)] would COUNT DOWN and silently
                        #duplicate the tail if the section header were the last line, so the
                        #empty case is spelled out instead of relying on the range.
                        $tail = if ($section -lt $lines.Count - 1) { @($lines[($section + 1)..($lines.Count - 1)]) } else { @() }
                        $lines = @($lines[0..$section]) + @("$Constant = *$Sid") + $tail
                    }

                    #secedit only reads UTF-16 .inf files; -Encoding Unicode is UTF-16LE on
                    #both editions.
                    Set-Content -Path $import -Value $lines -Encoding Unicode
                    & secedit.exe /configure /db $db /cfg $import /areas USER_RIGHTS /quiet | Out-Null
                    if ($LASTEXITCODE -ne 0)
                    {
                        throw "secedit.exe /configure failed with exit code $LASTEXITCODE while granting $Constant to $Account."
                    }
                }
                finally
                {
                    Remove-Item -Path $export, $import, $db -Force -ErrorAction SilentlyContinue
                }
            }.GetNewClosure()
        }
    }

    function Step_FolderAccess([string]$Path, [string]$Account, [string]$Sid,
        [ValidateSet('Read', 'ReadAndExecute', 'Modify')][string]$Rights, [string]$Label)
    {
        #Makes sure the service account can use a folder, and changes nothing else: the
        #admin's permission scheme (inheritance, other entries) is theirs. Access the
        #account already has through a built-in group that always contains it counts, so
        #a default install - where Users can read most of C:\ - gets no extra entry.
        #Only Allow entries that cover the folder AND everything below it count (container
        #and object inherit, no propagation limits), because Kestrel/NLog need the files,
        #not just the folder. Compared by SID, like the private-key step.
        $holderSids = @($Sid,
            'S-1-1-0',      #Everyone
            'S-1-5-11',     #Authenticated Users
            'S-1-5-32-545', #Users (contains Authenticated Users by default)
            'S-1-5-6')      #Service (in every service process token)
        $needed = [int][System.Security.AccessControl.FileSystemRights]$Rights
        $icaclsRights = @{ Read = 'R'; ReadAndExecute = 'RX'; Modify = 'M' }[$Rights]
        $inherit = [System.Security.AccessControl.InheritanceFlags]'ContainerInherit, ObjectInherit'
        $hasAccess = {
            if (-not (Test-Path -LiteralPath $Path -PathType Container)) { return $false }
            $rules = (Get-Acl -LiteralPath $Path).GetAccessRules($true, $true, [System.Security.Principal.SecurityIdentifier])
            return [bool]($rules | Where-Object {
                    $_.AccessControlType -eq 'Allow' -and
                    $holderSids -contains $_.IdentityReference.Value -and
                    ($_.InheritanceFlags -band $inherit) -eq $inherit -and
                    $_.PropagationFlags -eq 'None' -and
                    ([int]$_.FileSystemRights -band $needed) -eq $needed
                })
        }.GetNewClosure()
        @{
            Description = "$Account has $Label access to $Path"
            TestScript  = { & $hasAccess }.GetNewClosure()
            SetScript   = {
                #A folder created here (LogPath on a first install) may already be covered
                #by what it inherits; only add an entry when it is not.
                if (-not (Test-Path -LiteralPath $Path)) { New-Item -Path $Path -ItemType Directory -Force | Out-Null }
                if (& $hasAccess) { return }
                #icacls /grant adds one entry and leaves inheritance and every other entry
                #exactly as they were. Set-Acl would rewrite the whole descriptor, and on
                #folders it can demand SeSecurityPrivilege.
                & icacls.exe $Path /grant "*${Sid}:(OI)(CI)($icaclsRights)" | Out-Null
                if ($LASTEXITCODE -ne 0) { throw "icacls.exe failed with exit code $LASTEXITCODE granting $Account $Label access to $Path." }
            }.GetNewClosure()
        }
    }

    function Step_CertKeyAccess([System.Security.Cryptography.X509Certificates.X509Certificate2]$Certificate, [string]$Account, [string]$Failure)
    {
        #Gives the service account Read on the private key file behind one certificate in
        #LocalMachine\My. The store is readable by everyone, but the key file is ACL'd to
        #SYSTEM and local Administrators only, and renewing a certificate produces a NEW key
        #file with those defaults again - so this is checked on every deploy. Captured by
        #thumbprint. $Failure says what breaks without it (the HTTPS listener, Graph calls).
        $thumbprint = $Certificate.Thumbprint
        $certLabel = "$thumbprint ('$($Certificate.Subject)', expires $($Certificate.NotAfter.ToString('yyyy-MM-dd')))"

        #GetNewClosure() rebinds these scriptblocks to a new dynamic module whose scope
        #chain reaches the global scope but NOT this script's, so the two helpers below
        #cannot be called by name from inside them - the closure dies at run time with
        #CommandNotFoundException. Capture each function's scriptblock in a variable
        #(closures do copy those) and invoke it with &. Same class of GetNewClosure
        #scoping trap as $skipSpn in the Service section.
        $getKeyPath = ${function:GetCertPrivateKeyPath}
        $getAccountSid = ${function:ResolveAccountSid}
        @{
            Description = "$Account can read the private key of certificate $certLabel"
            TestScript  = {
                $cert = Get-Item -Path "Cert:\LocalMachine\My\$thumbprint" -ErrorAction SilentlyContinue
                if (-not $cert) { return $false }

                $keyPath = & $getKeyPath -Cert $cert
                #No file to ACL - a hardware/smart-card/ephemeral provider, or a key
                #kept outside the standard machine key folders. Report the step as
                #satisfied rather than block the deploy on something this script cannot
                #fix, but say plainly what it means for the account.
                if (-not $keyPath)
                {
                    Write-Warning "Certificate $thumbprint has no private key file in the standard machine key folders (hardware, smart-card or ephemeral provider). Its permissions cannot be managed here - grant $Account read access with that provider's own tooling, or $Failure unless the account is a local administrator."
                    return $true
                }

                #Read the ACEs as SIDs: the key file's ACL routinely contains entries
                #for accounts that no longer resolve to a name, and translating those
                #for comparison would throw mid-enumeration.
                $sid = & $getAccountSid -Account $Account
                $read = [System.Security.AccessControl.FileSystemRights]::Read
                $rules = (Get-Acl -Path $keyPath).GetAccessRules($true, $true, [System.Security.Principal.SecurityIdentifier])
                return [bool]($rules | Where-Object {
                        $_.IdentityReference.Value -eq $sid.Value -and
                        $_.AccessControlType -eq 'Allow' -and
                        ($_.FileSystemRights -band $read) -eq $read
                    })
            }.GetNewClosure()
            SetScript   = {
                $cert = Get-Item -Path "Cert:\LocalMachine\My\$thumbprint"
                $keyPath = & $getKeyPath -Cert $cert
                $sid = & $getAccountSid -Account $Account

                #Read, not Full Control: the service only ever reads the key, and a
                #writable ACE would let a compromised service account replace it.
                #The rule is written against the SID so it survives the account being
                #renamed and does not depend on name resolution at apply time.
                $acl = Get-Acl -Path $keyPath
                $acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule(
                            $sid, [System.Security.AccessControl.FileSystemRights]::Read, 'Allow')))
                Set-Acl -Path $keyPath -AclObject $acl
            }.GetNewClosure()
        }
    }

    function GetSteps_AppConfig($Settings)
    {
        ##################################################
        #Update the app's configuration files
        ##################################################
        #set json config location in appsettings.Production.json
        Step_AppSettingsProperty -SitePath $settings.SitePath -Section 'WebJEA' -Name 'ConfigFile' `
            -Value "$($settings.ScriptsPath)\webjea.json"

        #Kestrel listeners use Kestrel's native endpoint configuration. A disabled listener
        #([int]$null is 0, so a missing/null port key disables it) is simply absent.
        #'*' listens on every IPv4 and IPv6 address. The certificate is either:
        # - CertSubject: picked by Kestrel at startup from LocalMachine\My (the match that
        #   expires last), so a renewal is used after the next restart. AllowInvalid is
        #   always on: without it Kestrel drops any certificate whose chain or revocation
        #   check fails at that moment - an unreachable CRL would stop HTTPS starting.
        #   ValidateSettings has already warned about anything untrusted.
        # - CertPath: a PFX file Kestrel loads and reloads whenever the file changes, so an
        #   ACME client renewing it in place needs no restart. Password only if it has one.
        #The whole Certificate object is replaced, so nothing from the other mode lingers.
        $endpoints = [ordered]@{}
        if ([int]$settings.HttpPort -gt 0)
        {
            $endpoints.Http = [ordered]@{ Url = "http://*:$([int]$settings.HttpPort)" }
        }
        $certLabel = $null
        if ([int]$settings.HttpsPort -gt 0)
        {
            if (-not [string]::IsNullOrWhiteSpace([string]$settings.CertPath))
            {
                $certificate = [ordered]@{ Path = [string]$settings.CertPath }
                $certLabel = "certificate file '$($settings.CertPath)'"
                if (-not [string]::IsNullOrEmpty($settings.ResolvedCertPassword))
                {
                    $certificate.Password = [string]$settings.ResolvedCertPassword
                    $certLabel += ', password set' #never the password itself
                }
            }
            else
            {
                $certificate = [ordered]@{
                    Subject      = [string]$settings.CertSubject
                    Store        = 'My'
                    Location     = 'LocalMachine'
                    AllowInvalid = $true
                }
                $certLabel = "certificate subject '$($settings.CertSubject)'"
            }
            $endpoints.Https = [ordered]@{
                Url         = "https://*:$([int]$settings.HttpsPort)"
                Certificate = $certificate
            }
        }
        $endpointLabel = @($endpoints.Keys | ForEach-Object { $endpoints[$_].Url }) -join ', '
        if ($certLabel) { $endpointLabel += " ($certLabel)" }
        Step_AppSettingsValue -SitePath $settings.SitePath -KeyPath 'Kestrel', 'Endpoints' -Value $endpoints `
            -Label "Kestrel.Endpoints = $endpointLabel"

        #Graph client certificate (Entra mode), only when the settings file names one: with
        #neither key set, AzureAd is the admin's (a client secret, env vars, hand-written
        #JSON) and is left alone. Microsoft.Identity.Web's own credential list, so:
        # - GraphCertSubject: the currently valid exact-DN match in LocalMachine\My with the
        #   latest NotBefore, looked up when the app starts - a renewal imported with the
        #   same subject is used after the next restart.
        # - GraphCertPath: a PFX read once at startup (no reload on change), with an
        #   ephemeral key, so the folder read access is all the account needs.
        #The whole array is replaced, so nothing from the other mode lingers; TenantId,
        #ClientId and the rest of AzureAd are untouched.
        $hasGraphCertSubject = -not [string]::IsNullOrWhiteSpace([string]$settings.GraphCertSubject)
        $hasGraphCertPath = -not [string]::IsNullOrWhiteSpace([string]$settings.GraphCertPath)
        if ($hasGraphCertSubject -or $hasGraphCertPath)
        {
            if ($hasGraphCertPath)
            {
                $credential = [ordered]@{
                    SourceType          = 'Path'
                    CertificateDiskPath = [string]$settings.GraphCertPath
                }
                $graphLabel = "certificate file '$($settings.GraphCertPath)'"
                if (-not [string]::IsNullOrEmpty($settings.ResolvedGraphCertPassword))
                {
                    $credential.CertificatePassword = [string]$settings.ResolvedGraphCertPassword
                    $graphLabel += ', password set' #never the password itself
                }
            }
            else
            {
                $credential = [ordered]@{
                    SourceType                   = 'StoreWithDistinguishedName'
                    CertificateStorePath         = 'LocalMachine/My'
                    CertificateDistinguishedName = [string]$settings.GraphCertSubject
                }
                $graphLabel = "certificate subject '$($settings.GraphCertSubject)'"
            }
            Step_AppSettingsValue -SitePath $settings.SitePath -KeyPath 'AzureAd', 'ClientCredentials' -Value @($credential) `
                -Label "AzureAd.ClientCredentials = $graphLabel"
        }

        #Earlier alphas configured the listeners through these WebJEA keys. The app now
        #refuses to start while any of them is present, so an upgrade removes them.
        $sitePath = $settings.SitePath
        $legacyKeys = @('HttpPort', 'HttpsPort', 'CertThumbprint')
        @{
            Description = 'appsettings.Production.json: retired WebJEA.HttpPort/HttpsPort/CertThumbprint removed'
            TestScript  = {
                $path = "$sitePath\appsettings.Production.json"
                if (-not (Test-Path $path)) { return $true }
                try { $json = Get-Content -Path $path -Raw | ConvertFrom-Json } catch { return $false }
                if ($null -eq $json.WebJEA) { return $true }
                return -not ($legacyKeys | Where-Object { $json.WebJEA.PSObject.Properties[$_] })
            }.GetNewClosure()
            SetScript   = {
                $path = "$sitePath\appsettings.Production.json"
                $json = Get-Content -Path $path -Raw | ConvertFrom-Json
                foreach ($key in $legacyKeys) { $json.WebJEA.PSObject.Properties.Remove($key) }
                $json | ConvertTo-Json -Depth 10 | Set-Content -Path $path -Encoding UTF8
            }.GetNewClosure()
        }

        #Host filtering replaces IIS host-header bindings: requests for names not in this
        #list get 400. localhost + machine name keep the smoke probe and local
        #troubleshooting working. AllowedHosts is a TOP-LEVEL key, not under WebJEA.
        $allowedHosts = (@($settings.SiteFQDNs) + @($env:COMPUTERNAME, 'localhost') | Select-Object -Unique) -join ';'
        Step_AppSettingsValue -SitePath $settings.SitePath -KeyPath 'AllowedHosts' -Value $allowedHosts `
            -Label "AllowedHosts = $allowedHosts"

        #set nlog log location in nlog.config in the site folder
        Step_XmlAttribute -Path "$($settings.SitePath)\nlog.config" `
            -XPath "//*[local-name()='target'][@name='file']/*[local-name()='target']" `
            -Name 'fileName' -Value "$($settings.LogPath)\$($settings.LogFile)" `
            -Label 'Setting log file location in nlog.config'

        #set nlog usage file location in nlog.config in the site folder
        Step_XmlAttribute -Path "$($settings.SitePath)\nlog.config" `
            -XPath "//*[local-name()='target'][@name='fileSummary']/*[local-name()='target']" `
            -Name 'fileName' -Value "$($settings.LogPath)\$($settings.LogUsageFile)" `
            -Label 'Setting usage log file location in nlog.config'
    }

    function GetSteps_Server
    {
        @{Description = '***** Configuring the Server *****' }

        #add site contents
        Step_CopySiteContent -Source "$($settings.SourcePath)\site" -Destination $settings.SitePath -ServiceName $settings.ServiceName -Label 'Site contents'

        #Seed the starter scripts. Keyed on the config file, not on "ScriptsPath has files":
        #SitePath and LogPath may live inside ScriptsPath (C:\webjea\site), so the site copy
        #above - or a log from an earlier run - would make an empty install look seeded and
        #leave no webjea.json for the WebJEA section to edit. config.json counts too: the
        #rename step below carries it over.
        #/XC /XN /XO copy only files that are missing, so a folder the admin has already
        #started (their own overview.ps1, say) is filled in, never overwritten.
        $scriptSource = $settings.SourcePath + '\Scripts'
        $scriptDest = $settings.ScriptsPath
        @{
            Description = 'Starter scripts copied'
            TestScript  = {
                (Test-Path -Path "$scriptDest\webjea.json" -PathType Leaf) -or
                (Test-Path -Path "$scriptDest\config.json" -PathType Leaf)
            }.GetNewClosure()
            SetScript   = {
                if (-not (Test-Path $scriptDest)) { New-Item -Path $scriptDest -ItemType Directory -Force | Out-Null }
                $params = @($scriptSource, $scriptDest, '/E', '/XC', '/XN', '/XO', '/NJH', '/NJS', '/NFL', '/NDL')
                Write-Verbose "robocopy.exe $($params -join ' ')"
                & robocopy.exe $params
                #robocopy exit codes below 8 are success (0 = nothing to copy, 1 = files
                #copied, 2 = extra files present, ...); 8 and above are real failures.
                if ($LASTEXITCODE -ge 8) { throw "robocopy failed with exit code $LASTEXITCODE while copying starter scripts" }
            }.GetNewClosure()
        }

        #The config file was named config.json before it became webjea.json. An upgrade
        #keeps the admin's existing ScriptsPath, so carry their file over to the new name.
        #Never overwrites: if webjea.json is already there, config.json is left alone.
        $legacyConfig = "$($settings.ScriptsPath)\config.json"
        $currentConfig = "$($settings.ScriptsPath)\webjea.json"
        @{
            Description = 'config.json renamed to webjea.json'
            TestScript  = {
                (Test-Path -Path $currentConfig -PathType Leaf) -or -not (Test-Path -Path $legacyConfig -PathType Leaf)
            }.GetNewClosure()
            SetScript   = {
                Rename-Item -Path $legacyConfig -NewName 'webjea.json'
            }.GetNewClosure()
        }

        #What the service account needs on the folders this script owns: run the site,
        #read the scripts, write the logs (NLog creates, appends and archives). Nothing is
        #ever removed; broad access is reported by ValidateSettings instead. Resolved to a
        #SID here so an unresolvable account fails before anything has been changed.
        $svcAccount = $settings.ServiceUserName
        $svcSid = (ResolveAccountSid -Account $svcAccount).Value
        Step_FolderAccess -Path $settings.SitePath -Account $svcAccount -Sid $svcSid -Rights 'ReadAndExecute' -Label 'read & execute'
        Step_FolderAccess -Path $settings.ScriptsPath -Account $svcAccount -Sid $svcSid -Rights 'ReadAndExecute' -Label 'read & execute'
        Step_FolderAccess -Path $settings.LogPath -Account $svcAccount -Sid $svcSid -Rights 'Modify' -Label 'modify'
    }
    function GetSteps_Service($Settings)
    {
        @{ Description = '***** Configuring the Windows Service *****' }

        $svcName = $settings.ServiceName
        $exePath = Join-Path $settings.SitePath 'WebJEA.exe'
        $account = $settings.ServiceUserName
        $isGmsa = $account -match '\$$'
        #Copied into a local for the closures below (same reason as $skipSpn), and kept a
        #SecureString until sc.exe needs it.
        $servicePasswordSecure = $ServicePassword
        $accountSid = (ResolveAccountSid -Account $account).Value

        #Grant Logon-as-a-Service directly to the service account (was granted to the
        #IIS APPPOOL\ principal in the IIS era). Resolved to a SID here rather than inside
        #the step so an unresolvable account fails before anything has been changed, with
        #ResolveAccountSid's message rather than a bare secedit exit code.
        Step_UserRight -Constant 'SeServiceLogonRight' -Account $account `
            -Sid $accountSid -Label 'Logon as a Service'

        #A PFX certificate is read straight from its folder - which belongs to the ACME
        #client (or the admin), so only the read access is added, nothing else changed.
        #Folder, not file: a renewal replaces the file, and the replacement inherits from
        #the folder. Kestrel reads CertPath; Microsoft.Identity.Web reads GraphCertPath (with
        #an ephemeral key, so no key-folder access is needed). Both in one folder is fine:
        #the second grant finds the first one already there.
        $pfxFolders = @()
        if ([int]$settings.HttpsPort -gt 0 -and -not [string]::IsNullOrWhiteSpace([string]$settings.CertPath))
        {
            $pfxFolders += Split-Path -Path $settings.CertPath -Parent
        }
        if (-not [string]::IsNullOrWhiteSpace([string]$settings.GraphCertPath))
        {
            $pfxFolders += Split-Path -Path $settings.GraphCertPath -Parent
        }
        foreach ($pfxFolder in @($pfxFolders | Select-Object -Unique))
        {
            Step_FolderAccess -Path $pfxFolder -Account $account -Sid $accountSid -Rights 'Read' -Label 'read'
        }

        #A certificate in LocalMachine\My is only usable once the account can read its
        #private key file (Step_CertKeyAccess). That single ACL is the reason a WebJEA
        #service account would otherwise have to be a local administrator.
        #Grant on EVERY certificate that matches (ValidateSettings collected them), not just
        #the one in use today, so a renewal that is already imported works on the next
        #restart:
        # - CertMatches: Kestrel picks the CertSubject match that expires last. Empty with
        #   CertPath.
        # - GraphCertMatches: Microsoft.Identity.Web picks the valid exact-DN match with the
        #   latest NotBefore. Empty unless GraphCertSubject is set.
        #A certificate used for both gets one step.
        $keyThumbprints = @{}
        foreach ($use in @(
                @{ Certs = $settings.CertMatches; Failure = 'the HTTPS listener will fail to start' }
                @{ Certs = $settings.GraphCertMatches; Failure = 'Microsoft Graph calls (group overage, display names, revalidation) will fail' }))
        {
            foreach ($matchedCert in @($use.Certs | Where-Object { $_ }))
            {
                if ($keyThumbprints.ContainsKey($matchedCert.Thumbprint)) { continue }
                $keyThumbprints[$matchedCert.Thumbprint] = $true
                Step_CertKeyAccess -Certificate $matchedCert -Account $account -Failure $use.Failure
            }
        }

        #Create/repair the service. sc.exe (not New-Service) because it handles gMSA
        #accounts (obj= with no password) and lets us fix binPath/start-type on updates.
        #Note sc.exe's space-after-equals syntax is mandatory.
        @{
            Description = "Windows service '$svcName' exists, runs $exePath as $account (delayed auto-start)"
            TestScript  = {
                $svc = Get-CimInstance Win32_Service -Filter "Name='$svcName'" -ErrorAction SilentlyContinue
                if (-not $svc) { return $false }
                if ($svc.PathName.Trim('"') -ne $exePath) { return $false }
                #Win32_Service reports gMSA/user names in DOMAIN\name form; compare case-insensitively
                if ($svc.StartName -ne $account) { return $false }
                return ($svc.StartMode -eq 'Auto')
            }.GetNewClosure()
            SetScript   = {
                $svc = Get-CimInstance Win32_Service -Filter "Name='$svcName'" -ErrorAction SilentlyContinue

                #obj=/password= go together. sc.exe requires password= whenever the account
                #is not LocalSystem, and MUST see it again on `config` whenever the account
                #changes (e.g. password-account -> gMSA on a redeploy) - omitting it there
                #would leave the old credential in place and Finalize's Restart-Service would
                #then fail with a logon error.
                # - gMSA: empty password. Windows PowerShell 5.1 drops a bare empty-string
                #   argument to native commands (shifting the rest of argv), so it is passed
                #   as '""' - the string containing two quote chars reaches sc.exe as an
                #   empty quoted value. The script forces $PSNativeCommandArgumentPassing =
                #   'Legacy' at the top so this idiom behaves the same under pwsh 7.2+.
                # - regular account with -ServicePassword: that password.
                # - regular account without it: ValidateSettings only allows that when the
                #   service already runs as this account, so both are left out and the
                #   stored credential is kept (passing an empty one would break the logon).
                $scArgs = @('binPath=', "`"$exePath`"", 'start=', 'delayed-auto')
                if ($isGmsa)
                {
                    $scArgs += @('obj=', $account, 'password=', '""')
                }
                elseif ($servicePasswordSecure)
                {
                    $scArgs += @('obj=', $account, 'password=', [System.Net.NetworkCredential]::new('', $servicePasswordSecure).Password)
                }

                if (-not $svc)
                {
                    & sc.exe create $svcName @scArgs | Out-Null
                    if ($LASTEXITCODE -ne 0) { throw "sc.exe create failed with exit code $LASTEXITCODE" }
                }
                else
                {
                    & sc.exe config $svcName @scArgs | Out-Null
                    if ($LASTEXITCODE -ne 0) { throw "sc.exe config failed with exit code $LASTEXITCODE" }
                }
                & sc.exe description $svcName 'WebJEA - web front-end for PowerShell scripts' | Out-Null
            }.GetNewClosure()
        }

        #A password passed for an account the service already runs as is a rotation: the
        #step above sees nothing to change (it can't read the stored password), so apply
        #it explicitly. Harmless right after the step above has just set it.
        if (-not $isGmsa -and $servicePasswordSecure)
        {
            @{
                Description = "Service '$svcName' logs on as $account with the password from -ServicePassword"
                RunAlways   = $true
                SetScript   = {
                    $plain = [System.Net.NetworkCredential]::new('', $servicePasswordSecure).Password
                    & sc.exe config $svcName obj= $account password= $plain | Out-Null
                    if ($LASTEXITCODE -ne 0) { throw "sc.exe config failed with exit code $LASTEXITCODE while setting the service password" }
                }.GetNewClosure()
            }
        }

        #Restart on failure: 5s, 10s, 30s; reset the failure counter daily.
        @{
            Description = "Service '$svcName' restarts on failure"
            RunAlways   = $true # sc.exe qfailure output parsing is brittle; setting is idempotent
            SetScript   = {
                & sc.exe failure $svcName reset= 86400 actions= restart/5000/restart/10000/restart/30000 | Out-Null
                if ($LASTEXITCODE -ne 0) { throw "sc.exe failure failed with exit code $LASTEXITCODE" }
            }.GetNewClosure()
        }

        #Firewall: one inbound rule per enabled port.
        foreach ($port in @([int]$settings.HttpPort, [int]$settings.HttpsPort) | Where-Object { $_ -gt 0 })
        {
            @{
                Description = "Firewall allows inbound TCP $port for WebJEA"
                TestScript  = {
                    $null -ne (Get-NetFirewallRule -DisplayName "WebJEA (TCP $port)" -ErrorAction SilentlyContinue)
                }.GetNewClosure()
                SetScript   = {
                    New-NetFirewallRule -DisplayName "WebJEA (TCP $port)" -Direction Inbound -Protocol TCP -LocalPort $port -Action Allow | Out-Null
                }.GetNewClosure()
            }
        }

        #Kerberos: kernel-mode auth is gone with IIS, so the HTTP/<fqdn> SPN must live on
        #the service account or clients silently fall back to NTLM. Verify rather than
        #let that happen. -SkipSpnCheck (or a non-domain account) downgrades to a warning.
        #Captured into a local: $SkipSpnCheck is the script's switch parameter, and
        #GetNewClosure() does not reliably re-resolve it from this nested function's
        #scope - a plain bool captures correctly.
        $skipSpn = $SkipSpnCheck.IsPresent
        $accountIsDomain = $account -notmatch "^(\.|$env:COMPUTERNAME|NT AUTHORITY|NT SERVICE)\\"
        foreach ($fqdn in @($settings.SiteFQDNs))
        {
            $spn = "HTTP/$fqdn"
            $samName = $account.Split('\')[1]
            @{
                Description = "SPN $spn is registered to $account (Kerberos)"
                TestScript  = {
                    if ($skipSpn -or -not $accountIsDomain)
                    {
                        Write-Warning "Skipping SPN check for $spn - Kerberos will not work without it (NTLM fallback only)."
                        return $true
                    }
                    #setspn.exe ships with RSAT and is absent on non-Server SKUs / stripped
                    #Server Core. Running it anyway would die under $ErrorActionPreference =
                    #'Stop' with a raw command-not-found - after the service was already
                    #stopped for the copy step - and give no hint that -SkipSpnCheck exists.
                    if (-not (Get-Command setspn.exe -ErrorAction SilentlyContinue))
                    {
                        Write-Warning "setspn.exe not found; cannot verify SPN $spn. Kerberos may fall back to NTLM. Install RSAT (Active Directory tools), or re-run with -SkipSpnCheck to accept that."
                        return $true
                    }
                    $result = & setspn.exe -L $samName 2>&1
                    return [bool]($result -match [regex]::Escape($spn))
                }.GetNewClosure()
                SetScript   = {
                    #Registering an SPN needs write access to the account object, which the
                    #deploying admin may not have. Try, and fail with the exact command.
                    #Same RSAT-absence guard as TestScript - without it, a missing setspn.exe
                    #would leave $LASTEXITCODE stale from a previous command, so the
                    #"$LASTEXITCODE -ne 0" check below could silently pass instead of throwing.
                    if (-not (Get-Command setspn.exe -ErrorAction SilentlyContinue))
                    {
                        throw "Could not register SPN $spn on ${account}: setspn.exe not found. Install RSAT (Active Directory tools) and re-run, or re-run Deploy.ps1 with -SkipSpnCheck to accept NTLM-only authentication."
                    }
                    & setspn.exe -S $spn $samName | Out-Null
                    if ($LASTEXITCODE -ne 0)
                    {
                        throw "Could not register SPN $spn on $account. Have a domain admin run: setspn -S $spn $samName  (or re-run Deploy.ps1 with -SkipSpnCheck to accept NTLM-only authentication)."
                    }
                }.GetNewClosure()
            }
        }

        #appsettings.Production.json (config location, Kestrel endpoints + certificate,
        #allowed hosts) and nlog paths
        GetSteps_AppConfig -Settings $Settings
    }
    function GetSteps_WebJEA($Settings)
    {
        @{ Description = '***** Configuring WebJEA Specific Settings *****' }
        #Update webjea.json BasePath property
        Step_JsonStringValue -Path "$($settings.ScriptsPath)\webjea.json" `
            -Key 'BasePath' -Value $settings.ScriptsPath `
            -Label 'BasePath in webjea.json is set to the scripts folder'

    }
    function GetSteps_Finalize($Settings)
    {
        @{ Description = '***** Finalizing Deployment *****' }

        #Start (or restart) the service so the freshly copied binaries and config are live.
        @{
            Description = "Service '$($settings.ServiceName)' is running"
            RunAlways   = $true # always (re)start at the end of a real deploy
            SetScript   = {
                Restart-Service -Name $settings.ServiceName -Force
                (Get-Service -Name $settings.ServiceName).WaitForStatus('Running', [TimeSpan]::FromSeconds(30))
            }.GetNewClosure()
        }

        #Smoke-test through the real listeners: resolve each FQDN to 127.0.0.1 (correct
        #Host header + SNI without DNS) and require a response below 500. 401/403 count
        #as alive (auth challenges come before the app logic). Retries absorb first-start
        #warmup.
        @{
            Description = 'Deployed site responds on its configured ports'
            TestScript  = {
                $curl = Join-Path $env:SystemRoot 'System32\curl.exe'
                if (-not (Test-Path $curl))
                {
                    Write-Warning 'curl.exe not found; skipping the deployment smoke probe.'
                    return $true
                }
                $probes = foreach ($fqdn in @($settings.SiteFQDNs))
                {
                    if ([int]$settings.HttpPort -gt 0) { @{ Scheme = 'http'; Port = [int]$settings.HttpPort; Fqdn = $fqdn } }
                    if ([int]$settings.HttpsPort -gt 0) { @{ Scheme = 'https'; Port = [int]$settings.HttpsPort; Fqdn = $fqdn } }
                }
                foreach ($probe in $probes)
                {
                    $url = '{0}://{1}:{2}/' -f $probe.Scheme, $probe.Fqdn, $probe.Port
                    $resolve = '{0}:{1}:127.0.0.1' -f $probe.Fqdn, $probe.Port
                    $status = 0
                    foreach ($attempt in 1..6)
                    {
                        #-k: probe goes to 127.0.0.1, trust-chain checks are not the point
                        #--negotiate -u :  passes Windows auth as the (elevated) deploy user
                        $raw = & $curl -k -s -o NUL -w '%{http_code}' --negotiate -u : --resolve $resolve $url
                        $status = 0
                        [void][int]::TryParse($raw, [ref]$status)
                        if ($status -ge 100 -and $status -lt 500) { break }
                        Start-Sleep -Seconds 5
                    }
                    if ($status -lt 100 -or $status -ge 500)
                    {
                        Write-Warning "Probe of $url returned '$status' - the service did not respond correctly."
                        return $false
                    }
                    Write-Verbose "Probe of $url returned $status"
                }
                return $true
            }.GetNewClosure()
            SetScript   = {
                #remediation: make sure the service is up, give it time, then the
                #framework re-runs the probe above
                Start-Service -Name $settings.ServiceName -ErrorAction SilentlyContinue
                Start-Sleep -Seconds 10
            }.GetNewClosure()
        }
    }

    function GetSteps($Settings, $OnlySections)
    {
        if ($OnlySections -contains 'All' -or $OnlySections -contains 'Server') { GetSteps_Server }
        if ($OnlySections -contains 'All' -or $OnlySections -contains 'Service') { GetSteps_Service $Settings }
        if ($OnlySections -contains 'All' -or $OnlySections -contains 'WebJEA') { GetSteps_WebJEA $Settings }
        if ($OnlySections -contains 'All' -or $OnlySections -contains 'Finalize') { GetSteps_Finalize $Settings }
    }
    #endregion Config
    #region Functions
    function NormalizeFQDNs($Value, [string]$KeyName)
    {
        #SiteFQDNs accepts a single string or an array of them. Trim, drop blanks and the
        #optional root dot, then de-dup. Host names are case-insensitive but
        #Select-Object -Unique is not, so compare through an OrdinalIgnoreCase set - it
        #keeps the first spelling of a name and drops later differently-cased repeats.
        $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
        $names = @(@($Value) | ForEach-Object { "$_".Trim().TrimEnd('.') } | Where-Object { $_ -and $seen.Add($_) })

        foreach ($name in $names)
        {
            if ($name -eq '*')
            {
                throw "$KeyName contains the wildcard '*'; every host name WebJEA answers on must be spelled out."
            }
            if ($name -match '[:/\\@ ]')
            {
                throw "$KeyName entry '$name' must be a bare host name - no scheme, port, path or spaces (e.g. 'webjea.example.com')."
            }
            #Single-label names are allowed on purpose: a cert-less test install can use the
            #machine name. An IPv4 literal also passes - AllowedHosts accepts one.
            if ($name.Length -gt 253 -or
                $name -notmatch '^[A-Za-z0-9]([A-Za-z0-9-]{0,61}[A-Za-z0-9])?(\.[A-Za-z0-9]([A-Za-z0-9-]{0,61}[A-Za-z0-9])?)*$')
            {
                throw "$KeyName entry '$name' is not a valid host name (letters, digits and hyphens per label; each label 63 characters or fewer, 253 total)."
            }
        }
        return @($names)
    }

    function ResolveAccountSid([string]$Account)
    {
        #'.\name' is a valid ServiceUserName spelling but NTAccount cannot translate it;
        #the machine name means the same thing and does resolve. Everything downstream
        #compares and grants by SID so the private-key step stays idempotent regardless of
        #which spelling the settings file uses (Get-Acl would report the canonical
        #'MACHINE\name' and a plain string compare against '.\name' would never match,
        #re-granting on every deploy and then failing its own re-test).
        $name = if ($Account.StartsWith('.\')) { "$env:COMPUTERNAME\" + $Account.Substring(2) } else { $Account }
        try
        {
            return ([System.Security.Principal.NTAccount]$name).Translate([System.Security.Principal.SecurityIdentifier])
        }
        catch
        {
            throw "Could not resolve service account '$Account' to a SID, so its rights, folder access and certificate key access cannot be checked or granted. Verify the account exists and that this machine can reach its domain."
        }
    }

    function GetCertPrivateKeyPath([System.Security.Cryptography.X509Certificates.X509Certificate2]$Cert)
    {
        #The LocalMachine\My store itself is readable by everyone; what actually gates a
        #TLS handshake is the ACL on the private KEY FILE behind the certificate, which
        #Windows creates granting only SYSTEM and the local Administrators group.
        #Returns $null when there is no file to ACL - a hardware or ephemeral provider
        #(TPM, HSM, smart card), or a key stored somewhere other than the standard machine
        #key folders - so callers can say so instead of inventing a path.
        $key = $null
        foreach ($getter in @(
                { [System.Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($Cert) },
                { [System.Security.Cryptography.X509Certificates.ECDsaCertificateExtensions]::GetECDsaPrivateKey($Cert) }))
        {
            #These throw (rather than return null) when the certificate claims a private
            #key that cannot be opened - a key registration broken by an incomplete import,
            #or a key this process is not allowed to touch. Either way the HTTPS listener
            #would fail at startup with the same error, so surface it here with the fix
            #attached instead of letting a raw CryptographicException end the deploy.
            try
            {
                $key = & $getter
                if ($key) { break }
            }
            catch
            {
                throw ("Certificate '$($Cert.Thumbprint)' reports a private key, but it could not be opened: $($_.Exception.Message). " +
                    'Re-import the certificate from a PFX that includes the private key, or repair the existing key registration with ' +
                    "certutil -repairstore My $($Cert.Thumbprint)")
            }
        }
        if (-not $key) { return $null }

        if ($key -is [System.Security.Cryptography.RSACryptoServiceProvider])
        {
            $name = $key.CspKeyContainerInfo.UniqueKeyContainerName
        }
        else
        {
            $name = $key.Key.UniqueName
        }
        if (-not $name) { return $null }
        #Some providers hand back a full path rather than a bare container file name.
        if ($name -match '[\\/]') { if (Test-Path -Path $name -PathType Leaf) { return $name } else { return $null } }

        #Probe rather than infer the folder from the key type: a CNG key object (RSACng)
        #can front a legacy CAPI container that lives under RSA\MachineKeys, so mapping
        #type -> folder produces a plausible path to a file that is not there.
        foreach ($folder in @(
                "$env:ProgramData\Microsoft\Crypto\RSA\MachineKeys" #CAPI machine containers
                "$env:ProgramData\Microsoft\Crypto\Keys"            #CNG machine keys
                "$env:ProgramData\Microsoft\Crypto\SystemKeys"      #CNG keys owned by the system
            ))
        {
            $candidate = Join-Path $folder $name
            if (Test-Path -Path $candidate -PathType Leaf) { return $candidate }
        }
        return $null
    }

    function GetCertChainErrors([System.Security.Cryptography.X509Certificates.X509Certificate2]$Cert)
    {
        #The same test Kestrel applies when AllowInvalid is false (X509Certificate2Collection.
        #Find with validOnly): build the chain against this machine's trust store with
        #online revocation checking. Returns one line per chain problem; empty = trusted.
        $chain = New-Object System.Security.Cryptography.X509Certificates.X509Chain
        try
        {
            $chain.ChainPolicy.RevocationMode = [System.Security.Cryptography.X509Certificates.X509RevocationMode]::Online
            if ($chain.Build($Cert)) { return @() }
            return @($chain.ChainStatus | ForEach-Object { "$($_.Status): $($_.StatusInformation.Trim())" })
        }
        finally { $chain.Dispose() }
    }

    function GetCertProblems([System.Security.Cryptography.X509Certificates.X509Certificate2]$Cert, [string[]]$FQDNs, [switch]$SkipTrust)
    {
        #Fatal: Kestrel cannot serve the certificate at all. Warnings: it will serve it, but
        #some or all browsers will complain - the admin's call, so the deploy goes on.
        #-SkipTrust leaves out the chain build (an online revocation check per call) when
        #only the fatal list is wanted.
        $fatal = @()
        $warnings = @()
        if (-not $Cert.HasPrivateKey)
        {
            $fatal += 'it has no private key (export it again as a PFX that includes the private key)'
        }
        $serverAuthOid = '1.3.6.1.5.5.7.3.1'
        $eku = $Cert.Extensions | Where-Object { $_ -is [System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension] }
        if ($eku -and -not ($eku.EnhancedKeyUsages | Where-Object { $_.Value -eq $serverAuthOid }))
        {
            $fatal += 'its enhanced key usage does not include Server Authentication'
        }
        if ($SkipTrust) { return @{ Fatal = $fatal; Warnings = $warnings } }

        $now = Get-Date
        if ($Cert.NotBefore -gt $now) { $warnings += "it is not valid until $($Cert.NotBefore)" }
        elseif ($Cert.NotAfter -lt $now) { $warnings += "it expired on $($Cert.NotAfter) - browsers will refuse it" }
        else
        {
            $daysLeft = [int][Math]::Floor(($Cert.NotAfter - $now).TotalDays)
            if ($daysLeft -lt 30) { $warnings += "it expires in $daysLeft day(s)" }
        }
        #NotTimeValid repeats the date warning above.
        $chainErrors = @(GetCertChainErrors -Cert $Cert | Where-Object { $_ -notlike 'NotTimeValid:*' })
        if ($chainErrors.Count -gt 0)
        {
            $warnings += "it is not trusted by this server ($($chainErrors -join '; ')) - browsers that don't trust its issuer will show a warning"
        }
        $certNames = @($Cert.DnsNameList.Unicode)
        foreach ($fqdn in @($FQDNs))
        {
            $wildcardName = if ($fqdn.Contains('.')) { '*.' + $fqdn.Split('.', 2)[1] } else { $null }
            if (-not ($certNames -contains $fqdn -or ($wildcardName -and $certNames -contains $wildcardName)))
            {
                $warnings += "it does not cover host name '$fqdn' (certificate names: $($certNames -join ', ')) - browsers will show a name mismatch"
            }
        }
        return @{ Fatal = $fatal; Warnings = $warnings }
    }

    function OpenPfx([string]$Path, [string]$Password)
    {
        #Opened only to be checked, so keep the private key out of the machine's key
        #folders: EphemeralKeySet (.NET Framework 4.7.2+, every pwsh) holds it in memory.
        #Older frameworks get a temporary key container, which the caller's Reset() removes.
        #[string] keeps a missing password an empty string, which also opens a PFX that
        #was exported with no password, and avoids the SecureString overload.
        $flags = [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::DefaultKeySet
        if ([Enum]::GetNames([System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]) -contains 'EphemeralKeySet')
        {
            $flags = [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]'EphemeralKeySet'
        }
        return [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($Path, [string]$Password, $flags)
    }

    function GetBroadAccessWarnings([string]$Path, [string]$Label, [switch]$WarnOnRead)
    {
        #Deploy only ever ADDS what the service account needs; it does not take over a
        #folder's permissions. What it can do is say when a folder is open to everyone on
        #the box: write access there lets any signed-in user change what WebJEA runs (a
        #DLL next to WebJEA.exe, a script) or what it serves, and read access exposes any
        #secret stored in it. Inherit-only entries count too - Windows Server's default
        #"Users can create files in subfolders" comes through as one. Returns one message,
        #or nothing.
        #A folder that doesn't exist yet will inherit from its nearest existing parent, so
        #judge it by the parent's entries that a new subfolder would inherit.
        $target = $Path
        $inheritedFrom = $null
        while (-not (Test-Path -LiteralPath $target -PathType Container))
        {
            $parent = Split-Path -Path $target -Parent
            if ([string]::IsNullOrEmpty($parent) -or $parent -eq $target) { return }
            $target = $parent
            $inheritedFrom = $parent
        }

        $broadSids = @{
            'S-1-1-0'      = 'Everyone'
            'S-1-5-7'      = 'Anonymous Logon'
            'S-1-5-11'     = 'Authenticated Users'
            'S-1-5-4'      = 'Interactive'
            'S-1-5-32-545' = 'Users'
            'S-1-5-32-546' = 'Guests'
        }
        #FileSystemRights is a signed int; the generic bits are spelled out because a few
        #in-box entries (CREATOR OWNER and friends) are stored that way rather than mapped.
        $genericAll = 0x10000000
        $genericWrite = 0x40000000
        $genericRead = [int]::MinValue #0x80000000
        $writeMask = [int]([System.Security.AccessControl.FileSystemRights]'WriteData, AppendData, Delete, DeleteSubdirectoriesAndFiles, ChangePermissions, TakeOwnership') -bor $genericAll -bor $genericWrite
        $readMask = [int][System.Security.AccessControl.FileSystemRights]::ReadData -bor $genericAll -bor $genericRead

        $found = [ordered]@{}
        foreach ($rule in (Get-Acl -LiteralPath $target).GetAccessRules($true, $true, [System.Security.Principal.SecurityIdentifier]))
        {
            if ($rule.AccessControlType -ne 'Allow') { continue }
            if ($inheritedFrom -and -not ($rule.InheritanceFlags -band [System.Security.AccessControl.InheritanceFlags]::ContainerInherit)) { continue }
            $sid = $rule.IdentityReference.Value
            $name = $broadSids[$sid]
            #Domain Users is per-domain (S-1-5-21-<domain>-513), so match it by suffix.
            if (-not $name -and $sid -match '^S-1-5-21-.+-513$') { $name = 'Domain Users' }
            if (-not $name) { continue }

            $rights = [int]$rule.FileSystemRights
            $kind = $null
            if ($rights -band $writeMask) { $kind = 'write' }
            elseif ($WarnOnRead -and ($rights -band $readMask)) { $kind = 'read' }
            if (-not $kind) { continue }
            #Keep the strongest finding per group: write beats read.
            if ($found[$name] -ne 'write') { $found[$name] = $kind }
        }
        if ($found.Count -eq 0) { return }

        $where = if ($inheritedFrom) { "$Label '$Path' (not created yet; it will inherit from '$inheritedFrom')" } else { "$Label '$Path'" }
        $list = ($found.Keys | ForEach-Object { "$_ ($($found[$_]))" }) -join ', '
        $why = if ($found.Values -contains 'write') { 'Any signed-in user in these groups could change what WebJEA runs or serves.' }
        else { 'Any signed-in user in these groups could read the secrets stored there.' }
        return "$where grants access to broad groups: $list. $why See docs/System-Requirements.md#folder-permissions to restrict it."
    }

    function ValidateSettings( [psobject]$Settings )
    {
        #quick helper function
        function Assert($Condition, $Message) { if (-not $Condition) { throw $Message } }

        #A PFX setting (CertPath, GraphCertPath): the file must exist and open, and where it
        #lives matters. Returns @{ Cert; Password }; the caller checks the certificate for
        #its own use and must Reset() it. $Stored is the password an earlier deploy wrote to
        #appsettings.Production.json (already plain text there). $OtherPfx is the other
        #WebJEA PFX, which may share the folder.
        function ResolvePfx([string]$Path, [securestring]$Password, [string]$Stored,
            [string]$SettingName, [string]$ParamName, [string]$Example, [string]$Missing, [string]$OtherPfx)
        {
            Assert ($Path -match '^([A-Za-z]:\\|\\\\)') "$SettingName '$Path' must be a full path to the PFX file, e.g. $Example."
            Assert (Test-Path -LiteralPath $Path -PathType Leaf) "$SettingName '$Path' does not exist. $Missing"

            #Password: the parameter when given (it must work); otherwise the one stored by
            #an earlier deploy, so re-runs don't need it again; otherwise none.
            $candidates = @()
            if ($Password)
            {
                $candidates += , @($ParamName, [System.Net.NetworkCredential]::new('', $Password).Password)
            }
            else
            {
                if ($Stored) { $candidates += , @('the password stored by an earlier deploy', $Stored) }
                $candidates += , @('no password', '')
            }
            $cert = $null
            $resolved = $null
            $openError = $null
            foreach ($candidate in $candidates)
            {
                try
                {
                    $cert = OpenPfx -Path $Path -Password $candidate[1]
                    $resolved = $candidate[1]
                    Write-Verbose "$SettingName opened with $($candidate[0])."
                    break
                }
                catch
                {
                    #Unwrap PowerShell's 'Exception calling ".ctor"...' to the crypto message.
                    $openError = if ($_.Exception.InnerException) { $_.Exception.InnerException.Message.Trim() } else { $_.Exception.Message }
                }
            }
            if (-not $cert)
            {
                if ($Password) { throw "The PFX at $SettingName '$Path' could not be opened with ${ParamName}: $openError" }
                throw "The PFX at $SettingName '$Path' could not be opened without a password ($openError). Pass its password with $ParamName (e.g. $ParamName (Read-Host -AsSecureString)); later deploys reuse it."
            }

            try
            {
                #Where the file lives matters: the service account must not be able to swap
                #its own certificate, and upgrades must not touch it.
                $folder = Split-Path -Path $Path -Parent
                foreach ($owned in @(
                        @{ Label = 'SitePath'; Path = $settings.SitePath; Why = 'upgrades replace the site folder' }
                        @{ Label = 'ScriptsPath'; Path = $settings.ScriptsPath; Why = 'script authors may be able to write there' }
                        @{ Label = 'LogPath'; Path = $settings.LogPath; Why = 'the service account can write there, so it could replace its own certificate' }))
                {
                    if (($folder + '\').StartsWith($owned.Path + '\', [StringComparison]::OrdinalIgnoreCase))
                    {
                        Write-Warning "$SettingName '$Path' is inside $($owned.Label) - $($owned.Why). Keep the PFX in its own folder, e.g. C:\webjea\tls (see docs/Certificate-Renewal.md#where-to-put-the-pfx)."
                    }
                }
                #Read access only matters when there is no password between a reader and the key.
                $warning = GetBroadAccessWarnings -Path $folder -Label "The $SettingName folder" -WarnOnRead:([string]::IsNullOrEmpty($resolved))
                if ($warning) { Write-Warning $warning }
                $otherKeys = @(Get-ChildItem -LiteralPath $folder -File -ErrorAction SilentlyContinue |
                        Where-Object { $_.Extension -in '.pfx', '.p12', '.key', '.pem' -and $_.FullName -ne $Path -and $_.FullName -ne $OtherPfx })
                if ($otherKeys.Count -gt 0)
                {
                    Write-Warning "The $SettingName folder '$folder' also holds $($otherKeys.Name -join ', '). The service account is granted read on the whole folder, so keep only WebJEA's PFX files there."
                }
            }
            catch
            {
                $cert.Reset()
                throw
            }
            return @{ Cert = $cert; Password = $resolved }
        }

        #Reject IIS-era settings files outright: this version hosts as a Windows service.
        $retiredKeys = @('SiteName', 'AppPoolName', 'AppPoolUserName', 'AppPoolPassword',
            'AppPoolLoadUserProfile', 'DisableDefaultWebsite', 'AppName', 'ParentSiteName')
        $foundRetired = @($retiredKeys | Where-Object { $Settings.PSObject.Properties[$_] })
        Assert ($foundRetired.Count -eq 0) ("Settings file contains retired IIS-era keys: $($foundRetired -join ', '). " +
            'This version of WebJEA installs as a Windows service, not an IIS site, and sub-application ' +
            'installs are no longer supported on Windows (use containers - see docs/docker.md). ' +
            'Start from the new settings.template.json: ServiceName/ServiceUserName replace the site/app-pool keys. ' +
            'See docs/deployment-migration.md.')

        #Kestrel's native certificate configuration selects by subject and has no thumbprint
        #option. Point at the replacement, quoting the old certificate's name when it can be
        #found so the fix is copy-paste.
        if ($Settings.PSObject.Properties['CertThumbprint'])
        {
            $hint = ''
            if ($Settings.CertThumbprint -match '^[0-9A-Fa-f]{40}$')
            {
                $oldCert = Get-Item -Path "Cert:\LocalMachine\My\$($Settings.CertThumbprint)" -ErrorAction SilentlyContinue
                if ($oldCert) { $hint = " That certificate's subject name is '$($oldCert.GetNameInfo('SimpleName', $false))' - use that as CertSubject." }
            }
            throw ("Settings file contains the retired key CertThumbprint. The HTTPS certificate is now selected by Kestrel " +
                "by subject: replace CertThumbprint with CertSubject.$hint See docs/deployment-migration.md.")
        }

        #Remove trailing slashes from paths to avoid problems later
        $settings.SitePath = $settings.SitePath.TrimEnd('\')
        $settings.ScriptsPath = $settings.ScriptsPath.TrimEnd('\')
        $settings.LogPath = $settings.LogPath.TrimEnd('\')

        #Default the service name
        if (-not $settings.PSObject.Properties['ServiceName'] -or [string]::IsNullOrWhiteSpace($settings.ServiceName))
        {
            $settings | Add-Member -NotePropertyName ServiceName -NotePropertyValue 'WebJEA' -Force
        }

        #Passwords are accepted only as parameters (-ServicePassword, -CertPassword), so they
        #never sit in a settings file next to the release. Files from earlier releases carry
        #an empty ServicePassword key, which is harmless and accepted.
        Assert ([string]::IsNullOrEmpty([string]$settings.ServicePassword)) ('The settings file contains a ServicePassword. ' +
            'Pass it with -ServicePassword instead (e.g. -ServicePassword (Read-Host -AsSecureString)) so it is not stored in the settings file, and remove the key.')
        Assert (-not $Settings.PSObject.Properties['CertPassword']) ('The settings file contains CertPassword. ' +
            'Pass it with -CertPassword instead (e.g. -CertPassword (Read-Host -AsSecureString)) so it is not stored in the settings file, and remove the key.')
        Assert (-not $Settings.PSObject.Properties['GraphCertPassword']) ('The settings file contains GraphCertPassword. ' +
            'Pass it with -GraphCertPassword instead (e.g. -GraphCertPassword (Read-Host -AsSecureString)) so it is not stored in the settings file, and remove the key.')

        #verify the service account: a gMSA has no password; a regular account needs one
        #unless the service already runs as it (a re-deploy keeps the stored credential)
        Assert ($settings.ServiceUserName -match '^[^\\]+\\[^\\]+$') "ServiceUserName '$($settings.ServiceUserName)' is not in the correct format. It should be in the format 'domain\\username' or 'machinename\\username'."
        if ($settings.ServiceUserName -notmatch '\$$')
        {
            if (-not $ServicePassword)
            {
                #Same comparison the service step makes, so "already runs as it" here means
                #that step will leave the account (and its password) alone.
                $existingService = Get-CimInstance Win32_Service -Filter "Name='$($settings.ServiceName)'" -ErrorAction SilentlyContinue
                Assert ($existingService -and $existingService.StartName -eq $settings.ServiceUserName) ("ServiceUserName '$($settings.ServiceUserName)' is a regular account " +
                    "and the '$($settings.ServiceName)' service does not already run as it, so its password is needed: pass -ServicePassword (e.g. -ServicePassword (Read-Host -AsSecureString)), or use a gMSA (trailing `$).")
            }
        }
        else
        {
            Assert (-not $ServicePassword) "ServiceUserName '$($settings.ServiceUserName)' is a gMSA (ends with `$), which has no password. Remove -ServicePassword, or use a regular account."
            #Get-ADServiceAccount ships with RSAT (ActiveDirectory module) and is absent on
            #non-domain-controller servers that haven't had RSAT installed. Running it anyway
            #would throw a raw command-not-found that the try/catch below swallows, leaving
            #$userobj unset and the Assert below reporting the WRONG diagnosis ("not a valid
            #gMSA") when the real problem is that the module isn't installed. Same guard voice
            #as the setspn.exe check further down.
            if (-not (Get-Command Get-ADServiceAccount -ErrorAction SilentlyContinue))
            {
                Write-Warning "Get-ADServiceAccount is not available (ActiveDirectory PowerShell module / RSAT not installed); cannot verify that '$($settings.ServiceUserName)' is a valid gMSA. Proceeding on format checks alone - install RSAT (Active Directory tools) to validate its existence."
            }
            else
            {
                try
                {
                    $userobj = Get-ADServiceAccount -Identity $settings.ServiceUserName.Split('\')[1]
                }
                catch {}
                Assert ($null -ne $userobj) "ServiceUserName '$($settings.ServiceUserName)' does not appear to be a valid gMSA account. Provide a valid gMSA, or a regular account with -ServicePassword."
            }
        }

        #Ports: at least one enabled; https requires a cert ([int]$null is 0)
        $httpPort = [int]$settings.HttpPort
        $httpsPort = [int]$settings.HttpsPort
        Assert ($httpPort -gt 0 -or $httpsPort -gt 0) 'At least one of HttpPort or HttpsPort must be set to a port number.'
        #The certificate comes from exactly one place: the machine store (CertSubject) or a
        #PFX file (CertPath). Which key is filled in picks the mode.
        $hasCertSubject = -not [string]::IsNullOrWhiteSpace([string]$settings.CertSubject)
        $hasCertPath = -not [string]::IsNullOrWhiteSpace([string]$settings.CertPath)
        if ($httpsPort -gt 0)
        {
            Assert ($hasCertSubject -or $hasCertPath) ('HttpsPort is enabled but no certificate is set: set CertSubject (a certificate in LocalMachine\My) ' +
                'or CertPath (a PFX file, recommended for ACME certificates).')
            Assert (-not ($hasCertSubject -and $hasCertPath)) 'Set only one of CertSubject (a certificate in LocalMachine\My) and CertPath (a PFX file); leave the other empty.'
        }
        #The Graph client certificate (Entra mode) is optional and independent of the ports:
        #the machine store (GraphCertSubject), a PFX file (GraphCertPath), or neither - then
        #AzureAd is configured by hand and left alone. Settings files from before these keys
        #existed simply have neither.
        $hasGraphCertSubject = -not [string]::IsNullOrWhiteSpace([string]$settings.GraphCertSubject)
        $hasGraphCertPath = -not [string]::IsNullOrWhiteSpace([string]$settings.GraphCertPath)
        Assert (-not ($hasGraphCertSubject -and $hasGraphCertPath)) ('Set only one of GraphCertSubject (a certificate in LocalMachine\My) and GraphCertPath (a PFX file); ' +
            'leave the other empty, or both empty to configure AzureAd yourself.')
        #Trimmed once here: what is checked is what the AppConfig step writes.
        if ($hasGraphCertSubject) { $settings.GraphCertSubject = $settings.GraphCertSubject.Trim() }
        if ($hasGraphCertPath) { $settings.GraphCertPath = $settings.GraphCertPath.Trim() }
        if ($GraphCertPassword -and -not $hasGraphCertPath)
        {
            Write-Warning '-GraphCertPassword is ignored: it is only used with GraphCertPath.'
        }
        #Retired, but harmless: Kestrel is now always allowed to serve the certificate it
        #finds, and trust problems are warnings. Keep old settings files deploying.
        if ($Settings.PSObject.Properties['AllowUntrustedCert'])
        {
            Write-Warning 'AllowUntrustedCert is no longer used and is ignored: certificate trust problems are now warnings, not errors. Remove it from the settings file.'
        }

        #paths must differ (unchanged)
        Assert ($settings.SitePath -ne $settings.ScriptsPath) 'SitePath and ScriptsPath cannot be the same. Please update the settings file to specify different paths.'
        Assert ($settings.SitePath -ne $settings.LogPath) 'SitePath and LogPath cannot be the same. Please update the settings file to specify different paths.'
        Assert ($settings.LogFile -ne $settings.LogUsageFile) 'LogFile and LogUsageFile cannot be the same. Please update the settings file to specify different paths.'

        #The deployed appsettings.Production.json, if any: it holds secrets that matter to
        #the folder checks below, and the PFX password from an earlier deploy.
        $appSettingsPath = "$($settings.SitePath)\appsettings.Production.json"
        $appSettings = $null
        if (Test-Path -LiteralPath $appSettingsPath -PathType Leaf)
        {
            try { $appSettings = Get-Content -LiteralPath $appSettingsPath -Raw | ConvertFrom-Json } catch {}
        }

        #Host names: one list, explicit names only. SiteFQDN/SecondarySiteFQDNs were never
        #treated differently downstream (allowed hosts, SPNs, cert coverage, smoke probe all
        #re-merged them), so they are now the single SiteFQDNs key.
        $legacyFqdnKeys = @(@('SiteFQDN', 'SecondarySiteFQDNs') | Where-Object { $Settings.PSObject.Properties[$_] })
        Assert ($legacyFqdnKeys.Count -eq 0) ("Settings file contains retired key(s): $($legacyFqdnKeys -join ', '). " +
            'List every host name WebJEA answers on in the single SiteFQDNs key instead, e.g. ' +
            '"SiteFQDNs": [ "webjea.example.com", "alias.example.com" ]. See docs/deployment-migration.md.')

        #Normalized once here so every step below (and the closures they capture) shares the
        #same validated list.
        #[string[]] cast: a one-name list would otherwise come back off the pipeline as a
        #bare string, and the property is easier to reason about as always-an-array.
        [string[]]$allFQDNs = NormalizeFQDNs $Settings.SiteFQDNs 'SiteFQDNs'
        Assert ($allFQDNs.Count -gt 0) 'SiteFQDNs must list at least one host name WebJEA answers on.'
        $Settings | Add-Member -NotePropertyName SiteFQDNs -NotePropertyValue $allFQDNs -Force

        #Certificate checks, before the service is stopped. Kestrel is always told
        #AllowInvalid, so it serves whatever certificate it can load: only what would stop
        #it loading one fails the deploy here, and everything else (trust, dates, names) is
        #a warning - the admin chose the certificate.
        $certMatches = @()
        $resolvedCertPassword = $null
        if ($httpsPort -gt 0 -and $hasCertSubject)
        {
            $subject = $settings.CertSubject.Trim()
            $settings.CertSubject = $subject #written to Kestrel:Endpoints as-is

            #IndexOf, not -like: a wildcard certificate's subject contains '*', which -like
            #would treat as a pattern. Case-insensitive substring is what Kestrel's
            #FindBySubjectName does. A public-key-only certificate cannot complete a TLS
            #handshake (and has no key to ACL), so it is never a match.
            $certMatches = @(Get-ChildItem -Path Cert:\LocalMachine\My |
                    Where-Object { $_.HasPrivateKey -and $_.Subject.IndexOf($subject, [StringComparison]::OrdinalIgnoreCase) -ge 0 } |
                    Sort-Object -Property NotAfter -Descending)
            Assert ($certMatches.Count -gt 0) ("No certificate with a private key in LocalMachine\My has a subject containing '$subject'. " +
                'CertSubject is matched against the certificate subject (e.g. CN=webjea.example.com), not its SANs. ' +
                'Import the certificate from a PFX that includes the private key, or correct CertSubject.')

            $subjects = @($certMatches | ForEach-Object { $_.Subject } | Select-Object -Unique)
            if ($subjects.Count -gt 1)
            {
                Write-Warning "CertSubject '$subject' matches certificates with different subjects ($($subjects -join ' | ')). Kestrel serves the one that expires last - use a more specific CertSubject if that is not intended."
            }

            #With AllowInvalid, Kestrel skips only matches that don't allow Server
            #Authentication (and keys it can't open - the Service section grants that), then
            #serves the one that expires last. Newest-first, so the first usable one is it.
            $usable = @($certMatches | Where-Object { (GetCertProblems -Cert $_ -FQDNs @() -SkipTrust).Fatal.Count -eq 0 })
            Assert ($usable.Count -gt 0) ("No certificate matching CertSubject '$subject' allows Server Authentication, so Kestrel cannot serve any of them: " +
                (($certMatches | ForEach-Object { "$($_.Thumbprint) '$($_.Subject)'" }) -join ', ') + '. Issue one for Server Authentication, or correct CertSubject.')
            $cert = $usable[0]
            $certLabel = "Certificate $($cert.Thumbprint) '$($cert.Subject)' (issued by '$($cert.Issuer)', expires $($cert.NotAfter.ToString('yyyy-MM-dd')))"
            $problems = GetCertProblems -Cert $cert -FQDNs $allFQDNs
            foreach ($problem in $problems.Warnings) { Write-Warning "${certLabel}: $problem." }

            #The latest expiry wins even over a better certificate - e.g. a self-signed lab
            #certificate imported with the same subject. Say so when that is what happens.
            if ($problems.Warnings.Count -gt 0)
            {
                foreach ($older in @($usable | Select-Object -Skip 1))
                {
                    if ((GetCertProblems -Cert $older -FQDNs $allFQDNs).Warnings.Count -eq 0)
                    {
                        Write-Warning "Kestrel will serve $($cert.Thumbprint) because it expires last, although $($older.Thumbprint) (expires $($older.NotAfter.ToString('yyyy-MM-dd'))) has none of these problems. Remove $($cert.Thumbprint) from LocalMachine\My if it should not be served."
                        break
                    }
                }
            }
        }
        elseif ($httpsPort -gt 0)
        {
            #PFX file: Kestrel loads it from the path and reloads it when the file changes, so
            #an ACME client renewing it in place needs no restart and no key permissions.
            $certPath = $settings.CertPath.Trim()
            $settings.CertPath = $certPath #written to Kestrel:Endpoints as-is
            $stored = $null
            if ($appSettings -and $appSettings.PSObject.Properties['Kestrel']) { $stored = [string]$appSettings.Kestrel.Endpoints.Https.Certificate.Password }
            $pfx = ResolvePfx -Path $certPath -Password $CertPassword -Stored $stored -SettingName 'CertPath' -ParamName '-CertPassword' `
                -Example 'C:\webjea\tls\webjea.pfx' -Missing 'Point it at the PFX file your ACME client (or you) writes - see docs/Certificate-Renewal.md.' `
                -OtherPfx $settings.GraphCertPath
            $cert = $pfx.Cert
            $resolvedCertPassword = $pfx.Password
            try
            {
                $certLabel = "Certificate $($cert.Thumbprint) '$($cert.Subject)' in '$certPath' (issued by '$($cert.Issuer)', expires $($cert.NotAfter.ToString('yyyy-MM-dd')))"
                $problems = GetCertProblems -Cert $cert -FQDNs $allFQDNs
                Assert ($problems.Fatal.Count -eq 0) "$certLabel cannot serve HTTPS: $($problems.Fatal -join '; ')."
                foreach ($problem in $problems.Warnings) { Write-Warning "${certLabel}: $problem." }
            }
            finally
            {
                #Reset deletes the temporary key container a non-ephemeral import leaves
                #behind on older .NET Framework versions.
                $cert.Reset()
            }
        }
        #Every match, not just the one Kestrel serves today: the private-key ACL step grants
        #on all of them so an already-imported renewal works on the next restart.
        $Settings | Add-Member -NotePropertyName CertMatches -NotePropertyValue $certMatches -Force
        #Written to Kestrel:Endpoints by the AppConfig step. Never printed.
        $Settings | Add-Member -NotePropertyName ResolvedCertPassword -NotePropertyValue $resolvedCertPassword -Force

        #Graph client certificate. Its trust comes from the public key uploaded to the app
        #registration, not from a CA, so there is no chain, name or EKU to check - only that
        #Microsoft.Identity.Web will find one with a usable key. Dates are warnings: sign-in
        #still works without Graph, but group overage, display names and revalidation do not.
        function GetGraphCertWarnings([System.Security.Cryptography.X509Certificates.X509Certificate2]$Cert)
        {
            $now = Get-Date
            if ($Cert.NotBefore -gt $now) { return "it is not valid until $($Cert.NotBefore), so Graph calls fail until then" }
            if ($Cert.NotAfter -lt $now) { return "it expired on $($Cert.NotAfter), so Graph calls will fail" }
            $daysLeft = [int][Math]::Floor(($Cert.NotAfter - $now).TotalDays)
            if ($daysLeft -lt 30) { return "it expires in $daysLeft day(s) - renew it and upload the new public key to the app registration before then (docs/Certificate-Renewal.md#graph-client-certificate)" }
        }
        $graphCertMatches = @()
        $resolvedGraphCertPassword = $null
        if ($hasGraphCertSubject)
        {
            $dn = $settings.GraphCertSubject
            #The exact subject DN, compared case-insensitively - what Microsoft.Identity.Web's
            #StoreWithDistinguishedName lookup (FindBySubjectDistinguishedName) matches.
            #Unlike CertSubject, part of the name is not enough.
            $graphCertMatches = @(Get-ChildItem -Path Cert:\LocalMachine\My |
                    Where-Object { $_.HasPrivateKey -and $_.Subject -eq $dn } |
                    Sort-Object -Property NotBefore -Descending)
            if ($graphCertMatches.Count -eq 0)
            {
                $near = @(Get-ChildItem -Path Cert:\LocalMachine\My |
                        Where-Object { $_.HasPrivateKey -and $_.Subject.IndexOf($dn, [StringComparison]::OrdinalIgnoreCase) -ge 0 } |
                        ForEach-Object { "'$($_.Subject)'" } | Select-Object -Unique)
                $hint = if ($near.Count -gt 0) { " Certificates whose subject contains it: $($near -join ', ') - use the whole subject." } else { '' }
                throw ("No certificate with a private key in LocalMachine\My has the subject '$dn'. GraphCertSubject is the certificate's exact subject (e.g. CN=WebJEA-Graph), not part of it.$hint " +
                    'Create or import the Graph client certificate (docs/entra-appreg.md#option-a-certificate-recommended), or correct GraphCertSubject.')
            }
            #Microsoft.Identity.Web skips matches that are not valid right now and uses the
            #one issued last (latest NotBefore) of the rest, so that is the one to judge.
            $now = Get-Date
            $current = @($graphCertMatches | Where-Object { $_.NotBefore -le $now -and $_.NotAfter -ge $now })
            if ($current.Count -eq 0)
            {
                Write-Warning ("No certificate matching GraphCertSubject '$dn' is valid today (" +
                    (($graphCertMatches | ForEach-Object { "$($_.Thumbprint) valid $($_.NotBefore.ToString('yyyy-MM-dd')) to $($_.NotAfter.ToString('yyyy-MM-dd'))" }) -join ', ') +
                    '). Microsoft.Identity.Web only uses a currently valid one, so Graph calls will fail: import a renewed certificate and upload its public key to the app registration (docs/Certificate-Renewal.md#graph-client-certificate).')
            }
            else
            {
                $graphCert = $current[0]
                $warning = GetGraphCertWarnings -Cert $graphCert
                if ($warning) { Write-Warning "Graph client certificate $($graphCert.Thumbprint) '$($graphCert.Subject)': $warning." }
            }
        }
        elseif ($hasGraphCertPath)
        {
            #Microsoft.Identity.Web reads the PFX once at startup, with an ephemeral key, so
            #a replaced file is used after the next restart.
            $graphCertPath = $settings.GraphCertPath
            $stored = $null
            if ($appSettings -and $appSettings.PSObject.Properties['AzureAd'])
            {
                $existing = @($appSettings.AzureAd.ClientCredentials | Where-Object { $_ })
                if ($existing.Count -gt 0 -and $existing[0].SourceType -eq 'Path') { $stored = [string]$existing[0].CertificatePassword }
            }
            $pfx = ResolvePfx -Path $graphCertPath -Password $GraphCertPassword -Stored $stored -SettingName 'GraphCertPath' -ParamName '-GraphCertPassword' `
                -Example 'C:\webjea\tls\webjea-graph.pfx' -Missing 'Point it at the Graph client certificate PFX (docs/entra-appreg.md#option-a-certificate-recommended).' `
                -OtherPfx $settings.CertPath
            $graphCert = $pfx.Cert
            $resolvedGraphCertPassword = $pfx.Password
            try
            {
                $graphLabel = "Graph client certificate $($graphCert.Thumbprint) '$($graphCert.Subject)' in '$graphCertPath'"
                Assert $graphCert.HasPrivateKey "$graphLabel has no private key. Export it again as a PFX that includes the private key."
                $warning = GetGraphCertWarnings -Cert $graphCert
                if ($warning) { Write-Warning "${graphLabel}: $warning." }
            }
            finally
            {
                $graphCert.Reset()
            }
        }
        #Every match, for the same reason as CertMatches.
        $Settings | Add-Member -NotePropertyName GraphCertMatches -NotePropertyValue $graphCertMatches -Force
        #Written to AzureAd:ClientCredentials by the AppConfig step. Never printed.
        $Settings | Add-Member -NotePropertyName ResolvedGraphCertPassword -NotePropertyValue $resolvedGraphCertPassword -Force

        #ClientCredentials wins over the other two ways of giving Microsoft.Identity.Web a
        #credential, so once Deploy.ps1 writes it they are dead weight - and a client secret
        #left in the site folder is still a secret. Said, not removed: the admin put them there.
        $hasAzureAd = $appSettings -and $appSettings.PSObject.Properties['AzureAd']
        if (($hasGraphCertSubject -or $hasGraphCertPath) -and $hasAzureAd)
        {
            foreach ($key in 'ClientSecret', 'ClientCertificates')
            {
                #An empty value (a template's placeholder) is no credential, so not worth a word.
                if (@($appSettings.AzureAd.$key | Where-Object { $_ }).Count -gt 0)
                {
                    Write-Warning "appsettings.Production.json has AzureAd.$key, which is ignored once Deploy.ps1 writes AzureAd.ClientCredentials for GraphCert$(if ($hasGraphCertSubject) { 'Subject' } else { 'Path' }). Remove it from $appSettingsPath$(if ($key -eq 'ClientSecret') { ', and delete the secret from the app registration' })."
                }
            }
        }

        #Folder access: the Server section only adds what the service account lacks, so
        #folders left open to every user (the default under C:\) are reported here rather
        #than changed. Reading SitePath only matters once a secret is stored there: a PFX
        #password (HTTPS or Graph), a client secret, or a whole PFX in Base64.
        $siteHasSecret = -not ([string]::IsNullOrEmpty($resolvedCertPassword) -and [string]::IsNullOrEmpty($resolvedGraphCertPassword))
        if ($hasAzureAd)
        {
            $siteHasSecret = $siteHasSecret -or -not [string]::IsNullOrEmpty([string]$appSettings.AzureAd.ClientSecret) -or
            [bool](@($appSettings.AzureAd.ClientCredentials) | Where-Object { $_ -and ($_.CertificatePassword -or $_.Base64EncodedValue -or $_.ClientSecret) })
        }
        $folderChecks = @(
            @{ Label = 'SitePath'; Path = $settings.SitePath; WarnOnRead = $siteHasSecret }
            @{ Label = 'ScriptsPath'; Path = $settings.ScriptsPath; WarnOnRead = $false }
            @{ Label = 'LogPath'; Path = $settings.LogPath; WarnOnRead = $false }
        )
        foreach ($check in $folderChecks)
        {
            $warning = GetBroadAccessWarnings -Path $check.Path -Label $check.Label -WarnOnRead:$check.WarnOnRead
            if ($warning) { Write-Warning $warning }
        }
    }

    function InvokeStep([psobject]$Step, [switch]$TestOnly)
    {
        if (-not $step.description)
        {
            Write-Host "Error with configuration: $($Step | ConvertTo-Json -Depth 2)" -ForegroundColor Yellow
            return
        }
        if ($step.description -and -not ($step.testscript -or $step.setscript))
        {
            Write-Host $($Step.Description) -ForegroundColor Cyan
        }
        else
        {
            Write-Host "> $($Step.Description)" -ForegroundColor Cyan
        }
        $hasTest = $false
        if ($Step.RunAlways -and $Step.SetScript)
        {
            ##### Action step - runs on every real deploy, nothing to verify afterwards
            if ($TestOnly)
            {
                Write-Host '  [SKIP] action step (always runs on a real deploy).' -ForegroundColor Yellow
            }
            else
            {
                if ($Step.setscript -is [string]) { $Step.SetScript = [scriptblock]::Create($Step.SetScript) }
                Write-Verbose "  Set: $($Step.SetScript.ToString())"
                $set = & $Step.SetScript
                Write-Host '  [DONE]' -ForegroundColor Green
            }
        }
        elseif ($Step.TestScript -and $Step.SetScript)
        {
            ##### Custom Script Resource
            Write-Verbose "  Test: $($Step.TestScript.ToString())"
            if ($Step.testscript -is [string]) { $Step.TestScript = [scriptblock]::Create($Step.TestScript) }
            if ($Step.setscript -is [string]) { $Step.SetScript = [scriptblock]::Create($Step.SetScript) }

            $test = @{InDesiredState = & $Step.TestScript }
            if (-not $test.InDesiredState -and -not $TestOnly -and $Step.SetScript)
            {
                Write-Host '  [SET] not in desired state. Updating...'
                Write-Verbose "  Set: $($Step.SetScript.ToString())"
                $set = & $Step.SetScript
                $test = @{InDesiredState = & $Step.TestScript }
            }
            $hasTest = $true
        }
        elseif (-not $Step.TestScript -and -not $Step.SetScript)
        {
            #Heading-only step, nothing to do
            Write-Verbose 'No TestScript/SetScript specified for this step. Skipping.'
        }
        else
        {
            Write-Verbose ($step | ConvertTo-Json -Depth 2)
            throw "Invalid resource step: $($Step | ConvertTo-Json -Depth 2)"
        }

        if ($hasTest)
        {
            if ($test.InDesiredState)
            {
                Write-Host '  [OK] in desired state.' -ForegroundColor Green
            }
            elseif ($TestOnly)
            {
                Write-Host '  [CHANGE] not in desired state; a deploy would update it.' -ForegroundColor Yellow
            }
            else
            {
                #the Set ran and the re-test still failed - stop the deploy instead of
                #silently reporting success with a half-configured server
                Write-Host '  [FAILED] still not in desired state after Set.' -ForegroundColor Red
                throw "Step '$($Step.Description)' failed to reach desired state."
            }
        }
    }
    #endregion Functions
}
process
{
    #region Main
    [string]$SettingsFilePath = Resolve-Path -Path $SettingsFile
    Write-Verbose "Reading settings from file: $($SettingsFilePath)"
    #Join the comment-stripped lines into ONE string before ConvertFrom-Json: Windows
    #PowerShell 5.1 parses each pipeline input as its own JSON document, so piping a
    #multi-line file line-by-line fails there (pwsh 7 concatenates and would mask this).
    $Settings = (Get-Content -Path $SettingsFilePath | Where-Object { $_ -notmatch '^\s*//' }) -join "`n" | ConvertFrom-Json
    $settings | Add-Member -NotePropertyName 'SourcePath' -NotePropertyValue $PSScriptRoot
    Write-Verbose "Settings read from file (and calculated settings): $($Settings | ConvertTo-Json -Depth 1)"
    if ($OnlyReturnSettings)
    {
        Write-Output $Settings
        return
    }
    ValidateSettings -Settings $Settings

    #HTTP->HTTPS redirect is automatic whenever an HTTPS listener exists (the app's
    #redirect middleware discovers its port) - there is no separate opt-in setting. If
    #HttpPort is enabled without HttpsPort, HTTP is served directly with NO redirect to
    #HTTPS; that's a valid HTTP-only install, but it's also an easy accident, so require an
    #explicit opt-in. (ValidateSettings already requires CertSubject or CertPath whenever HttpsPort is set.)
    if (-not $AllowHttpWithoutRedirect)
    {
        $httpEnabled = [int]$Settings.HttpPort -gt 0
        $wouldRedirect = [int]$Settings.HttpsPort -gt 0
        if ($httpEnabled -and -not $wouldRedirect)
        {
            throw @"
HttpPort is enabled ($($Settings.HttpPort)) but HttpsPort is not, so HTTP requests will be served directly with no redirect to HTTPS.
HTTP is redirected to HTTPS automatically whenever both HttpPort and HttpsPort (with CertSubject or CertPath) are configured - there is no separate redirect setting.
If an HTTP-only install is intentional, re-run with -AllowHttpWithoutRedirect to proceed.
"@
        }
    }

    #This version hosts WebJEA as a Kestrel Windows service and no longer uses IIS. If IIS
    #is on this box AND has site bindings on the ports this deployment needs, HTTP.sys would
    #collide with the service's listeners, and an existing WebJEA IIS install will not be
    #upgraded in place. IIS serving unrelated sites on other ports is fine; only stop when
    #the requested ports actually conflict (or bindings can't be verified).
    if (-not $IgnoreExistingIIS)
    {
        $w3svc = Get-Service -Name W3SVC -ErrorAction SilentlyContinue
        if ($w3svc)
        {
            [int[]]$requestedPorts = @([int]$Settings.HttpPort, [int]$Settings.HttpsPort) | Where-Object { $_ -gt 0 }
            try
            {
                Import-Module WebAdministration -ErrorAction Stop
                #bindingInformation is 'ip:port:hostheader'; take [-2] so bracketed IPv6
                #addresses in the ip segment don't break the port extraction
                [int[]]$iisPorts = @(Get-WebBinding |
                        Where-Object { $_.protocol -in @('http', 'https') } |
                        ForEach-Object { ($_.bindingInformation -split ':')[-2] } |
                        Where-Object { $_ -match '^\d+$' } |
                        ForEach-Object { [int]$_ })
                [int[]]$conflictingPorts = @($requestedPorts | Where-Object { $_ -in $iisPorts })
                $reason = "IIS is installed on this server and has site bindings on port(s) $($conflictingPorts -join ', '), which this deployment requires."
            }
            catch
            {
                [int[]]$conflictingPorts = $requestedPorts
                $reason = "IIS is installed on this server (the W3SVC service exists) and its site bindings could not be enumerated ($($_.Exception.Message)), so ports $($requestedPorts -join ', ') cannot be confirmed free."
            }
            if ($conflictingPorts.Count -gt 0)
            {
                throw @"
$reason
This version of WebJEA runs as a self-hosted Windows service on Kestrel and no longer installs into IIS:
  - WebJEA needs exclusive use of ports $($Settings.HttpPort)/$($Settings.HttpsPort); IIS bindings on those ports will conflict.
  - An existing WebJEA IIS site is NOT migrated automatically. See docs/deployment-migration.md
    for the manual steps (remove the old site/app pools or free the ports; scripts and logs are reused).
  - IIS sub-application installs are no longer supported on Windows. Run additional instances as
    containers instead - see docs/docker.md and docker/examples/.
If you have resolved the port conflicts, or accept the risk, re-run with -IgnoreExistingIIS to proceed.
"@
            }
        }
    }

    [hashtable[]]$Steps = GetSteps -Settings $Settings -OnlySections $OnlySections
    Write-Host "Found $($Steps.Count) configuration steps to apply based on the settings and selected sections."
    if ($OnlyReturnSteps)
    {
        Write-Output $Steps
        return
    }
    #Use a combination of DSC and custom scripts to configure the server.
    foreach ($Step in $Steps)
    {
        InvokeStep -Step $Step -TestOnly:$TestOnly
    }

    #endregion Main
}
end
{

}