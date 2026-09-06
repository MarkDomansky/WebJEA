<#
.SYNOPSIS
    Interactive step-by-step manual test guide for deploying WebJEA to each supported
    target environment.

.DESCRIPTION
    Walks a tester through setting up and verifying WebJEA on:

      1. ServerWithIIS    - Windows Server that is already running IIS
                            (verifies the Deploy.ps1 port-conflict guard + coexistence)
      2. ServerDesktop    - Windows Server (Desktop Experience), no IIS
      3. ServerCore       - Windows Server Core (shell only), no IIS
      4. WindowsContainer - Windows container under Hyper-V isolation on Windows 11
                            (Docker Desktop, Windows-container mode)
      5. LinuxContainer   - Linux container on Docker Desktop (WSL2)

    Each step prints the exact commands to run and a verification checklist.
    Press Enter to advance, Q to quit. Run with -NoPause to print an entire
    scenario without stopping (useful for capturing to a file with *> guide.txt).

    The guide itself runs anywhere PowerShell 5.1+ is available (including the
    target servers); the commands inside the steps say where they must be run.

.PARAMETER Scenario
    One or more scenarios to display, or 'All'. If omitted, an interactive menu
    is shown.

.PARAMETER NoPause
    Print all steps of the selected scenario(s) without pausing between them.

.PARAMETER List
    List the available scenarios and exit.

.EXAMPLE
    .\Show-ManualTestGuide.ps1
    Interactive menu, then paged step-by-step guide.

.EXAMPLE
    .\Show-ManualTestGuide.ps1 -Scenario ServerCore

.EXAMPLE
    .\Show-ManualTestGuide.ps1 -Scenario All -NoPause *> webjea-test-guide.txt
#>
#Requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter()]
    [ValidateSet('ServerWithIIS', 'ServerDesktop', 'ServerCore', 'WindowsContainer', 'LinuxContainer', 'All')]
    [string[]]$Scenario,

    [Parameter()]
    [switch]$NoPause,

    [Parameter()]
    [switch]$List
)

$ErrorActionPreference = 'Stop'

#region Shared step builders (Windows-service scenarios share most of their steps)

function Step_BuildPackage
{
    @{
        Title  = 'Build (or download) the release package'
        Body   = @(
            'On your dev workstation, from the repository root. The build needs the'
            '.NET 10 SDK, Node.js/npm, and PowerShell 7. The publish is SELF-CONTAINED'
            'win-x64, so the target server does NOT need a .NET runtime installed.'
        )
        Code   = @(
            'cd <repo-root>'
            'pwsh -File .github\workflows\build.ps1 -OutputPath .\build-output -CreateZip'
        )
        Note   = 'Alternatively, download a published release zip from GitHub Releases and unzip it.'
        Verify = @(
            'build-output\ contains Deploy.ps1, settings.template.json, site\WebJEA.exe, scripts\'
            'A zip of the package was created (if -CreateZip was used)'
        )
    }
}

function Step_ServerPrereqs([bool]$Core, [bool]$ExpectIIS)
{
    $body = @()
    if ($Core)
    {
        $body += 'Target: Windows Server 2019+ installed WITHOUT Desktop Experience (Server Core).'
        $body += 'Work at the console (cmd -> powershell), or remote in from your workstation:'
    }
    elseif ($ExpectIIS)
    {
        $body += 'Target: Windows Server 2019+ (Desktop Experience) with the Web Server (IIS)'
        $body += 'role installed and at least one site bound to ports 80/443 (the Default Web'
        $body += 'Site is fine). This scenario tests coexistence, so leave IIS running.'
    }
    else
    {
        $body += 'Target: Windows Server 2019+ (Desktop Experience) with NO IIS role installed.'
    }
    $body += ''
    $body += 'Requirements on the target:'
    $body += '  - Local admin access, and internet access to the PowerShell Gallery'
    $body += '    (Deploy.ps1 installs the NuGet provider, PowerShellGet 2.2.5, and the'
    $body += '    xXMLConfigFile, cUserRightsAssignment, DSCR_FileContent modules).'
    $body += '  - Domain-joined if you want to test Kerberos; a workgroup box works for a'
    $body += '    quick lab (local service account, NTLM only).'
    $body += '  - PowerShell 7 (Deploy.ps1 declares #Requires -Version 7.0).'

    $code = @()
    if ($Core)
    {
        $code += '# From your workstation (or skip and use the server console directly):'
        $code += 'Enter-PSSession -ComputerName <server> -Credential (Get-Credential)'
        $code += ''
    }
    $code += '# Install PowerShell 7 (run from Windows PowerShell 5.1 on the server):'
    $code += 'iex "& { $(irm https://aka.ms/install-powershell.ps1) } -UseMSI -Quiet"'
    $code += '# or on a server with winget:'
    $code += 'winget install --id Microsoft.PowerShell --source winget'

    $verify = @('pwsh -v reports 7.x on the target server')
    if ($ExpectIIS)
    {
        $verify += 'Get-Service W3SVC shows Running, and http://localhost/ serves the IIS site'
    }
    else
    {
        $verify += "Get-Service W3SVC errors with 'Cannot find any service' (IIS absent)"
    }
    if ($Core)
    {
        $verify += 'You have a working console or Enter-PSSession session on the server'
    }

    @{
        Title  = 'Prepare the target server'
        Body   = $body
        Code   = $code
        Verify = $verify
    }
}

