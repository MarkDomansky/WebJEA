param
(
	[Parameter()]
	[pscredential]$Var
)
Write-Host ('var.username: {0}' -f $Var.getnetworkcredential().username)
Write-Host ('var.password: {0}' -f $Var.getnetworkcredential().password)

$BSTR = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($Var.password)
$clearpw = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto($BSTR)
[Runtime.InteropServices.Marshal]::ZeroFreeBSTR($BSTR) #this is an important step to keep things secure

Write-Host ('var.password.clear: {0}' -f $clearpw)
