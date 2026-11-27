using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
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
                var settings=(SettingsDialog)typeof(MainForm).GetField("settingsDialog",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(main);
                foreach (int dpi in new[]{96,120,144,192,96}) {
                    main.ScaleConsole(dpi);
                    main.ClientSize=new Size(620*dpi/96,440);
                    main.PerformLayout();
                    var viewport=main.Controls.Find("Canvas",true).Single();
                    viewport.PerformLayout();
                    var dashboard=viewport.Controls[0];
                    dashboard.PerformLayout();
                    foreach (Control card in dashboard.Controls) card.PerformLayout();
                    foreach (string label in new[]{"USB 数据线","无线 AirPlay","设置","显示投屏窗口"}) {
                        var field=FindText(main,label);
                        Check(field!=null && field.ClientSize.Height>=field.Font.Height+2 &&
                            field.Parent.ClientRectangle.Contains(field.Bounds),label+" fits at "+dpi+" DPI");
                    }
                    Check(dpi<144 || dashboard.Height>viewport.ClientSize.Height,
                        "compact dashboard scrolls when needed at "+dpi+" DPI");
                    settings.ApplyInterface(.9f,1.2f,"Segoe UI");
                    settings.ScaleSettings(dpi);
                    settings.ClientSize=new Size(434*dpi/96,320*dpi/96);
                    settings.PerformLayout();
                    foreach (string label in new[]{"投屏","外观","组件","深色模式","始终显示投屏工具栏"}) {
                        var field=FindText(settings,label);
                        Check(field!=null && field.Font.Name=="Segoe UI" && field.Height>=field.Font.Height+2 &&
                            field.Width>=TextRenderer.MeasureText(label,field.Font).Width,
                            label+" fits small settings at 120% text and "+dpi+" DPI");
                    }
                }
                var dark=(CheckBox)FindText(settings,"深色模式");
                var theme=(ComboBox)settings.Controls.Find("AppearanceTheme",true).Single();
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
