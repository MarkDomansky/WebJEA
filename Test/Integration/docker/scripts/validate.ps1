<#
.SYNOPSIS
Short 1 line description of what this script does.

.DESCRIPTION
Detailed description of the script and what it is for.  This can be longer as well.

.EXAMPLE
ScriptTemplate.ps1 -param1 "Value"
Give an example of common usage.  Repeat EXAMPLE as desired

.PARAMETER Input01Mandatory
Short description of the parameter and how to use it.  Include PARAMETER for each parameter

.PARAMETER Input02MandatoryMinLen
Short description of the parameter and how to use it.

.INPUTS
String
#.NET Framework object types that can be _piped_ in.  Repeat allowed inputs within single INPUTS

.OUTPUTS
String
#.NET Framework object types that will be returned.

.LINK
URI or name of related topic. repeat LINK and URI/name as desired/needed.

.NOTES
Version:          1.0
Author:           <Author>
Creation Date:    <Date>
Purpose/Change:   Initial release


This template is CC0/1.0 Public Domain and can be found at github.com/markdomansky/powershellscripttemplate
#note: This comment block MUST come before everything else.
#>
#requires -version 3
#r#equires -runasadministrator
#r#equires -pssnapin <snapin> -version X.x
#r#equires -modules <module-name>,<module-name> #repeat as desired, replace <module-name> with @{ModuleName="X";ModuleVersion="1.0.0.0"} if you want specific versions
#r#equires -shellid <shellid>
##these must have the leading # to work. i.e. #requires -version 3 is active
##for -version: specify specific version requirements: 3, 5.1, etc.
##the other requires are pretty straightforward, just remove the extra # in requires

#This PS1 script can be turned into a function by wrapping the entire script in "Function <functionname> {<full content of script including help block>}"

[CmdletBinding(SupportsShouldProcess=$True,ConfirmImpact='Low')]
#SupportsShouldProcess=$true - forces acceptance of -Whatif and -Confirm
#ConfirmImpact='Medium' (Low, Medium (default), High) will prompt for action where $pscmdlet.shouldprocess is called
#  $pscmdlet.ShouldProcess("target",["action"]) is run.  Returns true/false, generally only want for high impact actions (i.e. restart computer)
#TODO: better explain confirmimpact, supportsshouldprocess
#DefaultParameterSetName="X" whatever parameterset used below
#HelpUri="Uri" used to store help documentation elsewhere: http://msdn.microsoft.com/en-us/library/dd878343(v=vs.85).aspx
#SupportsPaging=$true - adds First,Skip, IncludeTotalCount parameters automatically PSv3 req'd
#PositionalBinding=$false - default true, allows parameters by position, when false, all parameters must be defined by name (-computername "X")

#note: whatif, confirm, verbose, and debug are all passed through to sub-cmdlets/scripts called within the script.

param
(
    #special variables for WebJEA
    #[Parameter(Mandatory)] [string]$WEBJEAUsername="$($env:userdomain)\$($env:username)", #passes the domain\username to the script
    #[Parameter(Mandatory)] [string]$WEBJEAHostname=($env:computername), #passes the client machine from asp.net, will return IP address some times

#strings
    [Parameter(Position=0, Mandatory, ValueFromPipeline, ValueFromPipelineByPropertyName,
    HelpMessage='What computer name would you like to target?')]
	#also random comment
    [string]$Input01Mandatory,
	#just a comment

    [Parameter(Position=1, Mandatory=$true)]
    [ValidateLength(3,30)]
	<# comment block #>
    [string]$Input02MandatoryMinLen = "ABC",

    [Parameter(Position=2,mandatory=$false,HelpMessage="Help Message on MultiLine Directive")]
	#WEBJEA-Multiline
    [string]$Input03Str, #random comment

    [Parameter(HelpMessage='What value would you like to enter?')]
    [ValidateRange(1,100)]
    [int32]$Input04Range,

    [Parameter(Mandatory=$true,HelpMessage="Weird Help 'Message to trip up parser[]()")]
    [ValidateScript({$_ -eq [string]"ABCD"})] #must return $true/$false
    [string]$Input05Script="ABCD",

    [Parameter()]
    [ValidateNotNullOrEmpty()]
	#WEBJEA-Multiline
    [string]$Input13NotNullEmpty='x', #I think this will trip up the parser


#numbers
    [Parameter()]
    [int]$NInput01,

    [Parameter()]
    [int[]]$NInput02,

    [Parameter()]
    [uint32]$NInput3,

    [Parameter()]
    [double]$NInput4,

#listbox
    [Parameter()]
    [VALIDATESET('Input','Output','Both','A',1,2,3,"1/1/2017")]
	[ValidateCount(1,3)]
    [string[]]$LInput2,

    [Parameter(Mandatory)]
    [ValidateSet('Input','Output','Both')]
    [string]$LInput1,

    [Parameter()]
    [ValidateCount(1,5)] #number of items in collection
    [string[]]$Input08StrSetUpto5 = @('A',"B"),
	#multiline, but taken as multiple inputs

    [Parameter()]
    [ValidateSet('Input','Output','Both')] #any set of values you want here
    [string]$Input09ConstrainedSet,

    [Parameter()]
    [ValidateSet('Input','Output','Both','a','b','c','d',1,2)] #any set of values you want here
    [string[]]$Input09BConstrainedSet=@('a','d'),

    [Parameter(Mandatory)]
    [ValidateSet('Input','Output','Both','a','b','c','d',1,2)] #any set of values you want here
    [string[]]$Input09CConstrainedSet,

#dates
    [Parameter()]
    [datetime]$DInput01DT,

    [Parameter()]
	#WEBJEA-DateTime
    [datetime]$DInput02DT,

#switches/boolean
    [Parameter(Mandatory,HelpMessage="You must accept the terms")]
    [switch]$Input11Switch,

    [Parameter()]
    [boolean]$Input12Bool,


#webjea hidden parameters
    [Parameter()]
    [string]$WebJEAUsername, #this won't be shown to the user

    [Parameter()]
    [string]$WebJEAHostname #this won't be shown to the user


) #/param

