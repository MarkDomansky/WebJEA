#requires -version 7.0
param (
    $maxjobs = 20,
    $repeat = 10,
    [switch]$allresults
)

$sb = {
    $id = $args[0]
    $repeat = $args[1]
    $allresults = $args[2]
    $username = "test$id"
    $cred = [pscredential]::new($username, (ConvertTo-SecureString $username -Force -asplain))
    foreach ($i in (1..$repeat))
    {
        if ($allresults) {write-host "$username - $i/$repeat"}
        $webreq = Invoke-WebRequest -Credential $cred -Uri "https://web22dbg.domain1.local/?cmdid=$username" -skipcertificatecheck #-CertificateThumbprint 'b91afaae825d5c2caa5b09534b136d957747e819'
        if ($webreq -match 'OUTPUT-(test\d+)-(test\d+)-(.+)-OUTPUT')
        {
            if ($matches[1] -eq $matches[2])
            {
                if ($allresults) { Write-Host ('{0} - {1} - {2}' -f $matches[1], $matches[2], $matches[3]) }
            }
            else
            {
                Write-Warning ('{0} - {1} - {2}' -f $matches[1], $matches[2], $matches[3])
            }
        }
        elseif ($webreq.rawcontent.indexof('You do not have access to this command.') -gt -1)
        {
            Write-Warning ('{0} - No Access' -f $username)
        }
        Start-Sleep -Milliseconds (Get-Random -max 500)
    }
}

Write-Host "Starting Job, MaxThreads $maxjobs, Repeat $repeat"
$starttime = Get-Date
$jobs = foreach ($id in (1..$maxjobs))
{
    Start-ThreadJob -Name "Test$id" -ScriptBlock $sb -throttle $maxjobs -ArgumentList $id, $repeat, $allresults
}
Wait-Job $jobs | Out-Null
$endtime = Get-Date

Write-Host 'Receiving Jobs'
$global:output = $jobs | Receive-Job
Write-Host ('Runtime seconds: {0}' -f ($endtime - $starttime).totalseconds)
Write-Host "Results in `$output"