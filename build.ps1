$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Path (Join-Path $PSScriptRoot 'build') -Force | Out-Null
& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /nologo /target:winexe /platform:x86 /optimize+ /win32manifest:"$PSScriptRoot\src\app.manifest" /out:"$PSScriptRoot\build\iMacFanDesk.exe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Core.dll "$PSScriptRoot\src\FanDeskV2.cs" "$PSScriptRoot\src\DeskModel.cs" "$PSScriptRoot\src\DeskCharts.cs" "$PSScriptRoot\src\Smc.cs"
if($LASTEXITCODE -ne 0){throw 'Build failed'}

Copy-Item (Join-Path $PSScriptRoot "inpout32.dll") (Join-Path $PSScriptRoot "build\inpout32.dll") -Force