function Step_DnsAndCert([bool]$Core)
{
    $body = @(
        'Pick the FQDN the site will answer on (settings.json SiteFQDNs), make it resolve'
        'to the server from your test client, and stage a certificate for HTTPS.'
        'Deploy.ps1 validates that the certificate in LocalMachine\My actually covers'
        'the FQDN, so create the cert before running it.'
    )
    if ($Core)
    {
        $body += ''
        $body += 'Server Core has no browser - you will do the browser-based verification from'
        $body += 'a second machine, so set up DNS/hosts on THAT machine.'
    }
    @{
        Title  = 'Choose the site FQDN, DNS, and certificate'
        Body   = $body
        Code   = @(
            '# DNS: create an A record for webjea-test.corp.example.com, OR add a hosts'
            '# entry on the client machine you will browse from (elevated):'
            'Add-Content C:\Windows\System32\drivers\etc\hosts "<server-ip>  webjea-test.corp.example.com"'
            ''
            '# Self-signed certificate for testing (on the TARGET server, elevated):'
            "`$cert = New-SelfSignedCertificate -DnsName 'webjea-test.corp.example.com' -CertStoreLocation Cert:\LocalMachine\My"
            '$cert.Thumbprint    # record this for settings.json CertThumbprint'
        )
        Note   = 'HTTP-only test: set HttpsPort to 0 in settings.json and skip the certificate - HTTP is only ever redirected to HTTPS automatically when HttpPort, HttpsPort, and CertThumbprint are all set, so leaving HttpsPort at 0 is enough. Deploy.ps1 will refuse an HTTP-only install unless you pass -AllowHttpWithoutRedirect. Browsers will warn on a self-signed cert - expected in a lab.'
        Verify = @(
            'From the test client: ping/nslookup of the FQDN reaches the server'
            'The certificate thumbprint is recorded'
        )
    }
}

function Step_ServiceAccount
{
    @{
        Title  = 'Create the service account'
        Body   = @(
            'A gMSA is the recommended production account. For a quick lab you can use a'
            'domain user with a password, or a local user on a workgroup server.'
            'Deploy.ps1 grants the Logon-as-a-Service right itself. For Windows'
            'authentication the account needs READ access to AD to resolve groups.'
        )
        Code   = @(
            '# Option A - gMSA (from a domain-admin session; requires a KDS root key):'
            "New-ADServiceAccount -Name webjea-svc -DNSHostName webjea-svc.corp.example.com ``"
            "    -PrincipalsAllowedToRetrieveManagedPassword '<SERVERNAME>$'"
            '# then on the target server (needs the RSAT ActiveDirectory module):'
            'Install-ADServiceAccount -Identity webjea-svc'
            'Test-ADServiceAccount -Identity webjea-svc    # expect True'
            ''
            '# Option B - local lab account (workgroup server):'
            "net user webjeasvc 'P@ssw0rd!123' /add"
        )
        Note   = "settings.json: gMSA -> ServiceUserName 'DOMAIN\webjea-svc$' with ServicePassword ''; user account -> 'DOMAIN\user' or '<COMPUTERNAME>\webjeasvc' plus the password. Deploy.ps1 enforces exactly these combinations."
        Verify = @(
            'gMSA: Test-ADServiceAccount returns True on the target server'
            'ServiceUserName/ServicePassword values decided and recorded'
        )
    }
}

