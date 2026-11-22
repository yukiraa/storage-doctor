# Storage Doctor for Windows

See which files take up the most space in a folder and how much room is available on its drive. Runs on Windows 10 and 11 with .NET Framework 4.8.

## Run

1. Download **StorageDoctor.exe** from the [latest release](https://github.com/yukiraa/storage-doctor/releases/latest) and open it.
2. Select **Choose folder** to pick a location such as Downloads, then **Scan folder**. You can cancel a running scan.
3. Review the largest 100 files, sorted by size. Select a row and click **Show in Explorer**, or double-click it, to inspect the file before deciding what to move or remove.
4. For temporary files and system cleanup, open **Windows storage settings**. After cleanup, scan again to refresh the available space.

Scanning reads file metadata. Storage Doctor leaves file contents and Windows cleanup settings unchanged. Scan results stay on your PC.

## Reading the results

The summary counts readable files throughout the selected folder, including files outside the top 100. Sizes are logical file lengths. Compression, sparse files and hard links can make that total differ from the space actually occupied or recoverable. Windows reports drive space available to the current user.

Links, junctions and other reparse points are skipped, along with entries Windows refuses to read. The skipped count includes each omitted directory once; its contents are not counted. Changes made during a scan can affect the totals.

## Build and test

In Windows PowerShell 5.1, from the repository root:

```powershell
.\build.ps1
.\tests\run.ps1 -Ui
```

The build produces `build/StorageDoctor.exe`. Tests create a temporary sample folder and verify ranking, counts, cancellation, and the desktop scan flow. No administrator access is required.

Released under the [MIT license](LICENSE).
