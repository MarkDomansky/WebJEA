param([pscredential]$Var)

Write-Host ('UN: {0}' -f $cred.getnetworkcredential().username)
Write-Host ('PW: {0}' -f $cred.getnetworkcredential().password)

$BSTR = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($Var.password)
$clearpw = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto($BSTR)
[Runtime.InteropServices.Marshal]::ZeroFreeBSTR($BSTR) #this is an important step to keep things secure

Write-Host ('var.clearpassword: {0}' -f $clearpw)
