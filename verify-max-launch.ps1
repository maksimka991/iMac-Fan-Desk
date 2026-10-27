$ErrorActionPreference='Stop'
$exe=Join-Path $PSScriptRoot 'iMacFanDesk.exe'
Get-Process iMacFanDesk -ErrorAction SilentlyContinue | ForEach-Object {
    [void]$_.CloseMainWindow()
    if(!$_.WaitForExit(10000)){throw 'Close the current Fan Desk window before rebuilding.'}
}
& (Join-Path $PSScriptRoot 'build.ps1')
$probe=Start-Process -FilePath $exe -ArgumentList '--verify-max' -WorkingDirectory $PSScriptRoot -Wait -PassThru
if($probe.ExitCode -ne 0){throw ('Max verification failed: '+$probe.ExitCode)}
Start-Process -FilePath $exe -WorkingDirectory $PSScriptRoot
