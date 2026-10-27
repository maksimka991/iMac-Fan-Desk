$ErrorActionPreference='Stop'
$exe=Join-Path $PSScriptRoot 'iMacFanDesk.exe'
$probe=Start-Process -FilePath $exe -ArgumentList '--probe' -WorkingDirectory $PSScriptRoot -Wait -PassThru
if($probe.ExitCode -ne 0){throw ('SMC probe failed: '+$probe.ExitCode)}
Start-Process -FilePath $exe -WorkingDirectory $PSScriptRoot
