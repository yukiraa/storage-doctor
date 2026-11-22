using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Checks")]

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
        internal long Files, Bytes, Skipped;
        internal readonly List<FileSize> Largest = new List<FileSize>(100);
    }

    internal static class Scanner
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int ProgressCallback(long files);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate void RowCallback(long bytes, IntPtr path, int length);
        [StructLayout(LayoutKind.Sequential)]
        struct Totals { internal long Files, Bytes, Skipped; }
        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        delegate int ScanFolder([MarshalAs(UnmanagedType.LPWStr)] string path, int length,
            ProgressCallback progress, RowCallback row, out Totals totals);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        static extern IntPtr LoadLibraryExW(string path, IntPtr file, uint flags);
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        static extern IntPtr GetProcAddress(IntPtr module, string name);
        [DllImport("kernel32.dll", ExactSpelling = true)]
        static extern bool FreeLibrary(IntPtr module);

        sealed class Engine : SafeHandleZeroOrMinusOneIsInvalid
        {
            readonly FileStream imageLock;
            readonly string path;
            internal ScanFolder Invoke;

            internal Engine(IntPtr module, string path, FileStream imageLock) : base(true)
            {
                SetHandle(module);
                this.path = path;
                this.imageLock = imageLock;
            }

            protected override bool ReleaseHandle()
            {
                bool unloaded = FreeLibrary(handle);
                imageLock.Dispose();
                try { File.Delete(path); }
                catch (IOException) {}
                catch (UnauthorizedAccessException) {}
                return unloaded;
            }
        }

        static readonly Lazy<Engine> Native = new Lazy<Engine>(LoadEngine);

        internal static Task Prepare()
        {
            return Task.Run(() => Native.Value);
        }

        static Engine LoadEngine()
        {
            string arch = RuntimeInformation.ProcessArchitecture.ToString();
            string path = Path.Combine(Path.GetTempPath(), "StorageDoctor-scan-" + Guid.NewGuid().ToString("N") + ".dll");
            FileStream keepReadOnly;
            using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("StorageDoctor.Scan." + arch))
            {
                if (resource == null) throw new IOException("No bundled scan engine for " + arch + ".");
                using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    resource.CopyTo(output);
                keepReadOnly = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                // Verify the locked image before allowing Windows to load it.
                using (var hash = SHA256.Create())
                {
                    resource.Position = 0;
                    if (Convert.ToBase64String(hash.ComputeHash(resource)) != Convert.ToBase64String(hash.ComputeHash(keepReadOnly)))
                    {
                        keepReadOnly.Dispose();
                        throw new IOException("The extracted scan engine did not match the embedded copy.");
                    }
                }
            }
            // Resolve native dependencies exclusively from system32.
            var module = LoadLibraryExW(path, IntPtr.Zero, 0x800);
            if (module == IntPtr.Zero)
            {
                var error = new Win32Exception(Marshal.GetLastWin32Error());
                keepReadOnly.Dispose();
                File.Delete(path);
                throw error;
            }
            var engine = new Engine(module, path, keepReadOnly);
            try
            {
                var symbol = GetProcAddress(module, "scan_folder");
                if (symbol == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
                engine.Invoke = (ScanFolder)Marshal.GetDelegateForFunctionPointer(symbol, typeof(ScanFolder));
                AppDomain.CurrentDomain.ProcessExit += delegate { engine.Dispose(); };
                return engine;
            }
            catch { engine.Dispose(); throw; }
        }

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
            stop.ThrowIfCancellationRequested();
            var root = new DirectoryInfo(folder);
            if (!root.Exists) throw new DirectoryNotFoundException("Choose an existing folder you can read.");
            if ((root.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("This folder is a link or cloud placeholder. Choose its local target.");

            var engine = Native.Value;
            bool acquired = false;
            try
            {
                engine.DangerousAddRef(ref acquired);
                var result = new ScanResult();
                Totals totals;
                var path = root.FullName;
                int error = engine.Invoke(path, path.Length, count => {
                    if (stop.IsCancellationRequested) return 0;
                    if (progress != null) progress.Report(count);
                    return 1;
                }, (bytes, name, length) => result.Largest.Add(new FileSize(Marshal.PtrToStringUni(name, length), bytes)), out totals);
                stop.ThrowIfCancellationRequested();
                if (error != 0) throw new IOException(new Win32Exception(error).Message);
                result.Files = totals.Files;
                result.Bytes = totals.Bytes;
                result.Skipped = totals.Skipped;
                return result;
            }
            finally { if (acquired) engine.DangerousRelease(); }
        }
    }
}
