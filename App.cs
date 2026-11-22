using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace StorageDoctor
{
    internal sealed class MainWindow : Form
    {
        readonly TextBox folder = new TextBox { ReadOnly = true, Dock = DockStyle.Fill };
        readonly Button browse = new Button { Text = "Choose folder..." };
        readonly Button scan = new Button { Text = "Scan folder" };
        readonly Button reveal = new Button { Text = "Show in Explorer", Enabled = false };
        readonly Button settings = new Button { Text = "Windows storage settings" };
        readonly Label space = new Label();
        readonly Label status = new Label();
        readonly ListView files = new ListView { View = View.Details, Dock = DockStyle.Fill,
            FullRowSelect = true, MultiSelect = false, HideSelection = false, ShowItemToolTips = true };
        CancellationTokenSource active;

        internal MainWindow(string initial)
        {
            Text = "Storage Doctor";
            Font = new Font("Segoe UI", 10);
            AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(800, 500);
            MinimumSize = new Size(610, 410);
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, RowCount = 6 };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int row = 0; row < 6; row++)
                panel.RowStyles.Add(new RowStyle(row == 3 ? SizeType.Percent : SizeType.AutoSize, row == 3 ? 100 : 0));
            Controls.Add(panel);
            var selector = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3 };
            selector.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            selector.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            selector.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            folder.AccessibleName = "Folder to scan";
            folder.Text = initial;
            selector.Controls.Add(folder, 0, 0);
            selector.Controls.Add(browse, 1, 0);
            selector.Controls.Add(scan, 2, 0);
            panel.Controls.Add(selector, 0, 0);
            space.AutoSize = true;
            panel.Controls.Add(space, 0, 1);
            panel.Controls.Add(new Label { Text = "Largest files", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(3, 14, 3, 4) }, 0, 2);
            files.Columns.Add("Size", 105);
            files.Columns.Add("File", 205);
            files.Columns.Add("Folder", 385);
            files.AccessibleName = "Largest files by size";
            panel.Controls.Add(files, 0, 3);
            status.AutoSize = true;
            status.Dock = DockStyle.Fill;
            status.Text = "Choose a folder and start a scan.";
            panel.Controls.Add(status, 0, 4);
            var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
            actions.Controls.Add(reveal);
            actions.Controls.Add(settings);
            panel.Controls.Add(actions, 0, 5);
            foreach (var button in new[] { browse, scan, reveal, settings }) button.AutoSize = true;
            browse.Click += delegate {
                using (var dialog = new FolderBrowserDialog { SelectedPath = folder.Text, ShowNewFolderButton = false })
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    folder.Text = dialog.SelectedPath;
                    files.Items.Clear();
                    reveal.Enabled = false;
                    status.Text = "Ready to scan.";
                    UpdateSpace();
                }
            };
            scan.Click += async delegate { await StartOrCancel(); };
            files.SelectedIndexChanged += delegate { reveal.Enabled = files.SelectedItems.Count == 1; };
            reveal.Click += delegate { RevealFile(); };
            files.DoubleClick += delegate { RevealFile(); };
            settings.Click += delegate { Open(new ProcessStartInfo("ms-settings:storagesense") { UseShellExecute = true }); };
            FormClosing += delegate { if (active != null) active.Cancel(); };
            Shown += async delegate {
                UpdateSpace();
                try { await Scanner.Prepare(); }
                catch (Exception ex) { if (!IsDisposed && active == null) status.Text = "Could not load the scan engine. " + ex.Message; }
            };
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        static extern bool GetDiskFreeSpaceExW(string directory, out long available, out long capacity, out long free);

        void UpdateSpace()
        {
            long available, capacity, free;
            space.Text = GetDiskFreeSpaceExW(folder.Text.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, out available, out capacity, out free)
                ? String.Format("Drive space: {0} available of {1}", Scanner.Size(available), Scanner.Size(capacity))
                : "Free space unavailable for this location.";
        }

        async Task StartOrCancel()
        {
            if (active != null)
            {
                active.Cancel();
                scan.Enabled = false;
                status.Text = "Cancelling...";
                return;
            }
            var run = new CancellationTokenSource();
            active = run;
            string path = folder.Text;
            files.Items.Clear();
            reveal.Enabled = false;
            browse.Enabled = false;
            scan.Text = "Cancel scan";
            status.Text = "Scanning...";
            var progress = new Progress<long>(count => {
                if (!IsDisposed && active == run && !run.IsCancellationRequested)
                    status.Text = String.Format("Scanning... {0:N0} files counted.", count);
            });
            try
            {
                var result = await Task.Run(() => Scanner.Scan(path, run.Token, progress));
                run.Token.ThrowIfCancellationRequested();
                if (IsDisposed) return;
                files.BeginUpdate();
                try
                {
                    foreach (var file in result.Largest)
                    {
                        var row = new ListViewItem(new[] { Scanner.Size(file.Bytes), Path.GetFileName(file.Path), Path.GetDirectoryName(file.Path) });
                        row.Tag = file.Path;
                        row.ToolTipText = file.Path;
                        files.Items.Add(row);
                    }
                }
                finally { files.EndUpdate(); }
                status.Text = String.Format("{0:N0} files totaling {1}. Showing {2:N0} largest. Skipped entries: {3:N0}.",
                    result.Files, Scanner.Size(result.Bytes), result.Largest.Count, result.Skipped);
            }
            catch (OperationCanceledException) { if (!IsDisposed) status.Text = "Scan cancelled."; }
            catch (Exception ex) { if (!IsDisposed) status.Text = "Scan failed. " + ex.Message; }
            finally
            {
                active = null;
                run.Dispose();
                if (!IsDisposed)
                {
                    scan.Text = "Scan folder";
                    scan.Enabled = browse.Enabled = true;
                    UpdateSpace();
                }
            }
        }

        void RevealFile()
        {
            if (files.SelectedItems.Count != 1) return;
            string path = (string)files.SelectedItems[0].Tag;
            if (!File.Exists(path)) { status.Text = "File no longer available. Scan again to refresh."; return; }
            Open(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"),
                "/select,\"" + path + "\"") { UseShellExecute = false });
        }

        void Open(ProcessStartInfo info)
        {
            try { using (Process.Start(info)) {} }
            catch (Exception ex) { status.Text = "Could not open Windows tool. " + ex.Message; }
        }
    }

    internal static class App
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainWindow(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
        }
    }
}
