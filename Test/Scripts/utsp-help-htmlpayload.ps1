<#
.SYNOPSIS
Synopsis with <script >alert('hi');</script> payload

.PARAMETER Var
The computer na'<script >alert('hi');</script>me Var to query. Just one.

#>
param(
    $Var
)
write-host "Var: $Var"
