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
    [ValidateSet('PowerShell', 'Server', 'Service', 'WebJEA', 'Finalize', 'All')]
    [string[]]$OnlySections = 'All',

    [Parameter()]
    [switch]$IgnoreExistingIIS,

    [Parameter()]
    [switch]$SkipSpnCheck,

    [Parameter()]
    [switch]$AllowHttpWithoutRedirect
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

    function GetSteps_AppConfig($Settings)
    {
        ##################################################
        #Update the app's configuration files
        ##################################################
        #set json config location in appsettings.Production.json
        Step_AppSettingsProperty -SitePath $settings.SitePath -Section 'WebJEA' -Name 'ConfigFile' `
            -Value "$($settings.ScriptsPath)\config.json"

        #Kestrel listeners: the app reads these at startup (0 disables a listener).
        #[int]$null is 0, so a missing/null port key disables that listener.
        Step_AppSettingsProperty -SitePath $settings.SitePath -Section 'WebJEA' -Name 'HttpPort' `
            -Value ([int]$settings.HttpPort)
        Step_AppSettingsProperty -SitePath $settings.SitePath -Section 'WebJEA' -Name 'HttpsPort' `
            -Value ([int]$settings.HttpsPort)
        Step_AppSettingsProperty -SitePath $settings.SitePath -Section 'WebJEA' -Name 'CertThumbprint' `
            -Value ([string]$settings.CertThumbprint)

        #Host filtering replaces IIS host-header bindings: requests for names not in this
        #list get 400. localhost + machine name keep the smoke probe and local
        #troubleshooting working. AllowedHosts is a TOP-LEVEL key, not under WebJEA.
        $allowedHosts = (@($settings.SiteFQDNs) + @($env:COMPUTERNAME, 'localhost') | Select-Object -Unique) -join ';'
        @{
            Description = "appsettings.Production.json: AllowedHosts = $allowedHosts"
            TestScript  = {
                $path = "$($settings.SitePath)\appsettings.Production.json"
                if (-not (Test-Path $path)) { return $false }
                try { $json = Get-Content -Path $path -Raw | ConvertFrom-Json } catch { return $false }
                return ($json.AllowedHosts -eq $allowedHosts)
            }.GetNewClosure()
            SetScript   = {
                $path = "$($settings.SitePath)\appsettings.Production.json"
                $json = if (Test-Path $path) { Get-Content -Path $path -Raw | ConvertFrom-Json }
                else { [pscustomobject]@{} }
                if (-not $json.PSObject.Properties['AllowedHosts'])
                {
                    $json | Add-Member -NotePropertyName 'AllowedHosts' -NotePropertyValue $null
                }
                $json.AllowedHosts = $allowedHosts
                $json | ConvertTo-Json -Depth 10 | Set-Content -Path $path -Encoding UTF8
            }.GetNewClosure()
        }

        #set nlog log location in nlog.config in iis site
        @{
            Description = 'Setting log file location in nlog.config'
            Module      = 'xXMLConfigFile'
            Resource    = 'XMLConfigFile'
            Property    = @{
                Ensure      = 'Present'
                ConfigPath  = "$($settings.SitePath)\nlog.config"
                XPath       = "/nlog/targets/target[@name='file']/target"
                isAttribute = $true
                Name        = 'fileName'
                Value       = "$($settings.LogPath)\$($settings.LogFile)"
            }
        }

        #set nlog usage file location in nlog.config in iis site
        @{
            Description = 'Setting usage log file location in nlog.config'
            Module      = 'xXMLConfigFile'
            Resource    = 'XMLConfigFile'
            Property    = @{
                Ensure      = 'Present'
                ConfigPath  = "$($settings.SitePath)\nlog.config"
                XPath       = "/nlog/targets/target[@name='fileSummary']/target"
                isAttribute = $true
                Name        = 'fileName'
                Value       = "$($settings.LogPath)\$($settings.LogUsageFile)"
            }
        }
        #TODO assign permissions to scripts folder?
    }

    function GetSteps_PowerShell
    {

        @{Description = '***** Configuring PowerShell for DSC *****' }
        #Package Management and NuGet provider are required to install the other DSC resource modules from the PowerShell Gallery, so ensure they're installed before trying to run any DSC resources.
        @{
            Description = 'NuGet Package Provider >=2.8.5.201 is installed'
            TestScript  = {
                $verboseMemory = $VerbosePreference
                $VerbosePreference = 'SilentlyContinue'
                $provider = Get-PackageProvider -Name NuGet -ErrorAction SilentlyContinue
                $VerbosePreference = $verboseMemory
                return $provider -and ($provider.Version -ge [Version]'2.8.5.201')
            }
            SetScript   = {
                $verboseMemory = $VerbosePreference
                $VerbosePreference = 'SilentlyContinue'
                Install-PackageProvider -Name NuGet -Force -MinimumVersion '2.8.5.201'
                #reload packagemanagement to ensure the new provider is available in the current session
                # Remove-Module PackageManagement -Force
                # Import-Module PackageManagement -Force
                $VerbosePreference = $verboseMemory
            }
        }

        #WinRM is required for DSC to work, so ensure it's configured before trying to run any DSC resources.
        # WinRM service startup type is Automatic so it survives reboots
        @{ Description = 'WinRM service startup type is Automatic'
            TestScript = { (Get-Service -Name WinRM).StartType -eq 'Automatic' }
            SetScript  = { Set-Service -Name WinRM -StartupType Automatic }
        }

        # WinRM service is running
        @{ Description = 'WinRM service is running'
            TestScript = { (Get-Service -Name WinRM).Status -eq 'Running' }
            SetScript  = { Start-Service -Name WinRM }
        }

        # At least one WinRM listener is configured
        @{ Description = 'WinRM has at least one listener configured'
            TestScript = { (Get-ChildItem WSMan:\localhost\Listener | Measure-Object).Count -gt 0 }
            SetScript  = { winrm quickconfig -quiet }
        }

        # # WinRM is listening on IPv6 — only checked when IPv6 is enabled on any adapter
        # [bool]$ipv6Enabled = Get-NetAdapterBinding -ComponentID ms_tcpip6 -ErrorAction SilentlyContinue | Where-Object Enabled
        # if ($ipv6Enabled)
        # {
        #     @{ Description = 'WinRM is listening on IPv6'
        #         TestScript       = {
        #             $null -ne (Get-NetTCPConnection -LocalPort 5985 -State Listen -ErrorAction SilentlyContinue |
        #                     Where-Object { $_.LocalAddress -match ':' })
        #         }
        #         SetScript        = { Restart-Service -Name WinRM }
        #     }
        # } else {
        # }
        #on an IPv4 address (0.0.0.0 means all IPv4 interfaces)

        # WinRM is listening
        @{ Description = 'WinRM is listening on IPv4'
            TestScript = { $null -ne (Get-NetTCPConnection -LocalPort 5985 -State Listen -ErrorAction SilentlyContinue) }
            SetScript  = { Restart-Service -Name WinRM }
        }

        @{
            Description = 'PowerShellGet v2.2.5 installed'
            TestScript  = {
                $module = Get-Module -Name PowerShellGet -ListAvailable | Sort-Object Version -Descending | Select-Object -First 1
                return $module -and ($module.Version -ge [Version]'2.2.5')
            }
            SetScript   = {
                Install-Module -Name PowerShellGet -Force -RequiredVersion '2.2.5'
                #reload PowerShellGet to ensure the new version is available in the current session
                Remove-Module PowerShellGet -Force
                Import-Module PowerShellGet -Force
            }
        }
        @{
            Description = 'PSGallery Installation Policy is Trusted'
            TestScript  = { (Get-PSRepository -Name PSGallery -ErrorAction SilentlyContinue).InstallationPolicy -eq 'Trusted' }
            SetScript   = { Set-PSRepository -Name PSGallery -InstallationPolicy Trusted }
        }

        #Install required modules
        $modules = @(
            'xXMLConfigFile'
            'cUserRightsAssignment'
            # 'WebJEAConfig'
            'DSCR_FileContent'
        )
        foreach ($module in $modules)
        {
            @{
                Description = "PowerShell Module Installed: $module"
                TestScript  = { Get-Module -Name $module -ListAvailable -ErrorAction SilentlyContinue }.GetNewClosure()
                SetScript   = { Install-Module -Name $module -Force }.GetNewClosure()
            }
        }

    }
    function GetSteps_Server
    {
        @{Description = '***** Configuring the Server *****' }

        #add site contents
        Step_CopySiteContent -Source "$($settings.SourcePath)\site" -Destination $settings.SitePath -ServiceName $settings.ServiceName -Label 'Site contents'

        #Copy the scripts to the server
        if ((Test-Path -Path $settings.ScriptsPath -PathType Container) -and
            (Get-ChildItem -Path $settings.ScriptsPath -Recurse | Measure-Object).Count -gt 1)
        {
            @{
                Description = 'Starter scripts copied (skipped, files already exist)'
                TestScript  = { $true }
                SetScript   = { }
            }
        }
        else
        {
            #add starter scripts
            @{
                Description = 'Starter scripts copied'
                Module      = 'PSDesiredStateConfiguration'
                Resource    = 'file'
                Property    = @{
                    Ensure          = 'Present'
                    SourcePath      = $settings.SourcePath + '\Scripts'
                    DestinationPath = $settings.ScriptsPath
                    Recurse         = $true
                    type            = 'Directory'
                    MatchSource     = $true #always copy files to ensure accurate
                    Checksum        = 'SHA-256'
                }
            }
        }

    }
    function GetSteps_Service($Settings)
    {
        @{ Description = '***** Configuring the Windows Service *****' }

        $svcName = $settings.ServiceName
        $exePath = Join-Path $settings.SitePath 'WebJEA.exe'
        $account = $settings.ServiceUserName
        $password = $settings.ServicePassword

        #Grant Logon-as-a-Service directly to the service account (was granted to the
        #IIS APPPOOL\ principal in the IIS era).
        @{
            Description = "$account has Logon as a Service right"
            Module      = 'cUserRightsAssignment'
            Resource    = 'cUserRight'
            Property    = @{
                Ensure    = 'Present'
                Constant  = 'SeServiceLogonRight'
                Principal = $account
            }
        }

        #Serving HTTPS takes more than putting the certificate in LocalMachine\My: that
        #store is readable by everyone, but the private key file behind the certificate is
        #ACL'd to SYSTEM and local Administrators only. That single ACL is the reason a
        #WebJEA service account would otherwise have to be a local administrator - without
        #it Kestrel cannot open the key and the HTTPS listener fails at startup. Grant the
        #account plain Read on that one file instead. Checked on every deploy because
        #renewing a certificate produces a NEW key file with the default permissions again.
        if ([int]$settings.HttpsPort -gt 0 -and $settings.CertThumbprint)
        {
            $thumbprint = $settings.CertThumbprint

            #GetNewClosure() rebinds these scriptblocks to a new dynamic module whose scope
            #chain reaches the global scope but NOT this script's, so the two helpers below
            #cannot be called by name from inside them - the closure dies at run time with
            #CommandNotFoundException. Capture each function's scriptblock in a variable
            #(closures do copy those) and invoke it with &. Same class of GetNewClosure
            #scoping trap as $skipSpn further down.
            $getKeyPath = ${function:GetCertPrivateKeyPath}
            $getAccountSid = ${function:ResolveAccountSid}
            @{
                Description = "$account can read the private key of certificate $thumbprint"
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
                        Write-Warning "Certificate $thumbprint has no private key file in the standard machine key folders (hardware, smart-card or ephemeral provider). Its permissions cannot be managed here - grant $account read access with that provider's own tooling, or the HTTPS listener will fail to start unless the account is a local administrator."
                        return $true
                    }

                    #Read the ACEs as SIDs: the key file's ACL routinely contains entries
                    #for accounts that no longer resolve to a name, and translating those
                    #for comparison would throw mid-enumeration.
                    $sid = & $getAccountSid -Account $account
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
                    $sid = & $getAccountSid -Account $account

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

                #Always pass password= (even empty, for gMSA accounts): sc.exe requires it
                #whenever the account is not LocalSystem, and MUST see it again on `config`
                #whenever the account changes (e.g. password-account -> gMSA on a redeploy) -
                #omitting it there would leave the old credential in place and Finalize's
                #Restart-Service would then fail with a logon error. Windows PowerShell 5.1
                #drops a bare empty-string argument to native commands (shifting the rest of
                #argv), so an empty password is passed as '""' - the string containing two
                #quote chars reaches sc.exe as an empty quoted value. The script forces
                #$PSNativeCommandArgumentPassing = 'Legacy' at the top so this idiom behaves
                #the same under pwsh 7.2+.
                $passwordArg = if ([string]::IsNullOrEmpty($password)) { '""' } else { $password }
                $scArgs = @('binPath=', "`"$exePath`"", 'start=', 'delayed-auto', 'obj=', $account, 'password=', $passwordArg)

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

        #appsettings.Production.json (config location, ports, cert, redirect, allowed
        #hosts) and nlog paths
        GetSteps_AppConfig -Settings $Settings
    }
    function GetSteps_WebJEA($Settings)
    {
        @{ Description = '***** Configuring WebJEA Specific Settings *****' }
        #Update config.json basePath property
        @{
            Description = 'basePath in config.json is set to the scripts folder'
            Module      = 'DSCR_FileContent'
            Resource    = 'JSONFile'
            Property    = @{
                Ensure   = 'Present'
                Path     = "$($settings.ScriptsPath)\config.json"
                Key      = 'basePath'
                Value    = $settings.ScriptsPath
                Encoding = 'ascii'
            }
        }

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
        if ($OnlySections -contains 'All' -or $OnlySections -contains 'PowerShell') { GetSteps_PowerShell }
        if ($OnlySections -contains 'All' -or $OnlySections -contains 'Server') { GetSteps_Server }
        if ($OnlySections -contains 'All' -or $OnlySections -contains 'Service') { GetSteps_Service $Settings }
        if ($OnlySections -contains 'All' -or $OnlySections -contains 'WebJEA') { GetSteps_WebJEA $Settings }
        if ($OnlySections -contains 'All' -or $OnlySections -contains 'Finalize') { GetSteps_Finalize $Settings }
    }
    #endregion Config
    #region Functions
    function ConvertToExpression($Obj)
    {
        if ($null -eq $Obj)
        {
            return '$null'
        }
        else
        {
            $strB = [System.Text.StringBuilder]::new()
            switch ($Obj.GetType().Name)
            {
                'String' { return "'$($Obj)'" }
                'Boolean' { if ($Obj) { return '$true' } else { return '$false' } }
                'Hashtable'
                {
                    $strB.append('@{') | Out-Null
                    foreach ($property in $Obj.keys)
                    {
                        $strB.append("$property = $(ConvertToExpression $Obj.$property); ") | Out-Null
                    }
                    $strB.append('}') | Out-Null
                    return $strB.ToString()
                }
                'Object[]'
                {
                    $strB.append('@(') | Out-Null
                    $ArrayObj = $Obj | ForEach-Object {
                        "$(ConvertToExpression $_)"
                    }
                    $strB.append(($ArrayObj -join ', ')) | Out-Null
                    $strB.append(')') | Out-Null
                    return $strB.ToString()
                }
                default { return $Obj.tostring() }
            }
        }

    }

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
            throw "Could not resolve service account '$Account' to a SID, so its access to the certificate's private key cannot be checked or granted. Verify the account exists and that this machine can reach its domain."
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

    function ValidateSettings( [psobject]$Settings )
    {
        #quick helper function
        function Assert($Condition, $Message) { if (-not $Condition) { throw $Message } }

        #Reject IIS-era settings files outright: this version hosts as a Windows service.
        $retiredKeys = @('SiteName', 'AppPoolName', 'AppPoolUserName', 'AppPoolPassword',
            'AppPoolLoadUserProfile', 'DisableDefaultWebsite', 'AppName', 'ParentSiteName')
        $foundRetired = @($retiredKeys | Where-Object { $Settings.PSObject.Properties[$_] })
        Assert ($foundRetired.Count -eq 0) ("Settings file contains retired IIS-era keys: $($foundRetired -join ', '). " +
            'This version of WebJEA installs as a Windows service, not an IIS site, and sub-application ' +
            'installs are no longer supported on Windows (use containers - see docs/docker.md). ' +
            'Start from the new settings.template.json: ServiceName/ServiceUserName replace the site/app-pool keys. ' +
            'See docs/deployment-migration.md.')

        #Remove trailing slashes from paths to avoid problems later
        $settings.SitePath = $settings.SitePath.TrimEnd('\')
        $settings.ScriptsPath = $settings.ScriptsPath.TrimEnd('\')
        $settings.LogPath = $settings.LogPath.TrimEnd('\')

        #Default the service name
        if (-not $settings.PSObject.Properties['ServiceName'] -or [string]::IsNullOrWhiteSpace($settings.ServiceName))
        {
            $settings | Add-Member -NotePropertyName ServiceName -NotePropertyValue 'WebJEA' -Force
        }

        #verify the service account (same gMSA-vs-password rules as before)
        Assert ($settings.ServiceUserName -match '^[^\\]+\\[^\\]+$') "ServiceUserName '$($settings.ServiceUserName)' is not in the correct format. It should be in the format 'domain\\username' or 'machinename\\username'."
        if ($settings.ServicePassword)
        {
            Assert ($settings.ServiceUserName -notmatch '\$$') "ServiceUserName '$($settings.ServiceUserName)' appears to be a gMSA account (ends with $) but a password is provided. Remove the password for gMSA accounts or provide a valid password for regular user accounts."
        }
        else
        {
            Assert ($settings.ServiceUserName -match '\$$') "ServiceUserName '$($settings.ServiceUserName)' appears to be a regular user account but no password is provided. Provide a password, or use a gMSA account (trailing $, empty password)."
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
                Assert ($null -ne $userobj) "ServiceUserName '$($settings.ServiceUserName)' does not appear to be a valid gMSA account. Provide a valid gMSA or a regular user account with a password."
            }
        }

        #Ports: at least one enabled; https requires a cert ([int]$null is 0)
        $httpPort = [int]$settings.HttpPort
        $httpsPort = [int]$settings.HttpsPort
        Assert ($httpPort -gt 0 -or $httpsPort -gt 0) 'At least one of HttpPort or HttpsPort must be set to a port number.'
        Assert (-not ($httpsPort -gt 0 -and -not $settings.CertThumbprint)) 'HttpsPort is enabled but CertThumbprint is not provided.'

        #paths must differ (unchanged)
        Assert ($settings.SitePath -ne $settings.ScriptsPath) 'SitePath and ScriptsPath cannot be the same. Please update the settings file to specify different paths.'
        Assert ($settings.SitePath -ne $settings.LogPath) 'SitePath and LogPath cannot be the same. Please update the settings file to specify different paths.'
        Assert ($settings.LogFile -ne $settings.LogUsageFile) 'LogFile and LogUsageFile cannot be the same. Please update the settings file to specify different paths.'

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

        #Certificate checks (unchanged from the current file: thumbprint exists in
        #LocalMachine\My and every FQDN is covered by subject/SAN or wildcard SAN)
        if ($settings.CertThumbprint -and $httpsPort -gt 0)
        {
            $cert = Get-ChildItem -Path cert:\LocalMachine\My\$($settings.CertThumbprint) -ErrorAction SilentlyContinue
            Assert ($cert) "CertThumbprint '$($settings.CertThumbprint)' not found in LocalMachine\My store."
            #A public-key-only certificate imports and validates cleanly here but cannot
            #complete a TLS handshake, and there is no key for the private-key ACL step to
            #grant. Catch it now instead of at first service start.
            Assert ($cert.HasPrivateKey) "Certificate '$($settings.CertThumbprint)' is in LocalMachine\My but has no private key, so it cannot serve HTTPS. Re-import it from a PFX that includes the private key."
            $certNames = @($cert.DnsNameList.Unicode)
            foreach ($fqdn in $allFQDNs)
            {
                $wildcardName = if ($fqdn.Contains('.')) { '*.' + $fqdn.Split('.', 2)[1] } else { $null }
                Assert ($certNames -contains $fqdn -or ($wildcardName -and $certNames -contains $wildcardName)) "Host name '$fqdn' is not covered by the certificate with thumbprint '$($settings.CertThumbprint)' (certificate names: $($certNames -join ', '))."
            }
        }
    }

    function InvokeStep([psobject]$Step, [switch]$TestOnly)
    {
        if (-not $step.description)
        {
            Write-Host "Error with configuration: $($Step | ConvertTo-Json -Depth 2)" -ForegroundColor Yellow
            return
        }
        if ($step.description -and -not ($step.testscript -or $step.setscript -or $step.module))
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
        elseif ($Step.Module -and $Step.Resource)
        {
            ##### DSC Resource
            #Write-Host -ForegroundColor black -BackgroundColor yellow
            Write-Verbose "Invoke-DscResource -Module $($Step.Module) -Name $($Step.Resource) -Property $(ConvertToExpression $Step.Property) -Method Test"
            # Write-Host (Invoke-DscResource -Module $Step.Module -Name $Step.Resource -Property $Step.Property -Method Get -Verbose:$false | ConvertTo-Json -Depth 1)
            $verboseMemory = $VerbosePreference
            $VerbosePreference = 'SilentlyContinue'
            $test = Invoke-DscResource -Module $Step.Module -Name $Step.Resource -Property $Step.Property -Method Test
            if (-not $test.InDesiredState -and -not $TestOnly)
            {
                Write-Host '  [SET] not in desired state. Updating...'
                $set = Invoke-DscResource -Module $Step.Module -Name $Step.Resource -Property $Step.Property -Method Set

                #After setting, test again to confirm it reached the desired state
                $test = Invoke-DscResource -Module $Step.Module -Name $Step.Resource -Property $Step.Property -Method Test
            }
            $VerbosePreference = $verboseMemory
            $hasTest = $true
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
        elseif (-not $Step.TestScript -and -not $Step.SetScript -and -not $Step.Module -and -not $Step.Resource)
        {
            #Empty step, do nothing
            Write-Verbose 'No TestScript/SetScript or Module/Resource specified for this step. Skipping.'
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

    #HTTP->HTTPS redirect is automatic (HttpPort + HttpsPort + CertThumbprint all set) -
    #there is no separate opt-in setting. If HttpPort is enabled but the other two
    #aren't both set, HTTP is served directly with NO redirect to HTTPS; that's a valid
    #HTTP-only install, but it's also an easy accident, so require an explicit opt-in.
    if (-not $AllowHttpWithoutRedirect)
    {
        $httpEnabled = [int]$Settings.HttpPort -gt 0
        $wouldRedirect = ([int]$Settings.HttpsPort -gt 0) -and $Settings.CertThumbprint
        if ($httpEnabled -and -not $wouldRedirect)
        {
            throw @"
HttpPort is enabled ($($Settings.HttpPort)) but HttpsPort and CertThumbprint are not both set, so HTTP requests will be served directly with no redirect to HTTPS.
HTTP is only ever redirected to HTTPS automatically, when HttpPort, HttpsPort, and CertThumbprint are all configured - there is no separate redirect setting.
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