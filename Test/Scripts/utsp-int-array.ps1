param
(
    [Parameter()]
    [ValidateCount(1, 5)]
    [ValidateRange(1, 100)]
    [int[]]$Var
)
write-host "Var: $($Var -join ', ')"