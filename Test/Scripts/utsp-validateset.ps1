param
(
    [Parameter()]
    [ValidateSet('Input','Output','Both',1,2)] #any set of values you want here
    [string]$Var

)

write-host "Var: $Var"