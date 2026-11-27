using System;
using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MirrorDesk
{
    internal sealed class SetupPage : Panel
    {
        Guid packageId=Guid.NewGuid();
        readonly Button download=new GlassButton("下载组件（323 MiB）",true) { Name="DownloadComponents" };
        readonly Button zip=new GlassButton("导入便携 ZIP…") { Name="ImportComponents" };
        readonly Button folder=new GlassButton("选择已有目录…") { Name="ChooseComponentDirectory" };
        readonly Button copy=new GlassButton("复制路径");
        readonly Button cancel=new GlassButton("取消") { Visible=false };
        readonly ProgressBar bar=new ProgressBar { Dock=DockStyle.Fill, Margin=Padding.Empty,
            Style=ProgressBarStyle.Continuous, Visible=false, AccessibleName="组件准备总进度" };
        readonly Label status=GlassUi.Label("等待选择组件来源",10.5f,Theme.Muted);
        readonly Label directoryLabel=GlassUi.Label("组件保存位置",11,Theme.Ink,true);
        readonly System.Windows.Forms.Timer copyFeedback=new System.Windows.Forms.Timer { Interval=1800 };
        readonly TextBox directory=new TextBox {
            Dock=DockStyle.Fill, Multiline=true, ReadOnly=true, WordWrap=true,
            ScrollBars=ScrollBars.Vertical, Margin=Padding.Empty, BorderStyle=BorderStyle.FixedSingle,
            AccessibleName="组件完整路径"
        };
        AppPalette palette;
        CancellationTokenSource job;
        bool backAfterCancel, failed;

        internal bool IsPreparing { get { return job!=null; } }
        internal event Action BackRequested;
        internal event Action<string> Prepared;

        internal SetupPage()
        {
            Name="SetupPage";
            AccessibleName="准备投屏组件";
            Dock=DockStyle.Fill;
            Margin=Padding.Empty;
            DoubleBuffered=true;
            SetStyle(ControlStyles.ResizeRedraw,true);
            var scroll=new GlassPanel { Dock=DockStyle.Fill, AutoScroll=true };
            var grid=GlassUi.Grid(1,4);
            grid.Dock=DockStyle.Top; grid.AutoSize=true; grid.AutoSizeMode=AutoSizeMode.GrowAndShrink;
            grid.Padding=new Padding(21,3,21,11);
            Theme.Cols(grid,-1); Theme.Rows(grid,42,110,110,0);
            grid.RowStyles[3].SizeType=SizeType.AutoSize;
            var title=GlassUi.Label("准备投屏组件",15,Theme.Ink,true);
            title.Margin=new Padding(8,0,0,5);
            grid.Controls.Add(title,0,0);

            var sources=new ConsoleSurface(1,2) { Padding=new Padding(13,9,13,12), Margin=new Padding(0,0,0,8) };
            Theme.Cols(sources,-1); Theme.Rows(sources,42,-1);
            download.Margin=new Padding(0,0,0,4);
            sources.Controls.Add(download,0,0);
            var offline=GlassUi.Grid(2,1);
            offline.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
            offline.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
            Theme.Rows(offline,-1);
            zip.Margin=new Padding(0,0,4,0); folder.Margin=new Padding(4,0,0,0);
            offline.Controls.Add(zip,0,0); offline.Controls.Add(folder,1,0);
            sources.Controls.Add(offline,0,1);
            grid.Controls.Add(sources,0,1);

            var paths=new ConsoleSurface(2,2) { Padding=new Padding(13,6,13,11), Margin=new Padding(0,0,0,8) };
            Theme.Cols(paths,-1,91); Theme.Rows(paths,34,-1);
            paths.Controls.Add(directoryLabel,0,0);
            copy.Font=Theme.Font(10.5f);
            copy.Margin=new Padding(4,0,0,2);
            paths.Controls.Add(copy,1,0);
            directory.Text=RuntimePack.PackageDirectory(packageId);
            directory.Font=Theme.Font(10.5f);
            directory.Margin=new Padding(0,3,0,0);
            paths.Controls.Add(directory,0,1);
            paths.SetColumnSpan(directory,2);
            grid.Controls.Add(paths,0,2);

            var progress=new ConsoleSurface(2,2) { Padding=new Padding(13,6,13,10), Margin=Padding.Empty,
                AutoSize=true, AutoSizeMode=AutoSizeMode.GrowAndShrink };
            Theme.Cols(progress,-1,91); Theme.Rows(progress,0,7);
            progress.RowStyles[0].SizeType=SizeType.AutoSize;
            status.AutoSize=true; status.AutoEllipsis=false; status.MinimumSize=new Size(0,35);
            status.UseMnemonic=false; status.Margin=new Padding(0,0,6,0);
            progress.Controls.Add(status,0,0);
            progress.Controls.Add(bar,0,1);
            progress.SetColumnSpan(bar,2);
            cancel.Font=Theme.Font(10.5f);
            cancel.Margin=new Padding(0,0,0,2);
            progress.Controls.Add(cancel,1,0);
            grid.Controls.Add(progress,0,3);
            scroll.Controls.Add(grid);
            Controls.Add(scroll);

            copyFeedback.Tick+=delegate { copyFeedback.Stop(); copy.Text="复制路径"; };
            copy.Click+=delegate {
                try {
                    Clipboard.SetText(directory.Text); copy.Text="已复制"; copyFeedback.Stop(); copyFeedback.Start();
                } catch (System.Runtime.InteropServices.ExternalException) {
                    directory.Focus(); directory.SelectAll();
                    MessageBox.Show(this,"完整路径已选中，可按 Ctrl+C 复制。","复制组件路径");
                }
            };
            download.Click+=async delegate { await Prepare(null,false); };
            zip.Click+=async delegate {
                using (var pick=new OpenFileDialog { Filter="官方便携包 (*.zip)|*.zip", Title="选择 iPhoneMirror v1.8.3 便携 ZIP" })
                    if (pick.ShowDialog(FindForm())==DialogResult.OK) await Prepare(pick.FileName,false);
            };
            folder.Click+=async delegate {
                using (var pick=new FolderBrowserDialog { Description="选择已安装的 iPhoneMirror v1.8.3 文件夹", ShowNewFolderButton=false })
                    if (pick.ShowDialog(FindForm())==DialogResult.OK) await Prepare(pick.SelectedPath,true);
            };
            cancel.Click+=delegate { CancelPreparation(); };
        }

        internal void ApplyPalette(AppPalette value)
        {
            palette=value;
            palette.StyleButton(download,true);
            status.ForeColor=failed ? palette.Alert : palette.Muted;
        }

        protected override void OnPaintBackground(PaintEventArgs e) { GlassUi.ControlBackground(e.Graphics,this); }

        internal void RequestBack()
        {
            if (!IsPreparing) { BackRequested?.Invoke(); return; }
            backAfterCancel=true;
            CancelPreparation();
        }

        internal void CancelPreparation()
        {
            if (!IsPreparing) return;
            status.Text="正在取消…";
            cancel.Enabled=false;
            job.Cancel();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) {
                job?.Cancel();
                copyFeedback.Dispose();
            }
            base.Dispose(disposing);
        }

        async Task Prepare(string path,bool isFolder)
        {
            if (IsPreparing) return;
            var operation=new CancellationTokenSource();
            job=operation;
            var token=operation.Token;
            directory.Text=isFolder ? path : RuntimePack.PackageDirectory(packageId);
            directoryLabel.Text=isFolder ? "所选组件目录" : "组件保存位置";
            download.Enabled=zip.Enabled=folder.Enabled=false;
            cancel.Visible=true;
            bar.Value=0; bar.Visible=true;
            failed=false; status.ForeColor=palette.Muted;
            status.Text=isFolder ? "正在检查组件目录…" : "正在准备组件…";
            string ready=null;
            try {
                var reporter=new Progress<RuntimeProgress>(p=>{
                    if (job!=operation || IsDisposed || token.IsCancellationRequested) return;
                    status.Text=p.Message;
                    if (p.Percent>0) {
                        bar.Value=Math.Min(100,p.Percent);
                        status.Text=p.Message+"  "+bar.Value+"%";
                    }
                });
                ready=isFolder
                    ? await Task.Run(()=>RuntimePack.UseDirectory(path,token),token)
                    : await RuntimePack.PreparePackageAsync(packageId,path,reporter,token);
                if (!isFolder) packageId=Guid.NewGuid();
                token.ThrowIfCancellationRequested();
            } catch (OperationCanceledException) {
                ready=null;
                if (!IsDisposed) status.Text="已取消。";
            } catch (Exception ex) {
                if (!IsDisposed) {
                    status.Text=ex.GetBaseException().Message;
                    failed=true; status.ForeColor=palette.Alert;
                }
            } finally {
                job=null;
                operation.Dispose();
            }

            if (IsDisposed) return;
            download.Enabled=zip.Enabled=folder.Enabled=cancel.Enabled=true;
            bar.Visible=cancel.Visible=false;
            directoryLabel.Text="组件保存位置";
            directory.Text=RuntimePack.PackageDirectory(packageId);
            if (backAfterCancel) {
                backAfterCancel=false;
                BackRequested?.Invoke();
            } else if (ready!=null) {
                status.Text="组件已准备完成。";
                Prepared?.Invoke(ready);
            }
        }
    }
}