function Step_CopyAndSettings([int]$HttpPort, [int]$HttpsPort, [string]$PortNote)
{
    $body = @(
        'Copy the build output to the server, then create settings.json from the'
        'template and fill in your values:'
        ''
        '  SiteFQDNs           = the FQDN from the DNS/cert step, as a one-element array'
        '  ServiceUserName     = account from the previous step (ServicePassword if not gMSA)'
        "  HttpPort            = $HttpPort"
        "  HttpsPort           = $HttpsPort"
        '  CertThumbprint      = thumbprint from the cert step (when HttpsPort > 0;'
        '                        HTTP is then redirected to HTTPS automatically)'
        '  SitePath            = C:\inetpub\webjea   (app binaries; default is fine)'
        '  ScriptsPath         = C:\webjea           (config.json + your .ps1 scripts)'
        '  LogPath             = C:\webjea           (webjea.log / webjea-usage.log)'
    )
    if ($PortNote) { $body += ''; $body += $PortNote }
    @{
        Title  = 'Copy the package and create settings.json'
        Body   = $body
        Code   = @(
            '# From the workstation:'
            'Copy-Item .\build-output \\<server>\c$\deploy -Recurse'
            '# On the server:'
            'Copy-Item C:\deploy\settings.template.json C:\deploy\settings.json'
            'notepad C:\deploy\settings.json    # Server Core: use  pwsh -c "code" alternatives or edit remotely'
            ''
            '# Sanity-check the file parses and shows your values:'
            'pwsh -File C:\deploy\Deploy.ps1 -SettingsFile C:\deploy\settings.json -OnlyReturnSettings'
        )
        Verify = @(
            '-OnlyReturnSettings prints the settings object with your edited values'
            'The old IIS-era keys (SiteName, AppPoolName, ...) are NOT present - this version rejects them'
        )
    }
}

function Step_TestOnlyDeploy
{
    @{
        Title  = 'Dry run: Deploy.ps1 -TestOnly'
        Body   = @(
            'Run the deployment in test-only mode first. It validates settings (account'
            'format, cert coverage, port rules) and reports every step as [OK] /'
            '[CHANGE] / [SKIP] without changing the server.'
        )
        Code   = @(
            '# Elevated PowerShell 7 session on the server:'
            'Set-Location C:\deploy'
            'pwsh -File .\Deploy.ps1 -SettingsFile .\settings.json -TestOnly'
        )
        Verify = @(
            'Validation passes (no thrown error about account/cert/ports)'
            'Steps report [CHANGE] (fresh box) - nothing was actually modified'
        )
    }
}

function Step_RealDeploy([string[]]$ExtraNotes)
{
    $body = @(
        'Run the real deployment. It installs PSGallery modules, configures WinRM,'
        'copies the site to SitePath and starter scripts to ScriptsPath, creates the'
        'WebJEA Windows service, opens firewall ports, writes'
        'appsettings.Production.json, registers HTTP/<fqdn> SPNs, then starts the'
        'service and smoke-probes every FQDN/port combination.'
    )
    if ($ExtraNotes) { $body += ''; $body += $ExtraNotes }
    @{
        Title  = 'Deploy'
        Body   = $body
        Code   = @(
            'pwsh -File .\Deploy.ps1 -SettingsFile .\settings.json'
            ''
            '# If SPN registration fails (no AD write access) or setspn.exe is missing'
            '# and you accept NTLM-only authentication:'
            'pwsh -File .\Deploy.ps1 -SettingsFile .\settings.json -SkipSpnCheck'
        )
        Verify = @(
            'Every step ends [OK] / [DONE]; the run finishes without a thrown error'
            "Final step 'Deployed site responds on its configured ports' passes"
        )
    }
}