begin {
    #do pre script checks, etc

} #/begin

process {

	Write-Verbose "Starting Process"
	Write-Host (Get-Date).tostring()
	Write-Host "Running as: $($env:username)"
	Write-Host "PSBoundParameters"
	$PSBoundParameters.keys | %{
		Write-Host "[[span|psbold|$_]]"
		$PSBoundParameters[$_] | write-host

	}


} #/process

end {
    #useful for cleanup, or to write-output whatever you want to return to the user
} #/end

###############################################################
# Parameter Template
###############################################################
#    [Parameter(Position=0, Mandatory, ParameterSetName="Group1", ValueFromPipeline, ValueFromPipelineByPropertyName, HelpMessage='What computer name would you like to target?')]
##Also available in parameter: ValueFromRemainingArgument=$true -this pushes all remaining unassigned variables into this parameter.
##if you use parametersetname and want a parameter eligible for some but not all paramsets, you can specify the [Parameter()] property multiple times consecutively before the parameter definition ([string]$computername)
##if you want a parameter available always, don't specify parametersetname
##mandatory, valuefrom* can have "=$true/$false" but like a switch, it's implicit. note: I think v2 requires explicit declaration
#    [SupportsWildcards()]
#    [Alias('MachineName')] #Can use -MachineName in this example instead of Param1 and it will still be recognized, helpful for ValueFromPipelineByPropertyName.  Is array-based, so ('MachineName','Server') is allowed.
#    [ValidateCount(2,5)] #number of items in collection, if you provided 1 item or 6 items in an array, it would error.  Typically used with arrays [vartype[]]
#    [ValidateLength(3,30)] #typically for strings, if the string was 'AB', it would error
#    [ValidatePattern("regexpattern")] #usually string, must match the regex pattern {$param -match $regex}
#    [ValidateRange(1,100)] #typically a number, -1 or 1000 would error
#    [ValidateScript({$_ -gt (10)})] #must return $true/$false, this would require the number to be greater than 10
#    [ValidateSet('Input','Output','Both')] #any set of values you want here.  These are the only accepted values for this input.  Can also be effectively combined with arrays
#    [ValidateNotNullOrEmpty()] #Common to use, Mandatory doesn't enforce content, only the existence of a parameter, this can be used to ensure the user provides something beyond $null or ""
#    [ValidateNotNull()] #same as ValidateNotNullOrEmpty, but only prevents $null
#    [AllowNull()] #effectively reverse of ValidateNotNullOrEmpty
#    [AllowEmptyString()]
#    [AllowEmptyCollection()]
#    #WEBJEA-Multiline #WebJEA specific directive, this forces webjea to show a multiline input field
#    #WEBJEA-DateTime #WebJEA specific directive, this forces webjea to show prompt for date AND time, not just date when using variable [datetime]
#    [string[]]$ComputerName, #accepts multiple, typically use a foreach in process{}
#    [string]$ComputerName, #only one
##    common: [switch]/[boolean], [int]/[int32]/[byte]/[uint]/[uint64], [pscredential], [psobject], [float]/[double], [datetime] many others possible and most support [] within to accept multiple in an array.
##    note: switch and boolean are not treated the same, switches are called with -paramname[:$true/false] where boolean are called -paramname $true/$false
##    can always specify default values after the variable e.g. [string]$computername = $env:computername or (get-date) or most anything in powershell, but you can't see the other variables yet.  It can however reference variables in the parent scope.
##DynamicParam is available, but an advanced topic not covered here.  see: https://docs.microsoft.com/en-us/powershell/module/microsoft.powershell.core/about/about_functions_advanced_parameters?view=powershell-5.1#dynamic-parameters

