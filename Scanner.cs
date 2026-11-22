using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Threading;

namespace StorageDoctor
{
    internal sealed class FileSize
    {
        internal readonly string Path;
        internal readonly long Bytes;
        internal FileSize(string path, long bytes) { Path = path; Bytes = bytes; }
    }

    internal sealed class ScanResult
    {
        internal const int Limit = 100;
        internal long Files, Bytes, Skipped;
        internal readonly SortedSet<FileSize> Largest = new SortedSet<FileSize>(
            Comparer<FileSize>.Create(delegate(FileSize a, FileSize b) {
                int bySize = b.Bytes.CompareTo(a.Bytes);
                return bySize == 0 ? String.CompareOrdinal(a.Path, b.Path) : bySize;
            }));

        internal void Add(FileInfo file)
        {
            long bytes = file.Length;
            Bytes += bytes;
            Files++;
            if (Largest.Count == Limit && Largest.Max.Bytes > bytes) return;
            Largest.Add(new FileSize(file.FullName, bytes));
            if (Largest.Count > Limit) Largest.Remove(Largest.Max);
        }
    }

    internal static class Scanner
    {
        internal static string Size(long bytes)
        {
            string[] units = { "B", "KiB", "MiB", "GiB", "TiB", "PiB", "EiB" };
            double n = bytes;
            int unit = 0;
            while (n >= 1024 && unit < units.Length - 1) { n /= 1024; unit++; }
            return n.ToString("0.##") + " " + units[unit];
        }

        internal static ScanResult Scan(string folder, CancellationToken stop, IProgress<long> progress)
        {
            var root = new DirectoryInfo(folder);
            if (!root.Exists) throw new DirectoryNotFoundException("Choose an existing folder you can read.");
            if ((root.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("This folder is a link or cloud placeholder. Choose its local target.");
            var pending = new Stack<DirectoryInfo>();
            pending.Push(root);
            var result = new ScanResult();
            var timer = Stopwatch.StartNew();
            while (pending.Count > 0)
            {
                stop.ThrowIfCancellationRequested();
                var dir = pending.Pop();
                try
                {
                    dir.Refresh();
                    if (!dir.Exists || (dir.Attributes & FileAttributes.ReparsePoint) != 0) { result.Skipped++; continue; }
                    foreach (var entry in dir.EnumerateFileSystemInfos())
                    {
                        stop.ThrowIfCancellationRequested();
                        try
                        {
                            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) result.Skipped++;
                            else if (entry is DirectoryInfo) pending.Push((DirectoryInfo)entry);
                            else result.Add((FileInfo)entry);
                        }
                        catch (IOException) { result.Skipped++; }
                        catch (UnauthorizedAccessException) { result.Skipped++; }
                        catch (SecurityException) { result.Skipped++; }
                        if (progress != null && timer.ElapsedMilliseconds >= 250)
                        {
                            progress.Report(result.Files);
                            timer.Restart();
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (!(ex is IOException || ex is UnauthorizedAccessException || ex is SecurityException)) throw;
                    if (dir == root) throw;
                    result.Skipped++;
                }
            }
            stop.ThrowIfCancellationRequested();
            return result;
        }
    }
}
