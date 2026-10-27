$ErrorActionPreference='Stop'
$source=$PSScriptRoot
$target='C:\Users\root\AppData\Local\Programs\iMacFanDesk'
$data='C:\Users\root\AppData\Local\iMacFanDesk'
$backup=Join-Path $source ('update-backup-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $backup -Force|Out-Null
Start-Transcript -Path (Join-Path $backup 'update.txt') -Force
try {
    Copy-Item -LiteralPath $target -Destination (Join-Path $backup 'previous-install') -Recurse
    if(Test-Path (Join-Path $data 'sensor.txt')){Copy-Item (Join-Path $data 'sensor.txt') $backup}
    Export-ScheduledTask -TaskName iMacFanDesk|Set-Content (Join-Path $backup 'task.xml') -Encoding Unicode
    Stop-ScheduledTask -TaskName iMacFanDesk
    Start-Sleep -Seconds 1
    Get-Process iMacFanDesk -ErrorAction SilentlyContinue|Where-Object {$_.Path -eq (Join-Path $target 'iMacFanDesk.exe')}|Stop-Process
    Copy-Item (Join-Path $source 'iMacFanDesk-candidate.exe') (Join-Path $target 'iMacFanDesk.exe') -Force
    Copy-Item (Join-Path $source 'README.md') $target -Force
    Copy-Item (Join-Path $source 'build.ps1') $target -Force
    Copy-Item (Join-Path $source 'src\*') (Join-Path $target 'src') -Force
    [IO.File]::WriteAllText((Join-Path $data 'sensor.txt'),'HOT')
    $test=Start-Process (Join-Path $target 'iMacFanDesk.exe') -ArgumentList '--self-test' -Wait -PassThru
    if($test.ExitCode -ne 0){throw 'Safety self-test failed'}
    Start-ScheduledTask -TaskName iMacFanDesk
    Start-Sleep -Seconds 12
    Start-Process (Join-Path $target 'iMacFanDesk.exe') -WorkingDirectory $target
    Get-Content (Join-Path $data 'self-test.txt')
    Get-Content (Join-Path $data 'state.txt')
    'SUCCESS'|Set-Content (Join-Path $backup 'result.txt')
}catch {
    $_|Out-String|Set-Content (Join-Path $backup 'result.txt')
    # Recover the previous executable and controller if deployment fails.
    Stop-ScheduledTask -TaskName iMacFanDesk -ErrorAction SilentlyContinue
    Copy-Item (Join-Path $backup 'previous-install\iMacFanDesk.exe') (Join-Path $target 'iMacFanDesk.exe') -Force
    if(Test-Path (Join-Path $backup 'sensor.txt')){Copy-Item (Join-Path $backup 'sensor.txt') (Join-Path $data 'sensor.txt') -Force}
    Start-ScheduledTask -TaskName iMacFanDesk
    throw
}finally{Stop-Transcript}