function Step_VerifyService([int]$HttpPort, [int]$HttpsPort, [bool]$Core)
{
    $fqdn = 'webjea-test.corp.example.com'
    $httpsUrl = "https://${fqdn}$(if ($HttpsPort -ne 443) { ":$HttpsPort" })/"
    $httpUrl = "http://${fqdn}$(if ($HttpPort -ne 80) { ":$HttpPort" })/"
    $body = @(
        'Confirm the service end to end. The starter config permits the local'
        'Administrators group (permittedgroups ".\Administrators"), so test with an'
        'account in that group or edit C:\webjea\config.json permittedgroups first.'
    )
    if ($Core)
    {
        $body += ''
        $body += 'Run the curl checks on the server itself; do the browser checks from your'
        $body += 'second machine (Server Core has no browser).'
    }
    @{
        Title  = 'Verify the deployment'
        Body   = $body
        Code   = @(
            '# On the server:'
            'Get-Service WebJEA                      # Running, StartType Automatic (delayed)'
            "Get-NetFirewallRule -DisplayName 'WebJEA*'"
            "curl.exe -k -s -o NUL -w `"%{http_code}``n`" --negotiate -u : $httpsUrl"
            ''
            '# Legacy URLs must permanently redirect (301/308) to the new endpoint:'
            "curl.exe -k -s -o NUL -w `"%{http_code} -> %{redirect_url}``n`" $($httpUrl)webjea/"
            "curl.exe -k -s -o NUL -w `"%{http_code} -> %{redirect_url}``n`" $($httpUrl)default.aspx"
            ''
            '# Logs:'
            'Get-Content C:\webjea\webjea.log -Tail 20'
            'Get-Content C:\webjea\webjea-usage.log -Tail 5   # exists after you run a script'
        )
        Verify = @(
            "Browser: $httpUrl redirects to HTTPS (automatic - HttpPort/HttpsPort/CertThumbprint are all set)"
            "Browser: $httpsUrl signs you in and shows the dashboard"
            "The 'Overview' command loads; its onload script INTENTIONALLY raises errors"
            '  (12/0, Write-Error) - the page must still return HTTP 200 and render the'
            '  error text inline (errors no longer change the HTTP status)'
            'Run the Overview/validate.ps1 command; output renders; usage log gains a line'
            'On the client, klist shows an HTTP/<fqdn> ticket = Kerberos (absent = NTLM'
            '  fallback; expected with -SkipSpnCheck or a non-domain account)'
            'Legacy /webjea/, /default.aspx, /command.aspx return 301/308 redirects'
        )
    }
}

function Step_RerunAndTests
{
    @{
        Title  = 'Idempotency re-run and (optional) automated integration tests'
        Body   = @(
            'A second deploy over a healthy install must make no changes - this validates'
            'the desired-state logic. Then, optionally, point the automated integration'
            'suite at the site from your workstation.'
        )
        Code   = @(
            '# On the server - expect every testable step to report [OK]:'
            'pwsh -File .\Deploy.ps1 -SettingsFile .\settings.json'
            ''
            '# Optional, from the repo on your workstation (see Test\Integration\config.json):'
            'Set-Location <repo-root>\Test\Integration'
            '.\Invoke-IntegrationTests.ps1'
        )
        Verify = @(
            'Re-run reports [OK] on all state-checked steps (copy/restart steps always run)'
            'Integration tests pass (if run)'
        )
    }
}

function Step_ServerCleanup
{
    @{
        Title  = 'Cleanup (optional)'
        Body   = @('To return the server to a clean state after testing:')
        Code   = @(
            'Stop-Service WebJEA'
            'sc.exe delete WebJEA'
            'Remove-Item C:\inetpub\webjea, C:\webjea, C:\deploy -Recurse -Force'
            "Get-NetFirewallRule -DisplayName 'WebJEA*' | Remove-NetFirewallRule"
            '# Remove the test cert (thumbprint from settings.json) and any hosts entries:'
            'Remove-Item Cert:\LocalMachine\My\<thumbprint>'
        )
        Verify = @('Service, folders, firewall rules, and test cert are gone')
    }
}

#endregion Shared step builders

#region Container step builders

function Step_EntraAppRegistration([string]$RedirectUri)
{
    @{
        Title  = 'Create the Entra ID app registration'
        Body   = @(
            'Containers in this scenario use Entra ID authentication (Windows/Negotiate'
            'auth inside a container needs a gMSA credential spec on a domain-joined'
            'container HOST - not applicable to a typical Windows 11 / Docker Desktop lab).'
            ''
            'Follow docs/entra.md "App registration setup":'
            '  1. New app registration in your tenant; record Tenant ID and Client ID.'
            "  2. Add a Web redirect URI: $RedirectUri"
            '  3. Enable ID tokens; add the groups claim (or app roles).'
            '  4. Create a client secret; record its value.'
        )
        Verify = @(
            'Tenant ID, Client ID, and client secret recorded'
            "Redirect URI $RedirectUri saved on the registration"
        )
    }
}

function Step_ContainerContent([string]$BasePath)
{
    @{
        Title  = 'Prepare the scripts and logs folders'
        Body   = @(
            'The compose files mount .\scripts and .\logs from the docker\ folder into'
            'the container. Seed scripts\ with the starter config and scripts, then fix'
            'basepath and permittedgroups for the container environment.'
        )
        Code   = @(
            'cd <repo-root>\docker'
            'mkdir scripts, logs'
            'Copy-Item ..\ReleaseFiles\scripts\* .\scripts\'
        )
        Note   = ('Edit scripts\config.json: set "basepath": "{0}" and change permittedgroups from ".\\Administrators" to Entra values - a group object ID, app role value, or user UPN, or "*" for any signed-in user (lab only). See docs/entra.md Authorization modes.' -f $BasePath)
        Verify = @(
            'docker\scripts contains config.json, overview.ps1, validate.ps1'
            "config.json basepath is $BasePath"
            'permittedgroups contains your Entra group ID / UPN (or "*" for the lab)'
        )
    }
}

function Step_ContainerVerify([string]$Compose, [string]$OsNote)
{
    $composeArg = if ($Compose) { "-f $Compose " } else { '' }
    @{
        Title  = 'Verify the container'
        Body   = @(
            'Confirm the app serves, authenticates via Entra, and runs scripts.'
            $OsNote
        )
        Code   = @(
            'docker ps                                   # webjea container Up'
            "docker compose $($composeArg)logs -f webjea    # startup log; Ctrl+C to detach"
            'curl.exe -s -o NUL -w "%{http_code}`n" http://localhost:8080/   # 302 to Entra sign-in'
            'curl.exe -s -o NUL -w "%{http_code} -> %{redirect_url}`n" http://localhost:8080/webjea/'
        )
        Verify = @(
            'Browser: http://localhost:8080/ redirects to login.microsoftonline.com and'
            '  signs you in, landing on the dashboard'
            'The Overview command runs; error/verbose/warning formatting renders'
            'Legacy /webjea/ URL returns a permanent redirect (301/308)'
            '.\logs on the host gains webjea.log (and webjea-usage.log after a run)'
            'Restart test: docker compose restart -> site returns, session is invalidated'
            '  (in-container session state is ephemeral - expected)'
        )
    }
}

