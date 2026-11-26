using System;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MirrorDesk;

internal static class PreviewLayoutQA
{
    static int count;
    static void Check(bool passed,string message) {
        if (!passed) throw new InvalidOperationException(message);
        Console.WriteLine("PASS "+message); count++;
    }
    static bool Ratio(PreviewWindow win,double ratio) {
        var s=win.Surface.ClientSize;
        return s.Height>0 && Math.Abs((double)s.Width/s.Height-ratio)<.012;
    }
    static void ToggleAspect(PreviewWindow win) {
        var key=Message.Create(win.Handle,0x100,(IntPtr)(int)(Keys.Control|Keys.R),IntPtr.Zero);
        typeof(PreviewWindow).GetMethod("ProcessCmdKey",BindingFlags.Instance|BindingFlags.NonPublic)
            .Invoke(win,new object[]{key,Keys.Control|Keys.R});
    }
    static void ResizeEvent(PreviewWindow win,string name) {
        typeof(PreviewWindow).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(win,new object[]{EventArgs.Empty});
    }
    static PreviewWindow Open(string token) {
        var win=new PreviewWindow { Opacity=0,ShowInTaskbar=false };
        win.RestorePreviewLayout(token); win.Show(); Application.DoEvents(); return win;
    }
    [DllImport("kernel32")] static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32")] static extern bool TerminateProcess(IntPtr handle,uint result);

