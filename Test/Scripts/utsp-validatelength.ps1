param
(
    [Parameter()]
    [ValidateLength(1,6)]
    [string]$Var

)

write-host "Var: $Var"