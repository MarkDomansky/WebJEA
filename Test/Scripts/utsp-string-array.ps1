param
(
    [Parameter()]
    [string[]]$Var=@('a','d')

)

write-host "Var: $($Var -join ', ')"