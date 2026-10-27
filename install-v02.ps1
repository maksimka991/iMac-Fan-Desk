$ErrorActionPreference='Stop'
$source=$PSScriptRoot
$target='C:\Users\root\AppData\Local\Programs\iMacFanDesk'
$backup=Join-Path $source ('install-backup-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $backup -Force|Out-Null
Start-Transcript -Path (Join-Path $backup 'install.txt') -Force
try {
    foreach($name in @('RPMac','iMacFanDesk')) {
        if(Get-ScheduledTask -TaskName $name -ErrorAction SilentlyContinue){Export-ScheduledTask -TaskName $name|Set-Content (Join-Path $backup ($name+'.xml')) -Encoding Unicode}
    }
    Get-Process iMacFanDesk -ErrorAction SilentlyContinue|ForEach-Object{
        if($_.Path -eq (Join-Path $target 'iMacFanDesk.exe')){throw 'Installed version is running. Exit it through the tray first.'}
        [void]$_.CloseMainWindow();if(!$_.WaitForExit(10000)){throw 'Old Fan Desk did not close.'}
    }
    if(Test-Path -LiteralPath $target){Copy-Item -LiteralPath $target -Destination (Join-Path $backup 'previous-install') -Recurse}
    New-Item -ItemType Directory -Path $target -Force|Out-Null
    Copy-Item -LiteralPath (Join-Path $source 'iMacFanDesk-candidate.exe') -Destination (Join-Path $target 'iMacFanDesk.exe') -Force
    foreach($file in @('inpout32.dll','LICENSE','NOTICE','build.ps1')){Copy-Item -LiteralPath (Join-Path $source $file) -Destination $target -Force}
    Copy-Item -LiteralPath (Join-Path $source 'README.md') -Destination (Join-Path $target 'README.md') -Force
    Copy-Item -LiteralPath (Join-Path $source 'src') -Destination $target -Recurse -Force
    $test=Start-Process (Join-Path $target 'iMacFanDesk.exe') -ArgumentList '--self-test' -Wait -PassThru
    if($test.ExitCode -ne 0){throw 'Installed self-test failed'}
    $taskUser='DESKTOP-CBA0B6S\root'
    $action=New-ScheduledTaskAction -Execute (Join-Path $target 'iMacFanDesk.exe') -Argument '--tray' -WorkingDirectory $target
    $trigger=New-ScheduledTaskTrigger -AtLogOn -User $taskUser;$trigger.Delay='PT15S'
    $principal=New-ScheduledTaskPrincipal -UserId $taskUser -LogonType Interactive -RunLevel Highest
    $settings=New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable -MultipleInstances IgnoreNew -ExecutionTimeLimit ([TimeSpan]::Zero) -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1)
    $settings.Priority=5
    Register-ScheduledTask -TaskName iMacFanDesk -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Description 'iMac Fan Desk 0.2: selected sensor, 55-80C curve, tray temperature' -Force|Out-Null
    if(Get-ScheduledTask -TaskName RPMac -ErrorAction SilentlyContinue){Disable-ScheduledTask -TaskName RPMac|Out-Null}
    Get-Process RPMac -ErrorAction SilentlyContinue|Stop-Process
    $shortcutPath='C:\Users\root\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\iMac Fan Desk.lnk'
    if(Test-Path -LiteralPath $shortcutPath){Copy-Item -LiteralPath $shortcutPath -Destination $backup}
    $shell=New-Object -ComObject WScript.Shell;$shortcut=$shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath=Join-Path $target 'iMacFanDesk.exe';$shortcut.WorkingDirectory=$target;$shortcut.Description='Керування охолодженням iMac';$shortcut.Save()
    Start-ScheduledTask -TaskName iMacFanDesk
    Start-Sleep -Seconds 18
    Get-ScheduledTask -TaskName iMacFanDesk,RPMac|Select-Object TaskName,State|Format-Table
    Get-Content 'C:\Users\root\AppData\Local\iMacFanDesk\state.txt'
    Export-ScheduledTask -TaskName iMacFanDesk|Set-Content (Join-Path $backup 'iMacFanDesk-after.xml') -Encoding Unicode
    'SUCCESS'|Set-Content (Join-Path $backup 'result.txt')
    $backup|Set-Content (Join-Path $source 'latest-install-backup.txt')
}catch{$_|Out-String|Write-Output;$_|Out-String|Set-Content (Join-Path $backup 'result.txt');throw}
finally{Stop-Transcript}