#endregion Container step builders

#region Scenario definitions

function Get-Scenarios
{
    $scenarios = [ordered]@{}

    $scenarios['ServerWithIIS'] = @{
        Name    = 'Windows Server already running IIS'
        Summary = 'Port-conflict guard fires on 80/443, then coexistence on alternate ports.'
        Steps   = @(
            @{
                Title = 'What this scenario tests'
                Body  = @(
                    'This version of WebJEA is a self-hosted Kestrel Windows service - it no'
                    'longer installs into IIS. On a server where IIS is serving sites, the'
                    'deployment must:'
                    '  1. REFUSE to deploy onto ports IIS is bound to (a clear, actionable error'
                    '     is the expected/passing result), and'
                    '  2. Deploy cleanly alongside IIS on non-conflicting ports, leaving the'
                    '     existing IIS sites untouched.'
                )
            }
            Step_BuildPackage
            (Step_ServerPrereqs -Core:$false -ExpectIIS:$true)
            (Step_DnsAndCert -Core:$false)
            Step_ServiceAccount
            (Step_CopyAndSettings -HttpPort 80 -HttpsPort 443 -PortNote 'Deliberately start with 80/443 - the SAME ports IIS is using - to exercise the conflict guard.')
            @{
                Title  = 'Negative test: deploy onto the IIS ports and expect a refusal'
                Body   = @(
                    'With HttpPort 80 / HttpsPort 443 while IIS holds those bindings, the deploy'
                    'must stop BEFORE changing anything.'
                )
                Code   = @(
                    '# Elevated PowerShell 7 on the server:'
                    'Set-Location C:\deploy'
                    'pwsh -File .\Deploy.ps1 -SettingsFile .\settings.json'
                )
                Verify = @(
                    'The script throws: message names the conflicting port(s), explains the move'
                    '  to a self-hosted service, points at docs/deployment-migration.md and'
                    '  docs/docker.md, and mentions -IgnoreExistingIIS'
                    'No WebJEA service was created (Get-Service WebJEA errors)'
                    'IIS sites still serve normally'
                )
            }
            @{
                Title  = 'Reconfigure for coexistence and deploy'
                Body   = @(
                    'Move WebJEA to free ports and deploy again. The guard only fires on actual'
                    'conflicts, so no -IgnoreExistingIIS is needed once the ports differ.'
                    ''
                    'Edit C:\deploy\settings.json:'
                    '  HttpPort  = 8080'
                    '  HttpsPort = 8443   (redirects 8080 -> the HTTPS port automatically,'
                    '                      since CertThumbprint is already set)'
                )
                Code   = @(
                    'pwsh -File .\Deploy.ps1 -SettingsFile .\settings.json -TestOnly   # review'
                    'pwsh -File .\Deploy.ps1 -SettingsFile .\settings.json'
                )
                Note   = '-IgnoreExistingIIS exists for servers where IIS is installed but you have confirmed the bindings do not really conflict (or W3SVC is stopped/disabled). Do NOT use it while IIS actively holds the same ports - HTTP.sys and Kestrel would fight over them.'
                Verify = @(
                    'Deploy completes without the IIS guard firing'
                    'Final smoke probe passes on 8080/8443'
                )
            }
            (Step_VerifyService -HttpPort 8080 -HttpsPort 8443 -Core:$false)
            @{
                Title  = 'Confirm IIS is unaffected'
                Body   = @('The original IIS sites must still work exactly as before.')
                Code   = @(
                    'curl.exe -s -o NUL -w "%{http_code}`n" http://localhost/    # IIS site'
                    'Get-Service W3SVC                                           # still Running'
                )
                Verify = @(
                    'IIS default site still answers on 80/443'
                    'WebJEA answers on 8080/8443 at the same time'
                )
            }
            Step_RerunAndTests
            Step_ServerCleanup
        )
    }

    $scenarios['ServerDesktop'] = @{
        Name    = 'Windows Server (Desktop Experience), no IIS'
        Summary = 'The mainline clean install: full deploy + end-to-end verification.'
        Steps   = @(
            @{
                Title = 'What this scenario tests'
                Body  = @(
                    'The mainline path: a clean Windows Server with a GUI and no IIS. Deploy.ps1'
                    'should take the box from bare OS (plus PowerShell 7) to a running,'
                    'authenticated WebJEA service in one run.'
                )
            }
            Step_BuildPackage
            (Step_ServerPrereqs -Core:$false -ExpectIIS:$false)
            (Step_DnsAndCert -Core:$false)
            Step_ServiceAccount
            (Step_CopyAndSettings -HttpPort 80 -HttpsPort 443 -PortNote $null)
            Step_TestOnlyDeploy
            (Step_RealDeploy -ExtraNotes $null)
            (Step_VerifyService -HttpPort 80 -HttpsPort 443 -Core:$false)
            Step_RerunAndTests
            Step_ServerCleanup
        )
    }

    $scenarios['ServerCore'] = @{
        Name    = 'Windows Server Core (shell only), no IIS'
        Summary = 'Headless install: remoting/console only, RSAT-absent SPN path, remote verification.'
        Steps   = @(
            @{
                Title = 'What this scenario tests'
                Body  = @(
                    'Server Core has no browser, no RSAT by default, and often no setspn.exe.'
                    'This scenario proves the deploy works entirely from a shell, that the'
                    'SPN/RSAT guards degrade to warnings instead of crashing, and that the site'
                    'can be verified from a second machine.'
                )
            }
            Step_BuildPackage
            (Step_ServerPrereqs -Core:$true -ExpectIIS:$false)
            (Step_DnsAndCert -Core:$true)
            Step_ServiceAccount
            (Step_CopyAndSettings -HttpPort 80 -HttpsPort 443 -PortNote 'No notepad on some Core installs - edit settings.json on your workstation and copy it over, or use pwsh: (Get-Content ...) -replace ... | Set-Content ...')
            Step_TestOnlyDeploy
            (Step_RealDeploy -ExtraNotes @(
                'Server Core specifics to observe:'
                '  - Without RSAT, Get-ADServiceAccount is unavailable: gMSA validation degrades'
                '    to a WARNING (format checks only) instead of failing.'
                '  - Without setspn.exe, the SPN steps WARN that Kerberos cannot be verified'
                '    instead of crashing mid-deploy. Install RSAT AD tools to test the full SPN'
                '    path, or use -SkipSpnCheck to accept NTLM.'
            ))
            (Step_VerifyService -HttpPort 80 -HttpsPort 443 -Core:$true)
            Step_RerunAndTests
            Step_ServerCleanup
        )
    }

    $scenarios['WindowsContainer'] = @{
        Name    = 'Windows container (Hyper-V isolation) on Windows 11'
        Summary = 'Dockerfile.windows via Docker Desktop in Windows-container mode.'
        Steps   = @(
            @{
                Title = 'What this scenario tests'
                Body  = @(
                    'Builds docker/Dockerfile.windows and runs it with'
                    'docker-compose.windows.yml under Docker Desktop on Windows 11. On a client'
                    'OS, Windows containers always run with HYPER-V ISOLATION - which is exactly'
                    'what this scenario is meant to cover.'
                )
            }
            @{
                Title  = 'Prepare the Windows 11 host'
                Body   = @(
                    'Requires Windows 11 Pro/Enterprise with hardware virtualization enabled in'
                    'firmware, plus Docker Desktop.'
                )
                Code   = @(
                    '# Elevated PowerShell (reboot when prompted):'
                    'Enable-WindowsOptionalFeature -Online -FeatureName Microsoft-Hyper-V, Containers -All'
                    ''
                    '# Install Docker Desktop (or download from docker.com):'
                    'winget install --id Docker.DockerDesktop'
                    ''
                    '# Switch Docker Desktop to WINDOWS containers (or use the tray icon menu'
                    '# "Switch to Windows containers..."):'
                    '& "$env:ProgramFiles\Docker\Docker\DockerCli.exe" -SwitchWindowsEngine'
                    ''
                    'docker version -f "{{.Server.Os}}"    # must print: windows'
                )
                Verify = @(
                    'docker version reports Server OS = windows'
                    'Hyper-V and Containers features are enabled'
                )
            }
            (Step_EntraAppRegistration -RedirectUri 'http://localhost:8080/signin-oidc')
            (Step_ContainerContent -BasePath 'C:\\webjea\\scripts')
            @{
                Title  = 'Configure compose for Entra and start the container'
                Body   = @(
                    'The Windows image DEFAULTS to Windows (Negotiate) auth, which needs a gMSA'
                    'credential spec on a domain-joined host. On a Windows 11 lab box, switch it'
                    'to Entra: in docker\docker-compose.windows.yml, uncomment the environment'
                    'block and fill in:'
                    ''
                    '  Authentication__Mode: Entra'
                    '  AzureAd__TenantId:    <tenant-guid>'
                    '  AzureAd__ClientId:    <client-id>'
                    '  AzureAd__ClientSecret: <client-secret>'
                    ''
                    'The first build pulls the Windows Server Core base image - several GB, so'
                    'expect the initial build to take a while.'
                )
                Code   = @(
                    'cd <repo-root>\docker'
                    'docker compose -f docker-compose.windows.yml up -d --build'
                )
                Note   = 'gMSA path (only on a DOMAIN-JOINED container host): create a gMSA, install the CredentialSpec module, New-CredentialSpec -AccountName <gmsa>, then uncomment security_opt in the compose file instead of the Entra block. See docs/docker.md.'
                Verify = @(
                    'Build succeeds (multi-stage: sdk:10.0 build, aspnet:10.0-windowsservercore-ltsc2022 runtime)'
                    'docker ps shows the webjea:windows container Up with 8080->8080'
                )
            }
            @{
                Title  = 'Confirm Hyper-V isolation'
                Body   = @('On Windows 11 (client OS) the container must be Hyper-V isolated.')
                Code   = @(
                    "docker inspect -f '{{.HostConfig.Isolation}}' `$(docker ps -q -f ancestor=webjea:windows)"
                )
                Verify = @(
                    "Isolation prints 'hyperv' (or 'default', which resolves to hyperv on a"
                    '  client OS - process isolation is server-host only)'
                )
            }
            (Step_ContainerVerify -Compose 'docker-compose.windows.yml' -OsNote 'Scripts execute inside the Windows container: Windows cmdlets work, but the container is not domain-joined unless you configured a credential spec.')
            @{
                Title  = 'Cleanup'
                Body   = @('Stop the stack and (optionally) switch Docker back to Linux containers.')
                Code   = @(
                    'docker compose -f docker-compose.windows.yml down'
                    '& "$env:ProgramFiles\Docker\Docker\DockerCli.exe" -SwitchLinuxEngine'
                )
                Verify = @('docker ps no longer lists the webjea container')
            }
        )
    }

    $scenarios['LinuxContainer'] = @{
        Name    = 'Linux container on Docker Desktop'
        Summary = 'Dockerfile (Linux) + docker-compose.yml with Entra ID auth on WSL2.'
        Steps   = @(
            @{
                Title = 'What this scenario tests'
                Body  = @(
                    'Builds docker/Dockerfile and runs it with docker/docker-compose.yml on'
                    'Docker Desktop (WSL2 backend). Linux hosting REQUIRES Entra ID'
                    'authentication - Windows/Negotiate auth needs a Windows host. The'
                    'PowerShell 7 engine ships inside the app, but scripts run on LINUX:'
                    'Windows-only modules/cmdlets are unavailable.'
                )
            }
            @{
                Title  = 'Prepare the host'
                Body   = @('Docker Desktop in its default LINUX container mode (WSL2).')
                Code   = @(
                    'winget install --id Docker.DockerDesktop    # if not installed'
                    'docker version -f "{{.Server.Os}}"          # must print: linux'
                )
                Verify = @('docker version reports Server OS = linux')
            }
            (Step_EntraAppRegistration -RedirectUri 'http://localhost:8080/signin-oidc')
            (Step_ContainerContent -BasePath '/webjea/scripts')
            @{
                Title  = 'Fill in the Entra values and start the container'
                Body   = @(
                    'Edit docker\docker-compose.yml and set the AzureAd__* placeholders'
                    '(TenantId, ClientId, ClientSecret). Prefer a .env file over committing the'
                    'secret: put WEBJEA_CLIENT_SECRET=... in docker\.env and reference it as'
                    'AzureAd__ClientSecret: ${WEBJEA_CLIENT_SECRET}.'
                )
                Code   = @(
                    'cd <repo-root>\docker'
                    'docker compose up -d --build'
                )
                Verify = @(
                    'Build succeeds (multi-stage: sdk:10.0 build, aspnet:10.0 runtime)'
                    'docker ps shows webjea:latest Up with 8080->8080'
                )
            }
            (Step_ContainerVerify -Compose '' -OsNote 'Note: the starter overview.ps1 calls Get-Process svchost, which does not exist on Linux - it will surface as a script ERROR in the output. That is fine here: it doubles as the error-rendering check (page still returns HTTP 200).')
            @{
                Title  = 'Cleanup'
                Body   = @('Stop and remove the stack.')
                Code   = @(
                    'docker compose down'
                    'docker image rm webjea:latest    # optional'
                )
                Verify = @('docker ps no longer lists the webjea container')
            }
        )
    }

    $scenarios
}

