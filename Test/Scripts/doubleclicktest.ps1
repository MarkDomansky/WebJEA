param
(

    [Parameter()]
    [boolean]$ResetCounter
)
$file = "$env:temp\clicktest.txt"
Write-Host "ResetCounter: $ResetCounter"
if ($resetcounter -or (-not (Test-Path -Path $file)))
{
    $count = 0
}
else
{
    [int]$count = Get-Content -Path $file
    $count++
}
$count | Out-File -FilePath $file -Encoding ascii -Force
Write-Host "Count: $count"