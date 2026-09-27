Write-Output 'plain string on the success stream'
Write-Output @{ Key = 'value'; Count = 2 }
Write-Output ([pscustomobject]@{ Id = 'abc'; Enabled = $true; Nothing = $null })
