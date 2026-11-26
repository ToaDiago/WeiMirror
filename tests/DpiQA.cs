using System;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MirrorDesk;

static class DpiQA
{
    static void Check(bool passed,string message)
    {
        if (!passed) throw new InvalidOperationException(message);
        Console.WriteLine("PASS "+message);
    }

    static Control FindText(Control root,string text)
    {
        if (root.Text==text) return root;
        foreach (Control child in root.Controls) {
            var match=FindText(child,text);
            if (match!=null) return match;
        }
        return null;
    }

    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] static extern bool TerminateProcess(IntPtr process,uint code);

    [STAThread]
    static int Main()
    {
        int result=0;
        try {
            Application.EnableVisualStyles();
            using (var main=new MainForm(null)) {
                foreach (int dpi in new[]{96,120,144,192,96}) {
                    main.ScaleConsole(dpi);
                    main.ClientSize=new Size(Math.Min(1280,1040*dpi/96),720);
                    main.PerformLayout();
                    var viewport=main.Controls.Find("Canvas",true).Single();
                    viewport.PerformLayout();
                    var dashboard=viewport.Controls[0];
                    dashboard.PerformLayout();
                    foreach (Control card in dashboard.Controls) card.PerformLayout();
                    foreach (string label in new[]{"USB 数据线","深色模式","始终显示投屏工具栏","工具 / 关于"}) {
                        var field=FindText(main,label);
                        Check(field!=null && field.ClientSize.Height>=field.Font.Height+2 &&
                            field.Parent.ClientRectangle.Contains(field.Bounds),label+" fits at "+dpi+" DPI");
                    }
                    Check(dpi<144 || dashboard.Height>viewport.ClientSize.Height,
                        "compact dashboard scrolls when needed at "+dpi+" DPI");
                }
                var dark=(CheckBox)FindText(main,"深色模式");
                var theme=(ComboBox)main.Controls.Find("AppearanceTheme",true).Single();
                Color previous=main.BackColor;
                theme.SelectedIndex=1;
                Check(main.BackColor!=previous,"selecting Space recolors the control panel");
                previous=main.BackColor;
                dark.Checked=true;
                Check(main.BackColor!=previous && main.BackColor.GetBrightness()<.25 && theme.SelectedIndex==1,
                    "Space can independently switch to dark mode");
            }
            using (var preview=new PreviewWindow { Opacity=0,ShowInTaskbar=false }) {
                preview.Show();
                Application.DoEvents();
                var toolbar=preview.Controls.Find("PreviewToolbar",true).Single();
                var hwnd=preview.Surface.Handle;
                Check(preview.Surface.ClientSize.Height==preview.ClientSize.Height,
                    "auto-hide uses the entire video area");
                preview.AlwaysShowToolbar=true;
                Check(toolbar.Visible && preview.Surface.ClientSize.Height+toolbar.Height==preview.ClientSize.Height &&
                    preview.Surface.Handle==hwnd,"persistent toolbar reserves its own area without replacing the surface");
                preview.ToggleFullscreen();
                Check(!toolbar.Visible && preview.Surface.ClientSize.Height==preview.ClientSize.Height,
                    "fullscreen hides persistent chrome");
                preview.ToggleFullscreen();
                Check(toolbar.Visible,"leaving fullscreen restores the toolbar choice");
            }
        } catch(Exception ex) { Console.Error.WriteLine(ex); result=1; }
        Console.Out.Flush(); Console.Error.Flush();
        TerminateProcess(GetCurrentProcess(),(uint)result);
        return result;
    }
}