    [STAThread]
    static void Main() {
        uint result=0;
        try {
            Application.EnableVisualStyles();
            var large=new Rectangle(-2560,20,2560,1440);
            var small=new Rectangle(0,40,1000,640);
            var imported=new PreviewPlacement("gone",new Rectangle(1600,700,1800,1200),large.Size,true,false);
            Check(small.Contains(imported.Restore(small,new Size(360,240))),"missing display and smaller work area produce fully visible bounds");
            var neg=new PreviewPlacement("left",new Rectangle(120,80,900,650),large.Size,false,false);
            Check(neg.Restore(large,new Size(360,240))==new Rectangle(-2440,100,900,650),"negative-coordinate monitor restores exact relative position");
            Check(PreviewPlacement.Decode("1|broken") == null && PreviewPlacement.Decode("1|!|0|0|0|0|500|500|1000|700")==null,
                "malformed layout falls back safely");
            string saved,fullscreenSaved;
            Rectangle normal;
            using(var w=Open("")) {
                var work=Screen.FromControl(w).WorkingArea;
                var hwnd=w.Surface.Handle;
                int events=0;w.PreviewLayoutChanged+=delegate {events++;};
                w.SetVideoSize(1920,1080); Application.DoEvents();
                Check(Ratio(w,16/9.0) && work.Contains(w.Bounds),"first frame fits landscape surface inside work area");
                var initial=w.Bounds;
                int before=events;
                w.SetVideoSize(1280,720);w.SetVideoSize(1280,720);Application.DoEvents();
                Check(w.Bounds==initial && events==before,"same aspect updates do not move the window or write preferences");
                w.SetVideoSize(1080,1920);Application.DoEvents();
                Check(Ratio(w,9/16.0) && work.Contains(w.Bounds) && w.Width<initial.Width && w.Surface.Handle==hwnd,"rotation fits portrait while retaining the native surface");
                w.SetVideoSize(1920,1080);Application.DoEvents();
                Check(Math.Abs(w.Width-initial.Width)<2 && Math.Abs(w.Height-initial.Height)<2,"repeated rotations retain original viewing area");
                ResizeEvent(w,"OnResizeBegin"); w.Size=new Size(Math.Min(760,work.Width),Math.Min(500,work.Height));
                ResizeEvent(w,"OnResizeEnd");Application.DoEvents();
                Check(Ratio(w,16/9.0),"manual resize lets user choose viewing size and follows ratio on release");
                var beforeFull=w.Bounds;
                w.ToggleFullscreen();var full=w.Bounds; w.SetVideoSize(1080,1920);Application.DoEvents();
                Check(w.IsFullscreen && w.Bounds==full && w.Surface.Handle==hwnd,"device rotation leaves fullscreen display geometry intact");
                var during=PreviewPlacement.Decode(w.CapturePreviewLayout());
                Check(during.RelativeBounds.Size==beforeFull.Size,"fullscreen does not become next saved normal size");
                w.ToggleFullscreen();Application.DoEvents();
                Check(!w.IsFullscreen && Ratio(w,9/16.0),"fullscreen exit applies pending rotation to normal geometry");
                w.WindowState=FormWindowState.Minimized;Application.DoEvents();
                w.SetVideoSize(1920,1080);Application.DoEvents();
                Check(w.WindowState==FormWindowState.Minimized,"rotation does not restore a minimized presentation");
                w.WindowState=FormWindowState.Normal;Application.DoEvents();
                Check(Ratio(w,16/9.0),"restoring a minimized window applies pending landscape frame");
                w.WindowState=FormWindowState.Maximized;Application.DoEvents();
                var max=w.Bounds;w.SetVideoSize(1080,1920);Application.DoEvents();
                Check(w.WindowState==FormWindowState.Maximized && w.Bounds==max,"maximized window stays maximized during rotation");
                w.WindowState=FormWindowState.Normal;Application.DoEvents();
                Check(Ratio(w,9/16.0),"restoring a maximized window applies pending portrait frame");
                ToggleAspect(w);normal=w.Bounds;
                w.SetVideoSize(1920,1080);Application.DoEvents();
                Check(normal==w.Bounds && !PreviewPlacement.Decode(w.CapturePreviewLayout()).Follow,"opt-out retains fixed size after rotation");
                w.Bounds=new Rectangle(work.Left+18,work.Top+26,Math.Min(work.Width-30,630),Math.Min(work.Height-42,468));
                Application.DoEvents();normal=w.Bounds;saved=w.CapturePreviewLayout();
                w.WindowState=FormWindowState.Minimized;Application.DoEvents();
                Check(saved==w.CapturePreviewLayout(),"minimized coordinates do not overwrite remembered placement");
                w.WindowState=FormWindowState.Normal;Application.DoEvents();w.ToggleFullscreen();fullscreenSaved=w.CapturePreviewLayout();
                w.CloseFromController();
            }
            using(var w=Open(fullscreenSaved)) {
                Check(!w.IsFullscreen && w.WindowState==FormWindowState.Normal && w.Bounds==normal &&
                    !PreviewPlacement.Decode(w.CapturePreviewLayout()).Follow,"reopened window restores display, exact normal bounds, opt-out and no fullscreen");
                w.WindowState=FormWindowState.Maximized;Application.DoEvents(); string maximized=w.CapturePreviewLayout();
                var record=PreviewPlacement.Decode(maximized);
                Check(record.Maximized && record.RelativeBounds.Size==normal.Size,"maximized windows retain restore bounds");
                using (var repeat=Open(maximized)) {
                    Check(repeat.WindowState==FormWindowState.Maximized,"maximized presentation preference restored");
                    repeat.WindowState=FormWindowState.Normal;Application.DoEvents();
                    Check(repeat.Bounds==normal,"restoring after relaunch recovers original normal bounds");
                    repeat.CloseFromController();
                }
                w.CloseFromController();
            }
            using(var w=Open(imported.Encode())) {
                Check(Screen.PrimaryScreen.WorkingArea.Contains(w.Bounds),"stale monitor identifier falls back to available primary screen");
                w.CloseFromController();
            }
            Console.WriteLine("Preview layout checks: "+count);
        } catch(Exception ex) {Console.Error.WriteLine(ex);result=1;}
        Console.Out.Flush(); Console.Error.Flush(); TerminateProcess(GetCurrentProcess(),result);
    }
}
