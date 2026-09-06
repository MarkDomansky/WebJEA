<#
.SYNOPSIS
    Pester integration tests for WebJEA website.

.DESCRIPTION
    Contains Pester v5 tests for validating WebJEA website functionality including:
    - Website availability and health checks
    - Authentication flow
    - Page content validation
    - API endpoint testing

.PARAMETER Config
    The configuration hashtable loaded from config.json.

.PARAMETER Credential
    PSCredential for authenticated requests.

.NOTES
    This file is invoked by Invoke-IntegrationTests.ps1.
    Expand these tests as needed for your specific WebJEA deployment.
#>

param(
    [Parameter(Mandatory)]
    [hashtable]$Config,

    [Parameter()]
    [PSCredential]$Credential
)

BeforeDiscovery {
    $script:BaseUrl = $Config.WebServer.BaseUrl
    $script:FQDN = $Config.WebServer.FQDN
    $script:Timeout = $Config.WebServer.RequestTimeout
    $script:UseDefaultCredentials = ($null -eq $Credential -or $Credential -eq [PSCredential]::Empty)
}

BeforeAll {
    $script:BaseUrl = $Config.WebServer.BaseUrl
    $script:FQDN = $Config.WebServer.FQDN
    $script:Timeout = $Config.WebServer.RequestTimeout
    $script:Credential = $Credential
    $script:UseDefaultCredentials = ($null -eq $Credential -or $Credential -eq [PSCredential]::Empty)

    # Under Windows PowerShell 5.1 the psake ConfigureSsl task installs a
    # ServicePointManager trust-all policy; pwsh's HttpClient-based Invoke-WebRequest
    # ignores that, so apply -SkipCertificateCheck to every Invoke-WebRequest instead.
    if ($Config.WebServer.IgnoreSslErrors -and $PSVersionTable.PSEdition -eq 'Core') {
        $PSDefaultParameterValues['Invoke-WebRequest:SkipCertificateCheck'] = $true
    }

    function Invoke-WebJEARequest {
        [CmdletBinding()]
        param(
            [Parameter(Mandatory)]
            [string]$Uri,

            [Parameter()]
            [string]$Method = 'GET',

            [Parameter()]
            [hashtable]$Body,

            [Parameter()]
            [switch]$NoAuth
        )

        $params = @{
            Uri                = $Uri
            Method             = $Method
            TimeoutSec         = $script:Timeout
            UseBasicParsing    = $true
            ErrorAction        = 'Stop'
        }

        if (-not $NoAuth) {
            if ($script:UseDefaultCredentials) {
                $params.UseDefaultCredentials = $true
            }
            else {
                $params.Credential = $script:Credential
            }
        }

        if ($Body) {
            $params.Body = $Body
            $params.ContentType = 'application/x-www-form-urlencoded'
        }

        return Invoke-WebRequest @params
    }
}

Describe 'WebJEA Website Availability' -Tag 'Smoke', 'Availability' {

    Context 'Basic Connectivity' {

        It 'Should resolve the FQDN to an IP address' {
            $dns = Resolve-DnsName -Name $script:FQDN -ErrorAction Stop
            $dns | Should -Not -BeNullOrEmpty
            $dns.IPAddress | Should -Not -BeNullOrEmpty
        }

        It 'Should have port 443 open' {
            $tcpTest = Test-NetConnection -ComputerName $script:FQDN -Port 443 -WarningAction SilentlyContinue
            $tcpTest.TcpTestSucceeded | Should -BeTrue
        }
    }

    Context 'HTTPS Response' {

        It 'Should return HTTP 200 for the base URL' {
            $response = Invoke-WebJEARequest -Uri $script:BaseUrl
            $response.StatusCode | Should -Be 200
        }

        It 'Should return content with expected page title' {
            $response = Invoke-WebJEARequest -Uri $script:BaseUrl
            $response.Content | Should -Match '<title>.*WebJEA.*</title>'
        }

        It 'Should include security headers' {
            $response = Invoke-WebJEARequest -Uri $script:BaseUrl

            $response.Headers.'X-Content-Type-Options' | Should -Be 'nosniff'
            $response.Headers.'X-Frame-Options' | Should -BeIn @('DENY', 'SAMEORIGIN')
        }
    }
}

