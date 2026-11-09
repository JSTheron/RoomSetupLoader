$ErrorActionPreference = 'Stop'
$taskName = 'Room Setup Loader - Any User Logon'
$task = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
if ($null -ne $task) { Unregister-ScheduledTask -TaskName $taskName -Confirm:$false }
