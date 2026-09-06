param
(
    [Parameter()]
    [ValidatePattern("(put|both|\d)")]
    $Var

)

write-host "Var: $Var"