param([switch]$Ui, [switch]$AllArchitectures)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
& (Join-Path $root 'build.ps1') -HostOnly:(-not $AllArchitectures)
$compiler = Join-Path ([Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()) 'csc.exe'
$exe = Join-Path $root 'build/Checks.exe'
$app = Join-Path $root 'build/StorageDoctor.exe'
& $compiler /nologo /target:exe /main:StorageDoctor.Checks /r:System.Windows.Forms.dll /r:System.Drawing.dll "/r:$app" "/out:$exe" (Join-Path $PSScriptRoot 'Checks.cs')
if ($LASTEXITCODE) { throw 'Test compilation failed.' }
if ($Ui) { & $exe --ui } else { & $exe }
if ($LASTEXITCODE) { throw 'Test run failed.' }
