using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MirrorDesk
{
    internal sealed class SetupDialog : Form
    {
        static readonly Color HeaderColor = Color.FromArgb(25,52,58);
        static readonly Color AccentColor = Color.FromArgb(8,124,116);
        static readonly Color WashColor = Color.FromArgb(239,245,243);
        static readonly Color InkColor = Color.FromArgb(24,47,50);
        static readonly Color MutedColor = Color.FromArgb(71,98,96);
        static readonly Color LineColor = Color.FromArgb(208,222,218);
        readonly Guid packageId = Guid.NewGuid();
        public string EngineDirectory { get; private set; }
        readonly Button download = Theme.Button("下载组件（323 MiB）", true);
        readonly Button zip = Theme.Button("导入便携 ZIP…");
        readonly Button folder = Theme.Button("选择已有目录…");
        readonly Button copy = Theme.Button("复制路径");
        readonly Button cancel = Theme.Button("关闭");
        readonly ProgressBar bar = new ProgressBar { Dock = DockStyle.Fill, Margin = Padding.Empty, Style = ProgressBarStyle.Continuous, Visible = false, AccessibleName = "组件准备总进度" };
        readonly Label status = Theme.Label("等待选择组件来源", 13, MutedColor);
        readonly Label progressValue = Theme.Label("",13,AccentColor,true);
        readonly Label directoryLabel = Theme.Label("组件保存位置", 16, InkColor, true);
        readonly System.Windows.Forms.Timer copyFeedback = new System.Windows.Forms.Timer { Interval = 1800 };
        readonly TextBox directory = new TextBox {
            Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, WordWrap = true,
            ScrollBars = ScrollBars.Vertical, BackColor = Color.White, Margin = Padding.Empty,
            AccessibleName = "组件完整路径"
        };
        CancellationTokenSource job;
        bool closing;

        public SetupDialog()
        {
            SuspendLayout();
            Text = "准备投屏组件 · 微映";
            Font = Theme.Font(13);
            AutoScaleDimensions = new SizeF(96,96);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(784, 558);
            MinimumSize = new Size(420, 300);
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            BackColor = WashColor;
            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = WashColor };
            var grid = Theme.Grid(1,4);
            grid.Dock = DockStyle.Top; grid.AutoSize = true; grid.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Theme.Cols(grid,-1); Theme.Rows(grid,100,184,148,0);
            grid.RowStyles[3].SizeType=SizeType.AutoSize;
            var heading=Theme.Grid(1,2);
            heading.Padding=new Padding(24,10,24,12); heading.BackColor=HeaderColor;
            Theme.Cols(heading,-1); Theme.Rows(heading,48,-1);
            heading.Controls.Add(Theme.Label("准备投屏组件",24,Color.White,true),0,0);
            heading.Controls.Add(Theme.Label("iPhoneMirror v1.8.3  ·  Windows x64",13,Color.FromArgb(191,222,216)),0,1);
            grid.Controls.Add(heading,0,0);

            var sources=Theme.Grid(1,3);
            sources.Margin=new Padding(16,14,16,8); sources.Padding=new Padding(14,5,14,13);
            sources.BackColor=Color.White; Theme.Cols(sources,-1); Theme.Rows(sources,40,-1,48);
            sources.Controls.Add(Theme.Label("组件来源",16,InkColor,true),0,0);
            StyleButton(download,true);
            download.Margin=new Padding(0,2,0,8); sources.Controls.Add(download,0,1);
            var offline=Theme.Grid(2,1);
            offline.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
            offline.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50)); Theme.Rows(offline,-1);
            StyleButton(zip); StyleButton(folder);
            zip.Margin=new Padding(0,0,4,0); folder.Margin=new Padding(4,0,0,0);
            offline.Controls.Add(zip,0,0); offline.Controls.Add(folder,1,0);
            sources.Controls.Add(offline,0,2); grid.Controls.Add(sources,0,1);

            var paths=Theme.Grid(2,2);
            paths.Margin=new Padding(16,0,16,8); paths.Padding=new Padding(14,5,14,12);
            paths.BackColor=Color.White; Theme.Cols(paths,-1,114); Theme.Rows(paths,42,-1);
            paths.Controls.Add(directoryLabel,0,0); StyleButton(copy);
            copy.Margin=new Padding(8,1,0,3); paths.Controls.Add(copy,1,0);
            directory.Text = RuntimePack.PackageDirectory(packageId);
            directory.Font=Theme.Font(13); directory.ForeColor=InkColor;
            directory.BackColor=Color.FromArgb(247,250,249); directory.BorderStyle=BorderStyle.FixedSingle;
            directory.Margin=new Padding(0,3,0,0);
            paths.Controls.Add(directory,0,1); paths.SetColumnSpan(directory,2); grid.Controls.Add(paths,0,2);

            var progress=Theme.Grid(2,3);
            progress.Padding=new Padding(14,4,14,12); progress.Margin=new Padding(16,0,16,14);
            progress.BackColor=Color.White; progress.AutoSize=true; progress.AutoSizeMode=AutoSizeMode.GrowAndShrink;
            Theme.Cols(progress,-1,108); Theme.Rows(progress,0,8,46);
            progress.RowStyles[0].SizeType=SizeType.AutoSize;
            status.AutoSize=true; status.AutoEllipsis=false; status.MinimumSize=new Size(0,38);
            status.UseMnemonic=false; status.Margin=new Padding(0,0,6,0);
            progress.Controls.Add(status,0,0);
            progressValue.TextAlign=ContentAlignment.MiddleRight; progress.Controls.Add(progressValue,1,0);
            progress.Controls.Add(bar,0,1); progress.SetColumnSpan(bar,2);
            StyleButton(cancel); cancel.Margin=new Padding(0,6,0,0); progress.Controls.Add(cancel,1,2);
            grid.Controls.Add(progress,0,3);
            scroll.Controls.Add(grid); Controls.Add(scroll);
            ResumeLayout(true);
            Shown += delegate { download.Focus(); };
            copyFeedback.Tick += delegate { copyFeedback.Stop(); copy.Text="复制路径"; };
            copy.Click += delegate {
                try {
                    Clipboard.SetText(directory.Text); copy.Text="已复制"; copyFeedback.Stop(); copyFeedback.Start();
                }
                catch (System.Runtime.InteropServices.ExternalException) {
                    directory.Focus(); directory.SelectAll();
                    MessageBox.Show(this,"完整路径已选中，可按 Ctrl+C 复制。","复制组件路径");
                }
            };
            download.Click += async delegate { await Prepare(null, false); };
            zip.Click += async delegate {
                using (var pick = new OpenFileDialog { Filter = "官方便携包 (*.zip)|*.zip", Title = "选择 iPhoneMirror v1.8.3 便携 ZIP" })
                    if (pick.ShowDialog(this) == DialogResult.OK) await Prepare(pick.FileName, false);
            };
            folder.Click += async delegate {
                using (var pick = new FolderBrowserDialog { Description = "选择已安装的 iPhoneMirror v1.8.3 文件夹", ShowNewFolderButton = false })
                    if (pick.ShowDialog(this) == DialogResult.OK) await Prepare(pick.SelectedPath, true);
            };
            cancel.Click += delegate { Close(); };
            FormClosing += delegate(object s, FormClosingEventArgs e) {
                if (job == null) return;
                e.Cancel = true; closing = true;
                status.Text = "正在取消…"; cancel.Enabled = false;
                job.Cancel();
            };
        }

        static void StyleButton(Button button, bool primary=false)
        {
            button.Font=Theme.Font(13,primary);
            button.BackColor=primary ? AccentColor : Color.White;
            button.ForeColor=primary ? Color.White : InkColor;
            button.FlatAppearance.BorderColor=primary ? AccentColor : LineColor;
            button.FlatAppearance.MouseOverBackColor=primary ? Color.FromArgb(6,103,96) : Color.FromArgb(220,239,233);
            button.FlatAppearance.MouseDownBackColor=primary ? HeaderColor : Color.FromArgb(205,226,219);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) copyFeedback.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Rectangle area = Screen.FromControl(Owner ?? this).WorkingArea;
            MinimumSize = new Size(Math.Min(MinimumSize.Width,area.Width),Math.Min(MinimumSize.Height,area.Height));
            Size = new Size(Math.Min(Width,area.Width),Math.Min(Height,area.Height));
            Location = new Point(Math.Max(area.Left,Math.Min(Left,area.Right-Width)),
                Math.Max(area.Top,Math.Min(Top,area.Bottom-Height)));
        }

        async Task Prepare(string path, bool isFolder)
        {
            if (job != null) return;
            job = new CancellationTokenSource();
            var operation = job;
            var token = operation.Token;
            directory.Text = isFolder ? path : RuntimePack.PackageDirectory(packageId);
            directoryLabel.Text = isFolder ? "所选组件目录" : "组件保存位置";
            download.Enabled = zip.Enabled = folder.Enabled = false;
            cancel.Text = "取消";
            bar.Value = 0; bar.Visible = true;
            progressValue.Text=""; status.ForeColor=InkColor;
            status.Text = isFolder ? "正在检查组件目录…" : "正在准备组件…";
            try
            {
                var reporter = new Progress<RuntimeProgress>(p => {
                    if (job != operation || IsDisposed || token.IsCancellationRequested) return;
                    status.Text = p.Message;
                    if (p.Percent > 0) {
                        bar.Value=Math.Min(100,p.Percent);
                        progressValue.Text=bar.Value+"%";
                    }
                });
                EngineDirectory = isFolder
                    ? await Task.Run(() => RuntimePack.UseDirectory(path, token), token)
                    : await RuntimePack.PreparePackageAsync(packageId, path, reporter, token);
                if (token.IsCancellationRequested) EngineDirectory = null;
            }
            catch (OperationCanceledException) { status.Text = "已取消。"; }
            catch (Exception ex) { status.Text = ex.GetBaseException().Message; status.ForeColor=Color.FromArgb(147,57,38); }
            finally
            {
                job.Dispose(); job = null;
                download.Enabled = zip.Enabled = folder.Enabled = cancel.Enabled = true;
                bar.Visible=false; progressValue.Text="";
                cancel.Text = "关闭";
            }
            if (EngineDirectory != null) {
                directory.Text = EngineDirectory;
                DialogResult = DialogResult.OK; Close();
            }
            else if (closing) BeginInvoke(new Action(Close));
            else {
                directoryLabel.Text = "组件保存位置";
                directory.Text = RuntimePack.PackageDirectory(packageId);
            }
        }
    }
}
