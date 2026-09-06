$VerbosePreference = "Continue"
$DebugPreference = "Continue"

write-host "You can also specify a script to run each time the page loads using the onloadscript property."

write-host "WebJEA supports some basic formatting like:
Links: [[a |url|display]] -[[a|?cmdid=sample1|go to sample1]]-
CSS tags in a span: [[span |cssclasses|content]] -[[span|psvariable|this span uses a variable width font and italicised]]-.
Img: [[img |cssclasses|url]] [[img||./resources/ps.png]]
Nesting: [[a |url|[[img |cssclasses|url]]]] [[a|//powershell.org|[[img||./resources/ps.png]]]]"

Write-Host '[[span|psred|This is red]]'
Write-Host "We html encode script output for safety, <a href='see?'></a>"

Write-Host "Each script runs in its own instance, so load any scripts you need each time."

write-host "We honor spaces like a <pre>."
write-host (Get-Process svchost | select -First 2 | out-string)

Write-Host "WebJEA generates a usage log that documents all scripts that are run, with user and ip."
write-host "You can also send messages directly to this log by prefixing a line with 'WEBJEA:'."
write-host "The next write-host will not be shown but will appear in the logs."
Write-Host "WEBJEA:This is an NLOG message"

Write-Host "We also format Warning, Error, Verbose, and Debug messages."
Write-Warning "This is a warning message"
Write-Host "'natural' ps error:"
12/0
Write-Host "write-error:"
Write-Error "This is an error message with a [[a|?cmdid=overview|link]] in it"
Write-Verbose "This is a verbose message"
Write-Debug "This is a debug message"
write-host ""
Write-Host "All of this is exposed in [[a|psoutput.css|psoutput.css]]."
Write-Host $null

write-host "`$Env:WebJEAUsername is $($env:WebJEAUsername)"
write-host "`$Env:WebJEAHostname is $($env:WebJEAHostname)"
write-host ''
write-host "PSVersion: $($PSVersionTable.psversion.tostring())"
Write-Host (Get-Date).tostring()
