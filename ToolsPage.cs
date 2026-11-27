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

    internal sealed class GlassSectionRule : Control
    {
        public GlassSectionRule()
        {
            Dock=DockStyle.Fill;
            Margin=Padding.Empty;
            TabStop=false;
            DoubleBuffered=true;
        }

        protected override void OnPaintBackground(PaintEventArgs e) { GlassUi.ControlBackground(e.Graphics,this); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var palette=(FindForm() as IGlassHost)?.GlassPalette;
            if (palette==null || SystemInformation.HighContrast) return;
            using (var rule=new Pen(Color.FromArgb(33,palette.Ink)))
                e.Graphics.DrawLine(rule,0,Height/2,Width-1,Height/2);
        }
    }

    internal sealed partial class MainForm
    {
        Control BuildToolsPage()
        {
            var grid=GlassUi.Grid(1,8);
            grid.Dock=DockStyle.Top;
            grid.Padding=new Padding(10,4,11,8);
            Theme.Cols(grid,-1);
            int[] rows={ 32,42,38,20,31,48,36,28 };
            Theme.Rows(grid,rows);
            grid.Height=rows.Sum()+grid.Padding.Vertical;

            grid.Controls.Add(GlassUi.Label("投屏延迟测试",13,Theme.Ink,true),0,0);
            var instructions=GlassUi.Label("用相机同时拍下设备和电脑屏幕，再输入读数对比延迟。",10.5f,Theme.Muted);
            instructions.Name="Muted";
            grid.Controls.Add(instructions,0,1);
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
            grid.Controls.Add(new GlassSectionRule(),0,3);

            var version=Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
            grid.Controls.Add(GlassUi.Label("微映  "+version,13,Theme.Ink,true),0,4);
            var package=GlassUi.Label("投屏组件  iPhoneMirror v1.8.3\n许可证  GPL-3.0-only",10.5f,Theme.Muted);
            package.Name="Muted";
            grid.Controls.Add(package,0,5);
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
