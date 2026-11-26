using System;
using System.Drawing;
using System.Windows.Forms;

namespace MirrorDesk
{
    internal sealed class ToolsDialog : Form
    {
        readonly DpiLayout dpiLayout;
        readonly AppPalette palette;

        public ToolsDialog() : this(new AppPalette(0,false)) {}
        public ToolsDialog(AppPalette palette)
        {
            this.palette=palette;
            Text = "工具与关于 · 微映";
            StartPosition = FormStartPosition.CenterParent;
            Font = Theme.Font(13);
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(620,450);
            MinimumSize = new Size(400,280);
            BackColor = ConsoleColors.Canvas;
            MinimizeBox = MaximizeBox = false;
            var scroll = new Panel { Dock=DockStyle.Fill, AutoScroll=true, Name="Canvas" };
            var grid = Theme.Grid(1,8);
            grid.Dock = DockStyle.Top;
            grid.Height = 470;
            grid.Padding = new Padding(20,10,20,18);
            Theme.Cols(grid,-1);
            Theme.Rows(grid,56,57,48,48,56,77,48,40);
            grid.Controls.Add(Theme.Label("投屏延迟测试",20,ConsoleColors.Ink,true),0,0);
            grid.Controls.Add(Theme.Label("用同一台相机拍下 iPad 和电脑的计时画面，在测试页录入读数，对比 USB 与不同无线画质。",13,ConsoleColors.Secondary),0,1);
            var open=Theme.Button("在电脑上打开测试页",true);
            open.Name="OpenLatencyTest";
            open.Click+=delegate { BundledFiles.OpenBenchmark(this); };
            var save=Theme.Button("导出离线测试页…");
            save.Name="ExportLatencyTest";
            save.Margin=new Padding(0,5,0,0);
            save.Click+=delegate { BundledFiles.Export(this,true); };
            grid.Controls.Add(open,0,2);
            grid.Controls.Add(save,0,3);
            grid.Controls.Add(Theme.Label("微映  1.4",18,ConsoleColors.Ink,true),0,4);
            grid.Controls.Add(Theme.Label("Windows USB 与 AirPlay 投屏接收端，使用 iPhoneMirror v1.8.3 组件。程序附有完整源码、构建脚本和 GPL-3.0-only 许可证。",13,ConsoleColors.Secondary),0,5);
            var source=Theme.Button("导出完整源码 ZIP…");
            source.Name="ExportSource";
            source.Click+=delegate { BundledFiles.Export(this,false); };
            grid.Controls.Add(source,0,6);
            var author = new LinkLabel {
                Text="项目维护 · @GloryPkqa", Font=Theme.Font(11), Dock=DockStyle.Fill,
                TextAlign=ContentAlignment.MiddleLeft, LinkColor=ConsoleColors.Secondary,
                ActiveLinkColor=ConsoleColors.Accent, Margin=Padding.Empty
            };
            author.LinkClicked+=delegate {
                try {
                    using (System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                        "https://github.com/GloryPkqa") { UseShellExecute=true })) {}
                } catch (Exception ex) {
                    MessageBox.Show(this,ex.Message,"无法打开 GitHub 主页",MessageBoxButtons.OK,MessageBoxIcon.Warning);
                }
            };
            grid.Controls.Add(author,0,7);
            scroll.Controls.Add(grid);
            Controls.Add(scroll);
            palette.Apply(this,open);
            dpiLayout=new DpiLayout(this);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            palette.ApplyCaption(this);
            int dpi=DpiLayout.Read(this);
            dpiLayout.Apply(dpi);
            var area=Screen.FromControl(Owner ?? this).WorkingArea;
            ClientSize=new Size(Math.Min(area.Width-20,DpiLayout.Pixels(620,dpi/96f)),
                Math.Min(area.Height-50,DpiLayout.Pixels(450,dpi/96f)));
            Location=new Point(Math.Max(area.Left,Math.Min(Left,area.Right-Width)),
                Math.Max(area.Top,Math.Min(Top,area.Bottom-Height)));
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            dpiLayout?.Apply(e.DeviceDpiNew);
        }
    }
}
