param([securestring]$Var)
$VerbosePreference = "Continue"
$DebugPreference = "Continue"

$BSTR = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($Var)
$clearpw = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto($BSTR)
[Runtime.InteropServices.Marshal]::ZeroFreeBSTR($BSTR) #this is an important step to keep things secure

write-host ("Var.clear: {0}" -f $clearpw)
