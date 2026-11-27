using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

[assembly: AssemblyTitle("微映 · WeiMirror")]
[assembly: AssemblyProduct("WeiMirror")]
[assembly: AssemblyDescription("iPad 和 iPhone 的 USB / AirPlay 投屏接收端")]
[assembly: AssemblyVersion("1.4.5.0")]
[assembly: AssemblyFileVersion("1.4.5.0")]

namespace MirrorDesk
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--pipe") {
                Environment.ExitCode = AirPlay720Host.Run(args);
                return;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var engine = args.Length >= 2 && args[0] == "--engine" ? args[1] : null;
            using (var window = new MainForm(engine, PreferencesStore.ForCurrentUser()))
            {
                if (engine != null && args.Length==4 && args[2]=="--view") window.RestoreView(args[3]);
                Application.Run(window);
            }
        }
    }

    internal static class Theme
    {
        public static readonly Color Ink = Color.FromArgb(30, 43, 54);
        public static readonly Color Muted = Color.FromArgb(70, 88, 101);
        public static readonly Color Accent = Color.FromArgb(0, 105, 110);
        public static readonly Color Line = Color.FromArgb(207, 219, 222);
        public static readonly Color Stage = Color.FromArgb(18, 31, 39);
        public static Font Font(float size, bool bold = false)
        {
            return new Font("Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point);
        }
        public static Label Label(string value, float size, Color color, bool bold = false)
        {
            return new Label { Text = value, Font = Font(size, bold), ForeColor = color,
                TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Dock = DockStyle.Fill, Margin = Padding.Empty };
        }
        public static Button Button(string value, bool accent = false)
        {
            var b = new Button { Text = value, Font = Font(13f, accent), FlatStyle = FlatStyle.Flat,
                UseVisualStyleBackColor = false, BackColor = accent ? Accent : Color.White,
                ForeColor = accent ? Color.White : Ink, AutoEllipsis = true, Cursor = Cursors.Hand,
                Dock = DockStyle.Fill, Margin = Padding.Empty, Height = 36, TabStop = true };
            b.FlatAppearance.BorderColor = accent ? Accent : Line;
            b.FlatAppearance.MouseOverBackColor = accent ? Color.FromArgb(0, 88, 92) : Color.FromArgb(235, 245, 245);
            return b;
        }
        public static TableLayoutPanel Grid(int cols, int rows)
        {
            return new TableLayoutPanel { ColumnCount = cols, RowCount = rows, Dock = DockStyle.Fill,
                Margin = Padding.Empty, Padding = Padding.Empty, GrowStyle = TableLayoutPanelGrowStyle.FixedSize };
        }
        public static void Rows(TableLayoutPanel t, params int[] sizes)
        {
            foreach (int size in sizes)
                t.RowStyles.Add(new RowStyle(size < 0 ? SizeType.Percent : SizeType.Absolute, size < 0 ? 100 : size));
        }
        public static void Cols(TableLayoutPanel t, params int[] sizes)
        {
            foreach (int size in sizes)
                t.ColumnStyles.Add(new ColumnStyle(size < 0 ? SizeType.Percent : SizeType.Absolute, size < 0 ? 100 : size));
        }
    }

    internal static class BundledFiles
    {
        const string Benchmark = "WeiMirror.LatencyTest.html";

        internal static void Write(string resource, string path)
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(path));
            string temp = Path.Combine(dir, ".weimirror-" + Guid.NewGuid().ToString("N") + ".tmp");
            bool ownsTemp=false;
            try {
                using (var source = typeof(BundledFiles).Assembly.GetManifestResourceStream(resource)) {
                    if (source == null) throw new FileNotFoundException("程序缺少所需的内置文件，请重新下载微映。");
                    using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                        ownsTemp=true;
                        source.CopyTo(output);
                    }
                }
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            } finally { if (ownsTemp && File.Exists(temp)) File.Delete(temp); }
        }

        public static void OpenBenchmark(IWin32Window owner)
        {
            try {
                string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WeiMirror", "tools", "1.4");
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder,"WeiMirror-Latency.html");
                Write(Benchmark,path);
                using (Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })) {}
            } catch (Exception ex) { MessageBox.Show(owner, ex.Message, "无法打开延迟测试页", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        public static void Export(IWin32Window owner, bool benchmark)
        {
            using (var picker = new SaveFileDialog {
                FileName = benchmark ? "WeiMirror-Latency.html" : "WeiMirror-1.4.5-source.zip",
                Filter = benchmark ? "离线测试页 (*.html)|*.html" : "源代码归档 (*.zip)|*.zip", AddExtension = true })
            {
                if (picker.ShowDialog(owner) != DialogResult.OK) return;
                try { Write(benchmark ? Benchmark : "WeiMirror.Source.zip", picker.FileName); }
                catch (Exception ex) { MessageBox.Show(owner, ex.Message, "文件导出失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }
    }
}
