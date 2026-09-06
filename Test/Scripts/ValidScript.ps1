<#
.SYNOPSIS
This is a valid script.

.DESCRIPTION
A valid test script used for unit testing PSCmd initialization.

#>
param
(
    [Parameter(Mandatory=$true)]
    [string]$Param1,

    [Parameter()]
    [string]$Param2 = "default"
)

Write-Host "Param1: $Param1"
Write-Host "Param2: $Param2"
