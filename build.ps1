# Builds SpeedUpMyPC.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
# Nothing to install.
$ErrorActionPreference = 'Stop'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }

$src = Join-Path $PSScriptRoot 'src\SpeedUpMyPC'
$out = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Force $out | Out-Null

& $csc /nologo /target:winexe /optimize+ /platform:anycpu `
    /out:"$out\SpeedUpMyPC.exe" `
    /win32manifest:"$src\app.manifest" `
    /win32icon:"$src\app.ico" `
    /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.ServiceProcess.dll `
    "$src\*.cs"
if ($LASTEXITCODE -ne 0) { throw "Build failed" }
Write-Host "Built $out\SpeedUpMyPC.exe"
