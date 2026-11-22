param([switch]$HostOnly)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path ([Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()) 'csc.exe'
if (!(Test-Path -LiteralPath $compiler)) {
    throw 'Run this build with Windows PowerShell 5.1 and the .NET Framework 4.8 compiler installed.'
}
$outDir = Join-Path $PSScriptRoot 'build'
[void][IO.Directory]::CreateDirectory($outDir)
$exe = Join-Path $outDir 'StorageDoctor.exe'
$targets = [ordered]@{X86='i686'; X64='x86_64'; Arm64='aarch64'}
$resources = @(foreach ($arch in $targets.Keys) {
    if ($HostOnly -and $arch -ne [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()) { continue }
    $dll = Join-Path $outDir "scan-$arch.dll"
    & rustc --edition 2021 --crate-type cdylib --target "$($targets[$arch])-pc-windows-msvc" -C opt-level=3 -C strip=symbols -C target-feature=+crt-static (Join-Path $PSScriptRoot 'native/scan.rs') -o $dll
    if ($LASTEXITCODE) { throw "Rust scan engine compilation failed for $arch." }
    "/resource:$dll,StorageDoctor.Scan.$arch"
})
$sources = @((Join-Path $PSScriptRoot 'App.cs'), (Join-Path $PSScriptRoot 'Scanner.cs'))
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /r:System.Windows.Forms.dll /r:System.Drawing.dll @resources "/out:$exe" @sources
if ($LASTEXITCODE) { throw 'Application compilation failed.' }
Write-Output $exe
