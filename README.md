# Storage Doctor for Windows

See which files take up the most space in a folder and how much room is available on its drive. Runs on Windows 10 and 11 with .NET Framework 4.8.

## Run

1. Download **StorageDoctor.exe** from the [latest release](https://github.com/yukiraa/storage-doctor/releases/latest) and open it.
2. Select **Choose folder** to pick a location such as Downloads, then **Scan folder**. You can cancel a running scan.
3. Review the largest 100 files, sorted by size. Select a row and click **Show in Explorer**, or double-click it, to inspect the file before deciding what to move or remove.
4. For temporary files and system cleanup, open **Windows storage settings**. After cleanup, scan again to refresh the available space.

Scanning reads file metadata. Storage Doctor leaves file contents and Windows cleanup settings unchanged. Scan results stay on your PC.

The Windows interface uses C# and the scan engine uses Rust. The executable bundles the native engines for x86, x64 and ARM64. When the window opens it prepares the matching engine in the background, verifies the extracted library in the user's temporary directory and reuses it for subsequent scans. Users do not need to install Rust.

## Reading the results

The summary counts readable files throughout the selected folder, including files outside the top 100. Sizes are logical file lengths. Compression, sparse files and hard links can make that total differ from the space actually occupied or recoverable. Windows reports drive space available to the current user.

Links, junctions and other reparse points are skipped, along with entries Windows refuses to read. The skipped count includes each omitted directory once; its contents are not counted. Changes made during a scan can affect the totals.

## Build and test

For development, install stable Rust with the Windows MSVC toolchain and Visual Studio C++ Build Tools for your architecture. In Windows PowerShell 5.1, from the repository root:

```powershell
.\build.ps1 -HostOnly
.\tests\run.ps1 -Ui
.\tests\benchmark.ps1
```

The benchmark needs Git tag `v0.1.0`. It checks both engines' counts and every ranked path and size on the same 20,000-file fixture. It reports first invocation separately and alternates the engine order for six repeat scans. Actual times depend on the drive, folder layout and filesystem cache.

For the single release executable, install Visual Studio C++ link tools and SDK libraries for x86, x64 and ARM64, run `rustup target add i686-pc-windows-msvc x86_64-pc-windows-msvc aarch64-pc-windows-msvc`, then run `.\tests\run.ps1 -Ui -AllArchitectures`. The executable is `build/StorageDoctor.exe`. The scan and desktop tests use temporary sample files; using the app needs no administrator access.

Released under the [MIT license](LICENSE).
