using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace StorageDoctor
{
    internal static class Checks
    {
        static void Require(bool ok, string message)
        {
            if (!ok) throw new Exception(message);
        }

        static IEnumerable<Control> ControlsBelow(Control root)
        {
            foreach (Control child in root.Controls)
            {
                yield return child;
                foreach (var nested in ControlsBelow(child)) yield return nested;
            }
        }

        static async Task WaitFor(Func<bool> ready)
        {
            var clock = Stopwatch.StartNew();
            do
            {
                if (ready()) return;
                await Task.Delay(10);
            } while (clock.Elapsed.TotalSeconds < 20);
            throw new TimeoutException("Window did not finish the fixture scan.");
        }

        [STAThread]
        static void Main(string[] args)
        {
            string temp = Path.GetFullPath(Path.GetTempPath());
            string root = Path.Combine(temp, "storage-doctor-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var empty = Scanner.Scan(root, CancellationToken.None, null);
                Require(empty.Files == 0 && empty.Bytes == 0 && empty.Skipped == 0, "Empty directory.");
                var nested = Directory.CreateDirectory(Path.Combine(root, "nested files"));
                for (int i = 0; i < 150; i++)
                    File.WriteAllBytes(Path.Combine(i % 2 == 0 ? root : nested.FullName, i + ".bin"), new byte[i]);
                File.WriteAllBytes(Path.Combine(root, "another-149.bin"), new byte[149]);
                var report = Scanner.Scan(root, CancellationToken.None, null);
                var rows = report.Largest.ToArray();
                Require(report.Files == 151 && report.Bytes == 150 * 149 / 2 + 149, "Aggregate counts.");
                Require(report.Skipped == 0 && rows.Length == 100, "Bounded results.");
                Require(rows[0].Bytes == 149 && rows[1].Bytes == 149 && rows[99].Bytes == 51, "Top files and ties.");
                Require(Scanner.Size(1024) == "1 KiB" && Scanner.Size(0) == "0 B", "Size formatting.");
                try { Scanner.Scan(root, new CancellationToken(true), null); throw new Exception("Cancellation ignored."); }
                catch (OperationCanceledException) {}
                try { Scanner.Scan(Path.Combine(root, "missing"), CancellationToken.None, null); throw new Exception("Missing directory accepted."); }
                catch (DirectoryNotFoundException) {}
                var names = Directory.CreateDirectory(Path.Combine(root, "unicode names"));
                string[] expected = new[] { "space plus +.bin", "\uE000.bin", "\uD83D\uDCC1.bin" };
                foreach (var name in expected) File.WriteAllBytes(Path.Combine(names.FullName, name), new byte[4]);
                var sorted = Scanner.Scan(names.FullName, CancellationToken.None, null).Largest.Select(f => Path.GetFileName(f.Path));
                Require(sorted.SequenceEqual(expected.OrderBy(name => name, StringComparer.Ordinal)), "UTF-16 paths and tie order.");
                foreach (var name in expected) File.Delete(Path.Combine(names.FullName, name));
                string junction = Path.Combine(root, "junction");
                var mklink = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                    "/d /c mklink /J junction \"nested files\"") {
                    WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true
                };
                using (var process = Process.Start(mklink))
                {
                    process.WaitForExit();
                    Require(process.ExitCode == 0, "Could not create the test junction.");
                }
                try
                {
                    var links = Scanner.Scan(root, CancellationToken.None, null);
                    Require(links.Skipped == 1 && links.Files == report.Files && links.Bytes == report.Bytes, "Junction traversal.");
                    try { Scanner.Scan(junction, CancellationToken.None, null); throw new Exception("Junction root accepted."); }
                    catch (IOException) {}
                }
                finally { Directory.Delete(junction); }
                Console.WriteLine("Scanner checks passed.");
                if (!args.Contains("--ui")) return;

                Application.EnableVisualStyles();
                using (var window = new MainWindow(root))
                {
                    window.ShowInTaskbar = false;
                    window.Opacity = 0;
                    Exception failure = null;
                    window.Shown += async delegate {
                        try { await CheckWindow(window); }
                        catch (Exception ex) { failure = ex; }
                        finally { window.Close(); }
                    };
                    Application.Run(window);
                    if (failure != null) throw new Exception("UI check failed.", failure);
                    Console.WriteLine("Window scan, selection and cancellation passed.");
                }
            }
            finally
            {
                if (Directory.GetParent(root).FullName.TrimEnd('\\') == temp.TrimEnd('\\') &&
                    Path.GetFileName(root).StartsWith("storage-doctor-test-", StringComparison.Ordinal))
                    Directory.Delete(root, true);
            }
        }

        static async Task CheckWindow(MainWindow window)
        {
            var controls = ControlsBelow(window).ToArray();
            var scan = controls.OfType<Button>().Single(b => b.Text == "Scan folder");
            var list = controls.OfType<ListView>().Single();
            var reveal = controls.OfType<Button>().Single(b => b.Text == "Show in Explorer");
            scan.PerformClick();
            await WaitFor(() => list.Items.Count == 100);
            Require(controls.OfType<Label>().Any(l => l.Text.Contains("151 files")), "Summary missing.");
            Require(controls.OfType<Label>().Any(l => l.Text.StartsWith("Drive space:")), "Drive space missing.");
            list.Items[0].Selected = true;
            await WaitFor(() => reveal.Enabled);
            scan.PerformClick();
            scan.PerformClick();
            await WaitFor(() => controls.OfType<Label>().Any(l => l.Text == "Scan cancelled."));
            Require(!reveal.Enabled && scan.Enabled, "Controls are stale after cancellation.");
        }
    }
}
