#monospace font formatting
write-host (Get-Process svchost | select -First 2 | out-string)
