param
(
    [Parameter()]
    [ValidateScript( { $_ -eq [string]'ABCD' })] #must return $true/$false
    $Var

)

write-host "Var: $Var"