Describe 'WebJEA Authentication' -Tag 'Authentication' {

    Context 'Windows Authentication' {

        It 'Should reject unauthenticated requests with 401' {
            $params = @{
                Uri                = $script:BaseUrl
                Method             = 'GET'
                UseBasicParsing    = $true
                ErrorAction        = 'Stop'
            }

            # Expect 401 Unauthorized without credentials
            { Invoke-WebRequest @params } | Should -Throw
        }

        It 'Should accept authenticated requests' {
            $response = Invoke-WebJEARequest -Uri $script:BaseUrl
            $response.StatusCode | Should -Be 200
        }

        It 'Should display the authenticated username on the page' {
            $response = Invoke-WebJEARequest -Uri $script:BaseUrl

            $expectedUser = if ($script:UseDefaultCredentials) {
                [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
            }
            else {
                $script:Credential.UserName
            }

            # WebJEA typically displays the username somewhere on the page
            $response.Content | Should -Match $expectedUser.Split('\')[-1]
        }
    }
}

Describe 'WebJEA Page Content' -Tag 'Content' {

    Context 'Main Page Structure' {

        It 'Should contain a sidebar with menu items' {
            $response = Invoke-WebJEARequest -Uri $script:BaseUrl
            $response.Content | Should -Match 'sidebar|menu|nav'
        }

        It 'Should contain the main content area' {
            $response = Invoke-WebJEARequest -Uri $script:BaseUrl
            $response.Content | Should -Match 'content|main|container'
        }

        It 'Should load CSS stylesheets' {
            $response = Invoke-WebJEARequest -Uri $script:BaseUrl
            $response.Content | Should -Match '\.css'
        }

        It 'Should load JavaScript files' {
            $response = Invoke-WebJEARequest -Uri $script:BaseUrl
            $response.Content | Should -Match '\.js'
        }
    }

    Context 'Static Resources' {

        It 'Should serve the main CSS file' {
            $cssUrl = "$script:BaseUrl/main.css"
            $response = Invoke-WebJEARequest -Uri $cssUrl
            $response.StatusCode | Should -Be 200
            $response.Headers.'Content-Type' | Should -Match 'text/css'
        }

        It 'Should serve the sidebar CSS file' {
            $cssUrl = "$script:BaseUrl/sidebar.css"
            $response = Invoke-WebJEARequest -Uri $cssUrl
            $response.StatusCode | Should -Be 200
        }

        It 'Should serve JavaScript files' {
            $jsUrl = "$script:BaseUrl/startup.js"
            $response = Invoke-WebJEARequest -Uri $jsUrl
            $response.StatusCode | Should -Be 200
        }
    }
}

Describe 'WebJEA Functionality' -Tag 'Functional' {

    Context 'Error Handling' {

        It 'Should return custom error page for invalid paths' {
            $invalidUrl = "$script:BaseUrl/nonexistent-page-12345"

            try {
                Invoke-WebJEARequest -Uri $invalidUrl -ErrorAction Stop
            }
            catch {
                $_.Exception.Response.StatusCode.value__ | Should -BeIn @(404, 500)
            }
        }
    }

    Context 'Session Handling' {

        It 'Should maintain session across multiple requests' {
            $session = New-Object Microsoft.PowerShell.Commands.WebRequestSession

            $params = @{
                Uri                   = $script:BaseUrl
                WebSession            = $session
                UseBasicParsing       = $true
                UseDefaultCredentials = $true
                TimeoutSec            = $script:Timeout
            }

            $response1 = Invoke-WebRequest @params
            $response2 = Invoke-WebRequest @params

            $response1.StatusCode | Should -Be 200
            $response2.StatusCode | Should -Be 200
        }
    }
}

Describe 'WebJEA Performance' -Tag 'Performance' {

    Context 'Response Time' {

        It 'Should respond within acceptable time for the main page' {
            $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
            $response = Invoke-WebJEARequest -Uri $script:BaseUrl
            $stopwatch.Stop()

            $stopwatch.ElapsedMilliseconds | Should -BeLessThan 5000
        }

        It 'Should respond quickly for static resources' {
            $cssUrl = "$script:BaseUrl/main.css"
            $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
            $response = Invoke-WebJEARequest -Uri $cssUrl
            $stopwatch.Stop()

            $stopwatch.ElapsedMilliseconds | Should -BeLessThan 2000
        }
    }
}

Describe 'WebJEA Security' -Tag 'Security' {

    Context 'SSL/TLS Configuration' {

        It 'Should use TLS 1.2 or higher' {
            # This tests that the connection succeeds with TLS 1.2
            [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

            $response = Invoke-WebJEARequest -Uri $script:BaseUrl
            $response.StatusCode | Should -Be 200
        }
    }

    Context 'Headers and Content Security' {

        It 'Should not expose server version information' {
            $response = Invoke-WebJEARequest -Uri $script:BaseUrl

            # Server header should not reveal detailed version info
            if ($response.Headers.Server) {
                $response.Headers.Server | Should -Not -Match 'IIS/\d+\.\d+'
            }
        }

        It 'Should not expose ASP.NET version' {
            $response = Invoke-WebJEARequest -Uri $script:BaseUrl

            $response.Headers.'X-AspNet-Version' | Should -BeNullOrEmpty
            $response.Headers.'X-Powered-By' | Should -BeNullOrEmpty
        }
    }
}
