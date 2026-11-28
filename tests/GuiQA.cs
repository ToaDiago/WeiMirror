using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

static class GuiQA
{
    static int checks;
    static Form form;
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static T Field<T>(object target, string field) { return (T)target.GetType().GetField(field,Private).GetValue(target); }
    static object Call(object target, string method, params object[] args) { return target.GetType().GetMethod(method,Private | BindingFlags.Public).Invoke(target,args); }
    static Form Preview() { return Application.OpenForms.Cast<Form>().SingleOrDefault(x=>x.GetType().Name=="PreviewWindow" && !x.IsDisposed); }
    static Panel Surface(Form window) { return (Panel)window.GetType().GetProperty("Surface",Private|BindingFlags.Public).GetValue(window,null); }
    static IEnumerable<Control> Descendants(Control root) {
        foreach (Control child in root.Controls) {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    static bool ShowsPath(Control target,string path) {
        return Descendants(target).Any(c=>c.Visible && c.Text.IndexOf(path,StringComparison.OrdinalIgnoreCase)>=0);
    }
    static void ReviewImage(Form window,string name) {
        string directory=Environment.GetEnvironmentVariable("WEIMIRROR_QA_SCREENSHOTS");
        if (String.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        using (var bitmap=CaptureWindow(window))
            bitmap.Save(Path.Combine(directory,name+".png"),System.Drawing.Imaging.ImageFormat.Png);
    }
    static Bitmap CaptureWindow(Form window) {
        var transition=window.Controls.Find("ConsoleTransition",true).SingleOrDefault();
        if (transition?.Visible==true) Until(()=>!transition.Visible,"page transition finished");
        window.Update();
        var bitmap=new Bitmap(window.Width,window.Height,System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        try {
            using (var graphics=Graphics.FromImage(bitmap)) {
                var dc=graphics.GetHdc();
                try { if (!PrintWindow(window.Handle,dc,0)) throw new InvalidOperationException("Native window capture failed: "+window.Text); }
                finally { graphics.ReleaseHdc(dc); }
            }
            return bitmap;
        } catch {
            bitmap.Dispose();
            throw;
        }
    }
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr window,IntPtr dc,uint flags);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr window,int index);
    static Rectangle PixelsFor(Form window,Control child) {
        var screen=child.RectangleToScreen(child.ClientRectangle);
        screen.Offset(-window.Left,-window.Top);
        return screen;
    }
    static void CheckPaint(Form window,params Control[] controls) {
        using (var bitmap=CaptureWindow(window)) {
            foreach (Control child in controls) {
                Rectangle area=PixelsFor(window,child);
                Check(new Rectangle(Point.Empty,bitmap.Size).Contains(area),"native capture contains "+child.Text);
                int black=0, samples=0;
                for (int y=area.Top;y<area.Bottom;y+=3)
                    for (int x=area.Left;x<area.Right;x+=3) {
                        var color=bitmap.GetPixel(x,y);
                        if (color.R<12 && color.G<12 && color.B<12) black++;
                        samples++;
                    }
                Check(samples>0 && black*20<samples,"native paint has no black backing under "+child.Text);
            }
        }
    }
    static void CheckQualityFace(Form window,ComboBox quality) {
        using (var bitmap=CaptureWindow(window)) {
            var area=PixelsFor(window,quality);
            // Avoid borders and the arrow. Confirm the glyphs of the selected item
            // reach the native window capture, beyond checking its Text property.
            area.Inflate(-6,-5);
            area.Width=Math.Min(area.Width/2,180);
            int ink=0;
            for (int y=area.Top;y<area.Bottom;y++)
                for (int x=area.Left;x<area.Right;x++) {
                    var color=bitmap.GetPixel(x,y);
                    if (color.R<90 && color.G<100 && color.B<120) ink++;
                }
            Check(ink>area.Height && quality.Text.Contains("高清自动"),"selected quality text reaches the native combo face");
        }
    }
    static void Settings(Action<Control> inspect,bool viaShortcut=false) {
        int windowCount=Application.OpenForms.Count;
        var settings=Named(form,"SettingsPage");
        var header=(Button)Named(form,"OpenSettings");
        if (viaShortcut) Key(form,Keys.Control|Keys.Oemcomma);
        else header.PerformClick();
        Check(settings.Visible && settings.FindForm()==form && form.Enabled && Application.OpenForms.Count==windowCount,
            "settings opens in the enabled control panel");
        try {
            SettingsPage(settings,0);
            inspect(settings);
        } finally {
            Key(form,Keys.Escape);
        }
        Check(!settings.Visible && Field<Button>(form,"primary").Visible,"Escape returns to the control panel");
    }
    static void SettingsPage(Control settings,int section) {
        ((Button)Named(settings,"SettingsSection"+section)).PerformClick();
        Application.DoEvents();
    }

    static void VerifyHighDpiRepaint(Control mode,Button primary) {
        const int testDpi=192;
        var size=form.ClientSize;
        int originalDpi=form.DeviceDpi;
        ((Button)Named(form,"AirPlayMode")).PerformClick();
        Idle();
        Call(form,"ScaleConsole",testDpi);
        form.ClientSize=new Size(1176,840);
        form.PerformLayout();
        var quality=(ComboBox)Named(form,"AirPlayQuality");
        CheckPaint(form,Named(form,"OpenSettings"),Named(form,"UsbMode"),Named(form,"AirPlayMode"),primary,quality);
        CheckQualityFace(form,quality);
        ReviewImage(form,"console-wireless-200");
        Settings(settings=>{
            form.ClientSize=new Size(1016,738);
            Application.DoEvents();
            CheckPaint(form,Named(settings,"SettingsSection0"),Named(settings,"SettingsSection1"),
                Field<CheckBox>(form,"audio"),Field<CheckBox>(form,"showToolbar"),Named(form,"OpenSettings"));
            ReviewImage(form,"settings-casting-200");
        },true);
        Call(form,"ScaleConsole",originalDpi);
        form.ClientSize=size;
        SelectMode(mode,0);
        Idle();
    }

    static void VerifyToolsPage(Control settings) {
        int windowCount=Application.OpenForms.Count;
        SettingsPage(settings,3);
        Check(new[] { "OpenLatencyTest","ExportLatencyTest","ExportSource" }
            .All(name=>Named(settings,name).Visible && Named(settings,name).Enabled) &&
            Application.OpenForms.Count==windowCount,"tools and about shows latency and source actions in settings");
    }
    static string StatText() { var win=Preview(); return win==null ? "" : Field<Label>(win,"stats").Text; }
    static bool OverlayShown() { var win=Preview(); return win!=null && Field<Label>(win,"overlay").Visible; }
    static void SelectMode(Control picker,int index) {
        var property=picker.GetType().GetProperty("SelectedIndex");
        try { property.SetValue(picker,index,null); }
        catch(Exception ex) { Console.Error.WriteLine("SelectMode failed: "+ex); throw; }
    }
    static int CurrentMode(Form target) {
        var picker=Field<Control>(target,"mode");
        return (int)picker.GetType().GetProperty("SelectedIndex").GetValue(picker,null);
    }
    static Control Named(Control target,string name) {
        return Descendants(target).Single(c=>c.Name==name);
    }
    static Form WithPrefs(Assembly assembly,object store) {
        return (Form)Activator.CreateInstance(assembly.GetType("MirrorDesk.MainForm"),BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance,
            null,new object[]{null,store},null);
    }
    static void SetDestination(Func<Form,string> chooser) {
        form.GetType().GetField("SnapshotDestination",BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Instance).SetValue(form,chooser);
    }
    static void Key(Form window,Keys key) {
        var message=Message.Create(window.Handle,0x100,(IntPtr)(int)key,IntPtr.Zero);
        if (!window.PreProcessMessage(ref message)) throw new Exception("Unhandled preview key " + key);
        Application.DoEvents();
    }
    static void Check(bool value, string label) {
        if (!value) throw new Exception(label);
        checks++; Console.WriteLine("PASS GUI " + label);
    }
    static void Until(Func<bool> ready, string what) {
        var clock = Stopwatch.StartNew();
        do {
            Application.DoEvents();
            if (ready()) return;
            Thread.Sleep(15);
        } while (clock.ElapsedMilliseconds < 10000);
        // A failed scenario may leave a native session attached. Print the
        // evidence before WinForms shutdown enters the CLR export fixture.
        Console.Error.WriteLine("FAIL GUI timeout: " + what + " | footer=" + Field<Label>(form,"footer").Text);
        Console.Out.Flush(); Console.Error.Flush();
        TerminateProcess(GetCurrentProcess(), 1);
        throw new Exception("Timeout " + what);
    }
    static void Idle() { Until(() => !Field<bool>(form,"busy") && !Field<bool>(form,"polling"), "waiting for idle operation"); }
    static void Click(Button b) {
        if(!b.Enabled) {
            var picked=Field<ComboBox>(form,"devices").SelectedItem;
            Console.Error.WriteLine("Button disabled, busy={0} closing={1} selected={2} footer={3} enabledParent={4} enabledForm={5}",Field<bool>(form,"busy"),Field<bool>(form,"closing"),picked==null ? "(null)":picked.GetType().FullName,Field<Label>(form,"footer").Text,b.Parent.Enabled,form.Enabled);
        }
        Check(b.Enabled,"button available ["+b.Text+"]"); b.PerformClick(); Idle();
    }
    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] static extern bool TerminateProcess(IntPtr process, uint code);
    [STAThread] static int Main(string[] args) {
        int result = 0;
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        EventHandler launch = null;
        launch = delegate {
            Application.Idle -= launch;
            try { Run(args); }
            catch(Exception e) { Console.Error.WriteLine(e); result = 1; }
            Application.ExitThread();
        };
        Application.Idle += launch;
        Application.Run();
        Console.Out.Flush(); Console.Error.Flush();
        TerminateProcess(GetCurrentProcess(),(uint)result);
        return result;
    }
    static void Run(string[] args) {
        string runtime = Path.GetFullPath(args[0]);
        var assembly = Assembly.LoadFrom(Path.GetFullPath(args[1]));
        var packType = assembly.GetType("MirrorDesk.RuntimePack");
        // Only run this scenario with no configured product runtime. Avoid
        // touching an existing user's settings or starting their receiver.
        if (packType.GetMethod("Find").Invoke(null,null) != null)
            throw new Exception("Existing configured engine; run the GUI fixture in a fresh profile.");
        string prefsPath=Path.Combine(Path.GetDirectoryName(runtime),"isolated-profile","preferences.ini");
        object prefs=Activator.CreateInstance(assembly.GetType("MirrorDesk.PreferencesStore"),BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance,
            null,new object[]{prefsPath},null);
        using (form = WithPrefs(assembly,prefs)) {
            form.ShowInTaskbar = false;
            form.Show(); Until(() => !Field<bool>(form,"busy") && Field<Label>(form,"footer").Text.Contains("组件")
                && Field<Button>(form,"primary").Text.Contains("准备"),"first run");
            Button primary = Field<Button>(form,"primary");
            var mode = Field<Control>(form,"mode");
            string localPacks = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"WeiMirror","packs");
            Check(Preview()==null && !ShowsPath(form,localPacks) && !Named(form,"SettingsPage").Visible &&
                !Field<ComboBox>(form,"quality").Visible,
                "USB mode hides the wireless quality control and keeps component paths in settings");
            Check(primary.Font.SizeInPoints>=10 && primary.Font.SizeInPoints<12 && mode.Font.SizeInPoints<12,
                "console uses compact default type at 100 percent");
            SelectMode(mode,1); Idle();
            var quality=Field<ComboBox>(form,"quality");
            Check(quality.Visible && quality.Items.Count==6 && quality.Items.Cast<object>().All(item=>!item.ToString().Contains("120")),
                "the AirPlay home page exposes all six profiles at up to 60 fps");
            quality.Focus();
            quality.DroppedDown=true;
            Check(quality.DroppedDown,"the home page quality picker opens its native list");
            quality.DroppedDown=false;
            SelectMode(mode,0); Idle();
            Settings(dialog=>{
                Check(form.Opacity==1 && !form.AllowTransparency && (GetWindowLong(form.Handle,-20)&0x00080000)==0,
                    "settings remains a normal opaque window while open");
                Check(Descendants(form).Concat(Descendants(dialog)).Where(c=>c.Visible).All(c=>c.BackColor.A==255),
                    "visible settings controls have explicit opaque backgrounds");
                Check(!Descendants(dialog).Contains(quality),"settings uses the homepage quality picker without a duplicate");
                SettingsPage(dialog,2);
                Check(ShowsPath(dialog,localPacks),"components settings shows the download directory before setup");
                SettingsPage(dialog,1);
                var theme=(ComboBox)Named(dialog,"AppearanceTheme");
                Check(theme.Items.Count==9 && theme.FindStringExact("薄荷")>=0 && theme.FindStringExact("石墨")>=0,
                    "appearance offers eight presets and a custom theme");
                theme.SelectedIndex=theme.FindStringExact("自定义");
                Check(Named(dialog,"ThemeAccent").Visible && Named(dialog,"ThemeLightBackground").Visible &&
                    Named(dialog,"ThemeDarkBackground").Visible,"the custom theme shows three color pickers");
                theme.SelectedIndex=0;
                Check(!Named(dialog,"ThemeAccent").Visible,"preset themes collapse the custom colors");
                var scale=(ComboBox)Named(dialog,"TextPercent");
                var size=(ComboBox)Named(dialog,"InterfaceSize");
                var fonts=(ComboBox)Named(dialog,"InterfaceFont");
                Check(scale.Text=="100%" && size.Text=="中","text and interface size start at 100 percent");
                ReviewImage(form,"settings");
                float fontBefore=primary.Font.Size;
                int widthBefore=form.ClientSize.Width;
                scale.SelectedIndex=2;
                size.SelectedIndex=2;
                fonts.SelectedIndex=fonts.FindStringExact("Segoe UI");
                Check(primary.Font.Size>fontBefore && form.ClientSize.Width>widthBefore && primary.Font.Name=="Segoe UI",
                    "font, type size and interface size update the console");
                Field<CheckBox>(form,"darkMode").Checked=true;
                ReviewImage(form,"settings-dark");
                Field<CheckBox>(form,"darkMode").Checked=false;
                size.SelectedIndex=1;
                scale.SelectedIndex=1;
                fonts.SelectedIndex=0;
            });
            VerifyHighDpiRepaint(mode,primary);
            int setupWindowCount=Application.OpenForms.Count;
            Click(primary);
            var setup=Named(form,"SetupPage");
            Check(setup.Visible && Application.OpenForms.Count==setupWindowCount &&
                new[] { "DownloadComponents","ImportComponents","ChooseComponentDirectory" }
                .All(name=>Named(setup,name).Visible && Named(setup,name).Enabled),
                "first-run setup shows online/ZIP/folder choices in the control panel");
            Check(ShowsPath(setup,localPacks),"setup shows the real output directory before download");
            ((Button)Named(form,"OpenSettings")).PerformClick();
            Check(!setup.Visible && primary.Visible,"component setup returns to the control panel");
            object core = Activator.CreateInstance(assembly.GetType("MirrorDesk.MirrorCore"), runtime);
            // ReceiverQA exercises the host self-test as a standalone subprocess.
            // Reuse that successful fixture preflight to isolate UI lifecycle
            // checks from repeated CLR host creation in this instrumented VM.
            core.GetType().GetField("wirelessRuntimeProbed",Private).SetValue(core,true);
            form.GetType().GetField("core",Private).SetValue(form,core);
            form.GetType().GetField("engineRoot",Private).SetValue(form,runtime);
            Call(form,"UpdateControls");
            Settings(dialog=>{
                SettingsPage(dialog,2);
                Check(ShowsPath(dialog,runtime),"components settings shows the active runtime's actual path");
                Check(Field<Button>(form,"openPath").Enabled && Field<Button>(form,"driver").Enabled,
                    "active components expose their directory and USB driver actions");
                VerifyToolsPage(dialog);
            });
            VerifyUsbDiagnostics(Field<Button>(form,"refresh"));
            Check(Field<ComboBox>(form,"devices").Text=="书桌 iPad","refresh populates USB list");
            ReviewImage(form,"console");
            var themeSwitch=Field<CheckBox>(form,"darkMode");
            themeSwitch.Checked=true;
            ReviewImage(form,"console-dark");
            themeSwitch.Checked=false;
            Click(primary);
            Until(()=>StatText().Contains("59.5"),"stream stats");
            Form first=Preview();
            var surface=Surface(first);
            Check(first.TopLevel && first.Visible && first.Owner==null && first.ShowInTaskbar && form.Visible && form.Enabled,"USB playback opens a separate top-level taskbar window and leaves control panel usable");
            Check(surface.FindForm()==first && !form.Contains(surface) && FixtureSupport.Attached==surface.Handle,"native video HWND belongs to the detached window");
            Check(StatText().Contains("1920 × 1080") && Field<Label>(form,"footer").Text.Contains("书桌 iPad") && !OverlayShown(),"detached preview tracks stream status, dims and frames");
            Settings(dialog=>{
                SettingsPage(dialog,0);
                var showToolbar=Field<CheckBox>(form,"showToolbar");
                Call(showToolbar,"OnClick",EventArgs.Empty);
                Check(showToolbar.Checked && Named(first,"PreviewToolbar").Visible,
                    "settings switch displays the toolbar on the current preview");
                Call(showToolbar,"OnClick",EventArgs.Empty);
                Check(!showToolbar.Checked,"settings switch restores automatic toolbar hiding");
            });
            VerifyRotation(first);
            var pin=(ButtonBase)Named(first,"PinPreview");
            Call(pin,"OnClick",EventArgs.Empty);
            Check(first.TopMost && !form.TopMost,"pin applies only to preview window");
            var audio = Field<CheckBox>(form,"audio");
            Settings(dialog=>{ SettingsPage(dialog,0); Call(audio,"OnClick",EventArgs.Empty); Idle(); });
            Check(!FixtureSupport.Audio,"computer audio checkbox applied during session");
            var controlBounds=form.Bounds; var border=form.FormBorderStyle;
            Key(first,Keys.F11);
            Check(first.FormBorderStyle==FormBorderStyle.None && form.Bounds==controlBounds && form.FormBorderStyle==border && FixtureSupport.Attached==surface.Handle,"F11 expands only the detached window and preserves capture");
            VerifyScreenshot(first,runtime);
            Key(first,Keys.Escape);
            Check(first.FormBorderStyle!=FormBorderStyle.None && FixtureSupport.Attached==surface.Handle,"Escape restores detached preview");
            Check(first.TopMost,"pinned mode survives fullscreen round trip");
            first.Width+=65; Application.DoEvents();
            Check(form.Bounds==controlBounds && FixtureSupport.Attached==surface.Handle,"resizing the video window leaves the main window and renderer intact");
            form.WindowState=FormWindowState.Minimized; Application.DoEvents();
            Check(first.Visible && first.WindowState!=FormWindowState.Minimized,"panel minimization leaves the independently owned video window visible");
            form.WindowState=FormWindowState.Normal; Application.DoEvents();
            first.WindowState=FormWindowState.Minimized;
            Click(Field<Button>(form,"showPreview"));
            Check(Preview()==first && first.WindowState!=FormWindowState.Minimized && Surface(first).Handle==FixtureSupport.Attached,"show preview button restores the same minimized native target");
            first.Close();
            Until(()=>first.IsDisposed && Preview()==null && !FixtureSupport.Allocated && !Field<bool>(form,"busy"),"closing detached USB window cleans capture");
            Check(form.Visible && !form.IsDisposed && FixtureSupport.LastDestroyHadLivePreview,"closing detached USB window releases native resources before destroying its HWND");
            Click(primary); Until(()=>StatText().Contains("59.5"),"USB reopens in new window");
            Check(Preview()!=first && Preview().TopMost && !FixtureSupport.Audio,"USB can reopen with pin and selected audio setting");
            Click(primary); Check(!FixtureSupport.Allocated && Preview()==null,"main stop closes preview and releases USB source");
            VerifyCloseDuringAttach(primary);
            VerifyCloseDuringSnapshot(primary);
            VerifyUsbRecovery(primary,mode);
            SelectMode(mode,1); Idle(); FixtureSupport.ShowAirDevice = false;
            Check(quality.Items.Count>=4 && quality.Items[3].ToString().Contains("720") && quality.Items[3].ToString().Contains("60"),
                "720p60 is selectable without changing the original three quality options");
            Settings(dialog=>{
                SettingsPage(dialog,0);
                Field<TextBox>(form,"name").Text = "测验接收端";
            });
            quality.SelectedIndex=3;
            Click(primary);
            Until(()=>Field<Label>(form,"footer").Text.Contains("等待 iPad"),"wireless waiting");
            Check(Preview()==null && FixtureSupport.ReceiverName=="测验接收端" && FixtureSupport.ReceiverWidth==1280 &&
                FixtureSupport.ReceiverFps==30 && String.Equals(FixtureSupport.ReceiverHost,assembly.Location,StringComparison.OrdinalIgnoreCase) &&
                String.Equals(FixtureSupport.ReceiverLibraryDirectory,Path.Combine(runtime,"Wireless"),StringComparison.OrdinalIgnoreCase) &&
                !FixtureSupport.Allocated,"selecting 720p60 uses the custom host with the stock carrier and waits without an empty projection");
            Check(quality.Visible && !quality.Enabled && quality.Text.Contains("720"),
                "the home page preserves the active quality while AirPlay is running");
            Settings(dialog=>{
                SettingsPage(dialog,0);
                Check(!Field<TextBox>(form,"name").Enabled,"broadcast locks the active receiver name");
            });
            FixtureSupport.ShowAirDevice = true;
            Until(()=>Field<Label>(form,"footer").Text.Contains("客厅 iPad") && StatText().Contains("59.5"),"wireless attach and display");
            Check(FixtureSupport.Allocated && !OverlayShown() && Surface(Preview()).Handle==FixtureSupport.Attached,"AirPlay auto-opens and renders in detached preview");
            VerifyWirelessRecovery(primary);
            Preview().Close(); Until(()=>!FixtureSupport.AirStarted && !FixtureSupport.Allocated && Preview()==null,"window close stops AirPlay receiver");
            Check(!form.IsDisposed,"wireless window close stops broadcasting without closing control panel");
            SelectMode(mode,0); Idle(); Click(primary);
            Check(!FixtureSupport.Audio && !audio.Checked, "USB reconnection preserves selected computer mute");
            SelectMode(mode,1); Idle();
            Check(!FixtureSupport.Allocated && !FixtureSupport.AirStarted,"mode change during USB playback releases the old source");
            Click(primary);
            Until(()=>FixtureSupport.Allocated,"second wireless attachment");
            Settings(dialog=>{
                SettingsPage(dialog,1);
                ((ComboBox)Named(dialog,"AppearanceTheme")).SelectedIndex=2;
                Call(Field<CheckBox>(form,"darkMode"),"OnClick",EventArgs.Empty);
                var fonts=(ComboBox)Named(dialog,"InterfaceFont");
                fonts.SelectedIndex=fonts.FindStringExact("Segoe UI");
                ((ComboBox)Named(dialog,"TextPercent")).SelectedIndex=2;
                ((ComboBox)Named(dialog,"InterfaceSize")).SelectedIndex=0;
                SettingsPage(dialog,0);
                Call(Field<CheckBox>(form,"showToolbar"),"OnClick",EventArgs.Empty);
            });
            form.Close(); Until(()=>form.IsDisposed,"asynchronous window close");
            Check(!FixtureSupport.Allocated && Preview()==null && FixtureSupport.Calls.Last() == "shutdown","closing main panel during active reception releases both windows and capture");
        }
        using (form=WithPrefs(assembly,prefs)) {
            Check(File.Exists(prefsPath) && CurrentMode(form)==1 && Field<ComboBox>(form,"quality").SelectedIndex==3 &&
                Field<TextBox>(form,"name").Text=="测验接收端" && !Field<CheckBox>(form,"audio").Checked,"fresh panel restores explicit local preferences across restarts");
            Check(Field<ComboBox>(form,"appearance").SelectedIndex==2 && Field<CheckBox>(form,"darkMode").Checked &&
                Field<ComboBox>(form,"interfaceFont").Text=="Segoe UI" && Field<ComboBox>(form,"textPercent").Text=="110%" &&
                Field<ComboBox>(form,"interfaceSize").Text=="小" && Field<CheckBox>(form,"showToolbar").Checked,
                "theme, dark mode, font, font size, window size and toolbar survive a restart");
            var view=Call(form,"ViewToken");
            using (Form worker=WithPrefs(assembly,prefs)) {
                SelectMode(Field<Control>(worker,"mode"),0);
                Call(worker,"RestoreView",view);
                Check(CurrentMode(worker)==1 && !Field<CheckBox>(worker,"audio").Checked && Field<TextBox>(worker,"name").Text=="测验接收端",
                    "worker handoff restores sender name, connection and audio selection");
            }
        }
        Console.WriteLine("GUI checks: " + checks);
    }
    static void VerifyUsbDiagnostics(Button refresh) {
        var footer=Field<Label>(form,"footer");
        FixtureSupport.ShowUsbDevice=false; FixtureSupport.PhysicalAppleDevices=0;
        Click(refresh);
        Check(footer.Text.Contains("Windows 尚未识别"),"no physical Apple USB device is distinguished from missing trust");
        FixtureSupport.PhysicalAppleDevices=1;
        Click(refresh);
        Check(footer.Text.Contains("Windows 已识别"),"visible USB hardware with no receiver entry has a distinct diagnosis");
        FixtureSupport.ShowUsbDevice=true; FixtureSupport.UsbNeedsTrust=true;
        Click(refresh);
        Check(footer.Text.Contains("信任此电脑"),"explicit native trust prompt is shown for the listed device");
        FixtureSupport.UsbNeedsTrust=false;
        Click(refresh);
        Check(!footer.Text.Contains("信任此电脑") && Field<ComboBox>(form,"devices").Items.Count==1,
            "verified-device refresh replaces the prior trust diagnosis for the same ID");
    }
    static void VerifyRotation(Form win) {
        Panel video=Surface(win);
        IntPtr target=video.Handle;
        FixtureSupport.VideoWidth=1080; FixtureSupport.VideoHeight=1920;
        Until(()=>StatText().Contains("1080 × 1920") && video.Height>video.Width,"portrait geometry after rotated native frame");
        Check(video.Handle==target && Math.Abs((double)video.Width/video.Height-9d/16)<0.09,
            "real polling fits the portrait video surface without recreating its native HWND");
        Key(win,Keys.Control|Keys.R);
        Rectangle fixedBounds=win.Bounds;
        FixtureSupport.VideoWidth=1920; FixtureSupport.VideoHeight=1080;
        Until(()=>StatText().Contains("1920 × 1080"),"landscape frame arrives with fitting disabled");
        Check(win.Bounds==fixedBounds && FixtureSupport.Attached==target,"manual-size mode leaves window bounds alone on rotation");
        Key(win,Keys.Control|Keys.R);
        Until(()=>video.Width>video.Height,"re-enabling aspect follows current landscape frame");
        Key(win,Keys.F11);
        Rectangle fullscreen=win.Bounds;
        FixtureSupport.VideoWidth=1080; FixtureSupport.VideoHeight=1920;
        Until(()=>StatText().Contains("1080 × 1920"),"rotation while fullscreen");
        Check(fullscreen==win.Bounds && win.FormBorderStyle==FormBorderStyle.None,"rotation in fullscreen leaves monitor-sized window intact");
        Key(win,Keys.Escape);
        Until(()=>video.Height>video.Width,"return from fullscreen fits the latest rotated source");
        Check(win.Bounds!=fullscreen && FixtureSupport.Attached==video.Handle,"restoring portrait geometry retains the active renderer");
        FixtureSupport.VideoWidth=1920; FixtureSupport.VideoHeight=1080;
        Until(()=>StatText().Contains("1920 × 1080") && video.Width>video.Height,"reset landscape");
    }
    static void UsbDetached(Form preview,bool terminal) {
        FixtureSupport.UsbLossIsTerminal=terminal;
        FixtureSupport.ShowUsbDevice=false;
        Until(()=>Preview()==preview && !FixtureSupport.Allocated && OverlayShown() &&
            Field<Label>(preview,"overlay").Text.Contains("USB"),"USB unplug enters reconnect wait in original preview");
    }
    static void VerifyUsbRecovery(Button primary,Control mode) {
        Click(primary);
        Until(()=>StatText().Contains("59.5"),"start USB recovery scenario");
        Form window=Preview();
        IntPtr target=Surface(window).Handle;
        Rectangle bounds=window.Bounds;
        FixtureSupport.WaitingForFrames=true;
        Until(()=>OverlayShown() && !Named(window,"SaveSnapshot").Enabled,"temporary frame gap with USB device connected");
        FixtureSupport.WaitingForFrames=false;
        Until(()=>!OverlayShown() && StatText().Contains("59.5"),"USB frames resume without unplug");
        int attempts=FixtureSupport.UsbAttempts;
        UsbDetached(window,false);
        Check(window.TopMost && window.Bounds==bounds && Surface(window).Handle==target && !Named(window,"SaveSnapshot").Enabled,
            "unplug with nonterminal status preserves window, pin, geometry and target HWND and disables stale screenshots");
        FixtureSupport.ShowForeignUsb=true;
        int enumerations=FixtureSupport.UsbEnumerations;
        Until(()=>FixtureSupport.UsbEnumerations>=enumerations+2,"at least two reconnect polls with only wrong USB device");
        Check(FixtureSupport.ForeignUsbAttempts==0 && FixtureSupport.UsbAttempts==attempts && !FixtureSupport.Allocated,
            "different USB device with identical friendly name is never attached");
        FixtureSupport.RefuseUsbSession=true; FixtureSupport.ShowUsbDevice=true;
        Until(()=>FixtureSupport.UsbAttempts>attempts,"original USB device refuses first reconnect");
        Check(Preview()==window && !window.IsDisposed && !FixtureSupport.Allocated,"failed USB reconnect preserves existing preview for retry");
        FixtureSupport.RefuseUsbSession=false;
        Until(()=>FixtureSupport.ActiveId==FixtureSupport.UsbId && !OverlayShown() && StatText().Contains("59.5"),"original device resumes after retry");
        Check(Preview()==window && FixtureSupport.Attached==target && window.Bounds==bounds && window.TopMost &&
            !FixtureSupport.Audio && FixtureSupport.ForeignUsbAttempts==0,"only original USB ID resumes, restoring video and mute in place");
        FixtureSupport.ShowForeignUsb=false;
        UsbDetached(window,true);
        Check(primary.Enabled && mode.Enabled,"stop and transport controls remain available during reconnect wait");
        Click(primary);
        FixtureSupport.ShowUsbDevice=true;
        Click(Field<Button>(form,"refresh"));
        Check(window.IsDisposed && !FixtureSupport.Allocated && Preview()==null,"manual stop during unplug discards reconnect intent");
        VerifyUsbPendingCancellation(primary,mode,"close");
        VerifyUsbPendingCancellation(primary,mode,"stop");
        VerifyUsbPendingCancellation(primary,mode,"mode");
    }
    static void VerifyUsbPendingCancellation(Button primary,Control mode,string action) {
        Click(primary);
        Until(()=>StatText().Contains("59.5"),"start USB before pending reconnect "+action);
        Form window=Preview();
        UsbDetached(window,true);
        FixtureSupport.EnteredAttach.Reset(); FixtureSupport.ContinueAttach.Reset();
        try {
            FixtureSupport.ShowUsbDevice=true;
            Until(()=>FixtureSupport.EnteredAttach.IsSet,"original device is mid-reattach before "+action);
            Check(primary.Enabled && mode.Enabled,"controls remain available during native reconnect before "+action);
            if (action=="close") window.Close();
            else if (action=="stop") primary.PerformClick();
            else SelectMode(mode,1);
            Application.DoEvents();
            Check(!window.IsDisposed && Surface(window).IsHandleCreated,
                action+" during reconnect keeps the native target alive until the pending attach returns");
        } finally {FixtureSupport.ContinueAttach.Set();}
        Until(()=>window.IsDisposed && Preview()==null && !FixtureSupport.Allocated && !Field<bool>(form,"busy"),
            "pending reconnect honors "+action+" and releases USB session");
        if (action=="mode") { Check(CurrentMode(form)==1 && !FixtureSupport.AirStarted,"transport change cancels USB reconnect without opening AirPlay"); SelectMode(mode,0); Idle(); }
        Click(Field<Button>(form,"refresh"));
        Check(Preview()==null && !FixtureSupport.Allocated,"stale reconnect cannot resurrect the session after "+action);
    }
    static void VerifyScreenshot(Form window,string runtime) {
        string file=Path.Combine(Path.GetDirectoryName(runtime),"gui-capture.png");
        int chooseCount=0;
        SetDestination(owner=>{if(owner!=window) throw new Exception("wrong snapshot window owner"); chooseCount++; return file;});
        Key(window,Keys.Control|Keys.S);
        Until(()=>File.Exists(file) && chooseCount==1,"fullscreen Ctrl+S writes current source frame");
        using (var image=new Bitmap(file)) {
            Check(image.Width==3 && image.Height==2 && image.GetPixel(2,1).ToArgb()==Color.FromArgb(255,83,61,42).ToArgb(),
                "Ctrl+S saves verified native PNG pixels while preview is full screen and pinned");
        }
        SetDestination(owner=>{chooseCount++; return null;});
        int copies=FixtureSupport.FrameCopies;
        Key(window,Keys.Control|Keys.S);
        Until(()=>chooseCount==2,"cancel snapshot picker");
        Check(FixtureSupport.FrameCopies>copies && FixtureSupport.Allocated && window.Visible,"cancelling Save keeps native preview streaming");
        FixtureSupport.FrameProtected=true;
        Until(()=>!Named(window,"SaveSnapshot").Enabled,"protected frame disables save");
        copies=FixtureSupport.FrameCopies; Key(window,Keys.Control|Keys.S);
        Check(chooseCount==2 && FixtureSupport.FrameCopies==copies,"keyboard does not capture or open dialog for a protected frame");
        FixtureSupport.FrameProtected=false;
        Until(()=>Named(window,"SaveSnapshot").Enabled,"permission clears");
        Check(FixtureSupport.Attached==Surface(window).Handle,"recovering screenshot eligibility preserves native video target");
        byte[] original=File.ReadAllBytes(file);
        using (File.Open(file,FileMode.Open,FileAccess.Read,FileShare.None)) {
            SetDestination(owner=>file);
            Key(window,Keys.Control|Keys.S);
            Until(()=>StatText().Contains("截图失败"),"locked target error shown in preview");
        }
        Check(File.ReadAllBytes(file).SequenceEqual(original) && FixtureSupport.Allocated,
            "screenshot error leaves previous file and active USB session intact");
        SetDestination(owner=>file);
        Key(window,Keys.Control|Keys.S);
        Until(()=>StatText().Contains("截图已保存"),"retry after releasing held destination commits");
        Check(window.Visible && FixtureSupport.Attached==Surface(window).Handle,"failed save can be retried without opening a new session");
    }
    static void VerifyCloseDuringSnapshot(Button primary) {
        int dialogs=0;
        SetDestination(owner=>{dialogs++;return null;});
        Click(primary);
        Until(()=>Preview()!=null && Named(Preview(),"SaveSnapshot").Enabled,"visible frame for close-while-capturing screenshot");
        FixtureSupport.EnteredCopy.Reset(); FixtureSupport.ContinueCopy.Reset();
        Form win=Preview();
        try {
            Key(win,Keys.Control|Keys.S);
            Until(()=>FixtureSupport.EnteredCopy.IsSet,"in-flight capture");
            win.Close();
            Until(()=>Field<bool>(form,"previewStopQueued"),"close queued while copying frame");
            Check(!win.IsDisposed && FixtureSupport.Allocated,"preview close waits for in-flight frame copy");
        } finally { FixtureSupport.ContinueCopy.Set(); }
        Until(()=>win.IsDisposed && !Field<bool>(form,"busy") && !FixtureSupport.Allocated,"close drains screenshot job");
        Check(dialogs==0 && Preview()==null && primary.Enabled,"closing an in-flight snapshot skips obsolete save dialog and permits reconnecting");
    }
    static void VerifyCloseDuringAttach(Button primary) {
        FixtureSupport.EnteredAttach.Reset();
        FixtureSupport.ContinueAttach.Reset();
        try {
            primary.PerformClick();
            Until(()=>Preview()!=null && FixtureSupport.EnteredAttach.IsSet,"pending native attach");
            Form pending=Preview();
            IntPtr handle=Surface(pending).Handle;
            pending.Close(); Application.DoEvents();
            Check(!pending.IsDisposed && Surface(pending).Handle==handle,"closing while capture is starting retains the target HWND until native attach finishes");
        } finally { FixtureSupport.ContinueAttach.Set(); }
        Until(()=>Preview()==null && !FixtureSupport.Allocated && !Field<bool>(form,"busy"),"closing during attach returns to panel");
        Check(!form.IsDisposed && primary.Enabled && FixtureSupport.LastDestroyHadLivePreview,"connection can be retried after closing during native attach");
    }
    static void VerifyWirelessRecovery(Button primary) {
        Func<string,bool> active = id => FixtureSupport.ActiveId == id && FixtureSupport.Allocated
            && StatText().Contains("59.5") && !OverlayShown();
        FixtureSupport.ShowAirDevice = false;
        Until(() => FixtureSupport.AirStarted && !FixtureSupport.Allocated && Preview()==null, "peer departure releases old source and preview, returns to listening");
        Check(FixtureSupport.AirStarted && !FixtureSupport.Audio, "AirPlay peer departure clears stale frame and stats while broadcast remains live");
        FixtureSupport.ShowAirDevice = true;
        Until(() => active(FixtureSupport.AirId), "same peer returns");
        Check(!FixtureSupport.Audio, "same AirPlay peer can reconnect with selected audio and no extra click");

        FixtureSupport.WaitingForFrames = true;
        Until(() => StatText().Length == 0 && OverlayShown(), "streaming peer temporarily has no video frames");
        Check(FixtureSupport.ActiveId == FixtureSupport.AirId && FixtureSupport.Allocated, "temporary video loss clears stale FPS and frame without ending connected peer");
        FixtureSupport.WaitingForFrames = false;
        Until(() => active(FixtureSupport.AirId), "frames resume");
        Check(FixtureSupport.Allocated, "recovered frames restore preview");

        FixtureSupport.ShowSecondAirDevice = true;
        FixtureSupport.ShowAirDevice = false;
        Until(() => active(FixtureSupport.SecondAirId), "second peer takes over departed peer");
        Check(Field<Label>(form,"footer").Text.Contains("第二台 iPad"), "new source identity selected when old peer leaves while other peer remains");

        FixtureSupport.AirStarted = false;
        Until(() => !Field<bool>(form,"playing") && !Field<bool>(form,"listening") && !FixtureSupport.Allocated, "host crash during active stream");
        Check(primary.Enabled && Preview()==null, "host crash closes preview and allows receiver restart");

        FixtureSupport.ShowAirDevice = true;
        FixtureSupport.RefuseFirstAirDevice = true;
        Click(primary);
        Until(() => active(FixtureSupport.SecondAirId), "other peer not starved by first peer that fails to attach");
        Check(FixtureSupport.FailedAirStarts > 0 && FixtureSupport.AirStarted && !FixtureSupport.Audio, "failed AirPlay peer permits next peer without dropping listener or mute");
        FixtureSupport.RefuseFirstAirDevice = false;
        FixtureSupport.ShowSecondAirDevice = false;
    }
}