#endregion Scenario definitions

#region Rendering

function Show-Step([hashtable]$Step, [int]$Number, [int]$Total)
{
    Write-Host ''
    Write-Host ("=" * 78) -ForegroundColor DarkGray
    Write-Host ("Step {0} of {1}: {2}" -f $Number, $Total, $Step.Title) -ForegroundColor Yellow
    Write-Host ("=" * 78) -ForegroundColor DarkGray

    if ($Step.Body)
    {
        Write-Host ''
        foreach ($line in $Step.Body) { Write-Host "  $line" }
    }
    if ($Step.Code)
    {
        Write-Host ''
        foreach ($line in $Step.Code) { Write-Host "    $line" -ForegroundColor Cyan }
    }
    if ($Step.Note)
    {
        Write-Host ''
        Write-Host "  NOTE: $($Step.Note)" -ForegroundColor DarkYellow
    }
    if ($Step.Verify)
    {
        Write-Host ''
        Write-Host '  Verify before moving on:' -ForegroundColor Green
        foreach ($item in $Step.Verify) { Write-Host "    [ ] $item" -ForegroundColor Green }
    }
}

function Show-Scenario([string]$Key, [hashtable]$Def, [bool]$Paused)
{
    Write-Host ''
    Write-Host ("#" * 78) -ForegroundColor Magenta
    Write-Host ("# SCENARIO: {0}" -f $Def.Name) -ForegroundColor Magenta
    Write-Host ("# {0}" -f $Def.Summary) -ForegroundColor Magenta
    Write-Host ("#" * 78) -ForegroundColor Magenta

    $steps = @($Def.Steps)
    for ($i = 0; $i -lt $steps.Count; $i++)
    {
        Show-Step -Step $steps[$i] -Number ($i + 1) -Total $steps.Count
        if ($Paused -and $i -lt ($steps.Count - 1))
        {
            Write-Host ''
            $answer = Read-Host 'Enter = next step, Q = quit'
            if ($answer -match '^q') { return $false }
        }
    }
    Write-Host ''
    Write-Host ("Scenario '{0}' complete." -f $Def.Name) -ForegroundColor Magenta
    return $true
}

