$ErrorActionPreference='Stop'
Get-Process iMacFanDesk -ErrorAction SilentlyContinue | ForEach-Object {
    [void]$_.CloseMainWindow()
    if(!$_.WaitForExit(10000)){throw 'Current window is still open.'}
}
& (Join-Path $PSScriptRoot 'build.ps1')
Start-Process -FilePath (Join-Path $PSScriptRoot 'iMacFanDesk.exe') -ArgumentList '--max' -WorkingDirectory $PSScriptRoot
