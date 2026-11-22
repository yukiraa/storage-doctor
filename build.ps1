$ErrorActionPreference = 'Stop'
$compiler = Join-Path ([Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()) 'csc.exe'
if (!(Test-Path -LiteralPath $compiler)) {
    throw 'Run this build with Windows PowerShell 5.1 and the .NET Framework 4.8 compiler installed.'
}
$outDir = Join-Path $PSScriptRoot 'build'
[void][IO.Directory]::CreateDirectory($outDir)
$exe = Join-Path $outDir 'StorageDoctor.exe'
$sources = @((Join-Path $PSScriptRoot 'App.cs'), (Join-Path $PSScriptRoot 'Scanner.cs'))
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /r:System.Windows.Forms.dll /r:System.Drawing.dll "/out:$exe" @sources
if ($LASTEXITCODE) { throw 'Application compilation failed.' }
Write-Output $exe