#endregion Rendering

#region Main

$allScenarios = Get-Scenarios

if ($List)
{
    $i = 0
    foreach ($key in $allScenarios.Keys)
    {
        $i++
        Write-Host ("{0}. {1,-16} {2}" -f $i, $key, $allScenarios[$key].Name)
        Write-Host ("   {0,-16} {1}" -f '', $allScenarios[$key].Summary) -ForegroundColor DarkGray
    }
    return
}

if ($Scenario)
{
    $selected = if ($Scenario -contains 'All') { @($allScenarios.Keys) } else { $Scenario }
    foreach ($key in $selected)
    {
        if (-not (Show-Scenario -Key $key -Def $allScenarios[$key] -Paused (-not $NoPause))) { return }
    }
    return
}

# Interactive menu
while ($true)
{
    Write-Host ''
    Write-Host 'WebJEA manual test guide - pick a target environment:' -ForegroundColor Yellow
    $keys = @($allScenarios.Keys)
    for ($i = 0; $i -lt $keys.Count; $i++)
    {
        Write-Host ("  {0}. {1}" -f ($i + 1), $allScenarios[$keys[$i]].Name)
        Write-Host ("     {0}" -f $allScenarios[$keys[$i]].Summary) -ForegroundColor DarkGray
    }
    Write-Host '  Q. Quit'
    $choice = Read-Host 'Choice'
    if ($choice -match '^q') { break }
    $index = 0
    if ([int]::TryParse($choice, [ref]$index) -and $index -ge 1 -and $index -le $keys.Count)
    {
        $null = Show-Scenario -Key $keys[$index - 1] -Def $allScenarios[$keys[$index - 1]] -Paused (-not $NoPause)
    }
    elseif ($allScenarios.Contains($choice))
    {
        $null = Show-Scenario -Key $choice -Def $allScenarios[$choice] -Paused (-not $NoPause)
    }
    else
    {
        Write-Host 'Unrecognized choice.' -ForegroundColor Red
    }
}

#endregion Main
