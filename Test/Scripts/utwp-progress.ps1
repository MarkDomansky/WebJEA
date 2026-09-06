param ($delay=1500)
try {
$host.PrivateData.progressforegroundcolor = "darkcyan"
$host.PrivateData.progressbackgroundcolor = "white"
} catch {}
#42 char wide to get the current gif

(0..10) | %{write-progress -Activity "WebJEA Executing" -PercentComplete ($_*10);sleep -Milliseconds $delay}
Write-Progress -Activity "WebJEA Executing" -Completed