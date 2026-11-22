# Compare the complete Scanner.Scan call with the previous release.
param([ValidateRange(2, 30)][int]$Runs = 6,
      [ValidateRange(1, 500)][int]$Folders = 100,
      [ValidateRange(1, 2000)][int]$FilesPerFolder = 200)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'build'
& (Join-Path $root 'build.ps1') -HostOnly | Out-Host
$baseline = git -C $root show 'v0.1.0:Scanner.cs'
if ($LASTEXITCODE) { throw 'The benchmark needs the original v0.1.0 Git tag.' }
$oldSource = Join-Path $out 'BaselineScanner.cs'
[IO.File]::WriteAllLines($oldSource, [string[]]$baseline)
$oldDll = Join-Path $out 'BaselineScanner.dll'
$compiler = Join-Path ([Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()) 'csc.exe'
& $compiler /nologo /target:library /optimize+ "/out:$oldDll" $oldSource
if ($LASTEXITCODE) { throw 'Baseline compilation failed.' }

function Get-ScannerMethod($binary) {
    $type = [Reflection.Assembly]::LoadFile($binary).GetType('StorageDoctor.Scanner', $true)
    $type.GetMethod('Scan', [Reflection.BindingFlags]'NonPublic,Static')
}

function Get-Signature($result) {
    $flags = [Reflection.BindingFlags]'NonPublic,Instance'
    $type = $result.GetType()
    foreach ($name in 'Files', 'Bytes', 'Skipped') { $type.GetField($name, $flags).GetValue($result) }
    foreach ($row in $type.GetField('Largest', $flags).GetValue($result)) {
        $row.GetType().GetField('Path', $flags).GetValue($row)
        $row.GetType().GetField('Bytes', $flags).GetValue($row)
    }
}

$methods = @{
    Managed = Get-ScannerMethod $oldDll
    Rust = Get-ScannerMethod (Join-Path $out 'StorageDoctor.exe')
}
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
$fixture = Join-Path $tempRoot ('storage-doctor-bench-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($fixture)
try {
    $blocks = @(foreach ($n in 0..31) { ,([byte[]]::new($n)) })
    for ($d = 0; $d -lt $Folders; $d++) {
        $dir = [IO.Directory]::CreateDirectory((Join-Path $fixture "folder $d"))
        for ($f = 0; $f -lt $FilesPerFolder; $f++) {
            [IO.File]::WriteAllBytes([IO.Path]::Combine($dir.FullName, "$f.bin"), $blocks[$f % $blocks.Length])
        }
    }
    $times = @{Managed = @(); Rust = @()}
    $signature = $null
    for ($run = 0; $run -le $Runs; $run++) {
        $order = if ($run % 2) { 'Rust', 'Managed' } else { 'Managed', 'Rust' }
        foreach ($engine in $order) {
            $argsForScan = [object[]]::new(3)
            $argsForScan[0] = $fixture.PSObject.BaseObject
            $argsForScan[1] = [Threading.CancellationToken]::None
            $clock = [Diagnostics.Stopwatch]::StartNew()
            $result = $methods[$engine].Invoke($null, $argsForScan)
            $clock.Stop()
            $actual = (Get-Signature $result) -join "`n"
            if ($null -eq $signature) { $signature = $actual }
            if ($signature -cne $actual) { throw "Different totals or ranking from $engine." }
            if ($run -eq 0) {
                '{0}: first invocation {1:N1} ms' -f $engine, $clock.Elapsed.TotalMilliseconds
            } else { $times[$engine] += $clock.Elapsed.TotalMilliseconds }
        }
    }
    foreach ($engine in 'Managed', 'Rust') {
        $sorted = @($times[$engine] | Sort-Object)
        $median = ($sorted[[int][math]::Floor(($Runs-1)/2)] + $sorted[[int][math]::Floor($Runs/2)]) / 2
        '{0}: median {1:N1} ms ({2} files, {3} measured runs)' -f $engine, $median, ($Folders*$FilesPerFolder), $Runs
    }
} finally {
    if ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($fixture)) -eq $tempRoot -and
        [IO.Path]::GetFileName($fixture) -match '^storage-doctor-bench-[0-9a-f]{32}$') {
        [IO.Directory]::Delete($fixture, $true)
    }
}
