param
(
    [Parameter()]
    [ValidateCount(1,5)] #number of items in collection
    [string[]]$Var

)

write-host "Var: $($Var -join ', ')"