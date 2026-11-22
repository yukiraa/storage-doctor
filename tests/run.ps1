param([switch]$Ui)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
& (Join-Path $root 'build.ps1')
$compiler = Join-Path ([Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()) 'csc.exe'
$exe = Join-Path $root 'build/Checks.exe'
& $compiler /nologo /target:exe /main:StorageDoctor.Checks /r:System.Windows.Forms.dll /r:System.Drawing.dll "/out:$exe" (Join-Path $root 'App.cs') (Join-Path $root 'Scanner.cs') (Join-Path $PSScriptRoot 'Checks.cs')
if ($LASTEXITCODE) { throw 'Test compilation failed.' }
if ($Ui) { & $exe --ui } else { & $exe }
if ($LASTEXITCODE) { throw 'Test run failed.' }
