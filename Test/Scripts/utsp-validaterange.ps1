param
(
    [Parameter()]
    [ValidateRange(1, 100)]
    $Var
)
write-host "Var: $Var"