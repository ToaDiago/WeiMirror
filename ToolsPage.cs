using System;
using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace MirrorDesk
{
    internal sealed class GlassLinkLabel : LinkLabel
    {
        protected override void OnPaintBackground(PaintEventArgs e) { GlassUi.ControlBackground(e.Graphics,this); }
    }

    internal sealed partial class MainForm
    {
        Control BuildToolsPage()
        {
            var grid=GlassUi.Grid(1,8);
            grid.Dock=DockStyle.Top;
            grid.Padding=new Padding(10,3,11,4);
            Theme.Cols(grid,-1);
            int[] rows={ 31,51,41,8,30,56,39,24 };
            Theme.Rows(grid,rows);
            grid.Height=rows.Sum()+grid.Padding.Vertical;

            grid.Controls.Add(GlassUi.Label("投屏延迟测试",14,Theme.Ink,true),0,0);
            grid.Controls.Add(GlassUi.Label("用同一台相机拍下设备和电脑的计时画面，再录入读数对比延迟。",
                10.5f,Theme.Muted),0,1);
            var actions=GlassUi.Grid(2,1);
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
            Theme.Rows(actions,-1);
            var open=new GlassButton("打开测试页") { Name="OpenLatencyTest", Margin=new Padding(0,0,4,0) };
            open.Click+=delegate { BundledFiles.OpenBenchmark(this); };
            var save=new GlassButton("导出离线页…") { Name="ExportLatencyTest", Margin=new Padding(4,0,0,0) };
            save.Click+=delegate { BundledFiles.Export(this,true); };
            actions.Controls.Add(open,0,0);
            actions.Controls.Add(save,1,0);
            grid.Controls.Add(actions,0,2);

            var version=Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
            grid.Controls.Add(GlassUi.Label("关于微映  "+version,13,Theme.Ink,true),0,4);
            grid.Controls.Add(GlassUi.Label("投屏组件  iPhoneMirror v1.8.3\n许可证  GPL-3.0-only",
                10.5f,Theme.Muted),0,5);
            var source=new GlassButton("导出完整源码 ZIP…") { Name="ExportSource" };
            source.Click+=delegate { BundledFiles.Export(this,false); };
            grid.Controls.Add(source,0,6);
            var author=new GlassLinkLabel {
                Text="项目维护 · @GloryPkqa", Font=Theme.Font(10.5f), Dock=DockStyle.Fill,
                TextAlign=ContentAlignment.MiddleLeft, ActiveLinkColor=Theme.Accent, Margin=Padding.Empty
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
            return ScrollSettings(grid);
        }
    }
}
