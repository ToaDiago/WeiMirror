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
    static Control Named(Form target,string name) {
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
            form.Show(); Until(() => !Field<bool>(form,"busy") && Field<Label>(form,"footer").Text.Contains("首次"),"first run");
            Button primary = Field<Button>(form,"primary");
            var mode = Field<Control>(form,"mode");
            string localPacks = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"WeiMirror","packs");
            Check(Preview()==null && ShowsPath(form,localPacks),"main panel shows the component download directory before first setup");
            Check(primary.Font.SizeInPoints>=12 && mode.Font.SizeInPoints>=12 && Field<CheckBox>(form,"audio").Font.SizeInPoints>=12,"connection controls use readable fonts of at least 12 pt");
            bool setupOpened = false, setupPath = false, setupFonts = false;
            using (var timer = new System.Windows.Forms.Timer {Interval=80}) {
                timer.Tick += delegate {
                    Form dlg = Application.OpenForms.Cast<Form>().FirstOrDefault(f=>f.GetType().Name=="SetupDialog");
                    if (dlg != null) {
                        setupOpened = Field<Button>(dlg,"download").Enabled && Field<Button>(dlg,"zip").Enabled && Field<Button>(dlg,"folder").Enabled;
                        setupPath=ShowsPath(dlg,localPacks);
                        setupFonts=Field<Button>(dlg,"download").Font.SizeInPoints>=12;
                        timer.Stop(); Field<Button>(dlg,"cancel").PerformClick();
                    }
                };
                timer.Start(); Click(primary);
            }
            Check(setupOpened && !form.IsDisposed,"first-run setup exposes online/ZIP/folder choices and closes");
            Check(setupPath && setupFonts,"setup shows real output directory before download and has a large primary button");
            object core = Activator.CreateInstance(assembly.GetType("MirrorDesk.MirrorCore"), runtime);
            // ReceiverQA exercises the host self-test as a standalone subprocess.
            // Reuse that successful fixture preflight to isolate UI lifecycle
            // checks from repeated CLR host creation in this instrumented VM.
            core.GetType().GetField("wirelessRuntimeProbed",Private).SetValue(core,true);
            form.GetType().GetField("core",Private).SetValue(form,core);
            form.GetType().GetField("engineRoot",Private).SetValue(form,runtime);
            Call(form,"UpdateControls");
            Check(ShowsPath(form,runtime),"main panel shows the active runtime's actual absolute path");
            VerifyUsbDiagnostics(Field<Button>(form,"refresh"));
            Check(Field<ComboBox>(form,"devices").Text=="书桌 iPad","refresh populates USB list");
            Click(primary);
            Until(()=>StatText().Contains("59.5"),"stream stats");
            Form first=Preview();
            var surface=Surface(first);
            Check(first.TopLevel && first.Visible && first.Owner==null && first.ShowInTaskbar && form.Visible && form.Enabled,"USB playback opens a separate top-level taskbar window and leaves control panel usable");
            Check(surface.FindForm()==first && !form.Contains(surface) && FixtureSupport.Attached==surface.Handle,"native video HWND belongs to the detached window");
            Check(StatText().Contains("1920 × 1080") && Field<Label>(form,"footer").Text.Contains("书桌 iPad") && !OverlayShown(),"detached preview tracks stream status, dims and frames");
            VerifyRotation(first);
            var pin=(ButtonBase)Named(first,"PinPreview");
            Call(pin,"OnClick",EventArgs.Empty);
            Check(first.TopMost && !form.TopMost,"pin applies only to preview window");
            var audio = Field<CheckBox>(form,"audio"); audio.Checked = false; Idle();
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
            Field<TextBox>(form,"name").Text = "测验接收端";
            var quality=Field<ComboBox>(form,"quality");
            Check(quality.Items.Count>=4 && quality.Items[3].ToString().Contains("720") && quality.Items[3].ToString().Contains("60"),
                "720p60 is selectable without changing the original three quality options");
            quality.SelectedIndex = 3;
            Click(primary);
            Until(()=>Field<Label>(form,"footer").Text.Contains("等待 iPad"),"wireless waiting");
            Check(Preview()==null && FixtureSupport.ReceiverName=="测验接收端" && FixtureSupport.ReceiverWidth==1280 &&
                FixtureSupport.ReceiverFps==30 && String.Equals(FixtureSupport.ReceiverHost,assembly.Location,StringComparison.OrdinalIgnoreCase) &&
                String.Equals(FixtureSupport.ReceiverLibraryDirectory,Path.Combine(runtime,"Wireless"),StringComparison.OrdinalIgnoreCase) &&
                !FixtureSupport.Allocated,"selecting 720p60 uses the custom host with the stock carrier and waits without an empty projection");
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
            form.Close(); Until(()=>form.IsDisposed,"asynchronous window close");
            Check(!FixtureSupport.Allocated && Preview()==null && FixtureSupport.Calls.Last() == "shutdown","closing main panel during active reception releases both windows and capture");
        }
        using (form=WithPrefs(assembly,prefs)) {
            Check(File.Exists(prefsPath) && CurrentMode(form)==1 && Field<ComboBox>(form,"quality").SelectedIndex==3 &&
                Field<TextBox>(form,"name").Text=="测验接收端" && !Field<CheckBox>(form,"audio").Checked,"fresh panel restores explicit local preferences across restarts");
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
        var follow=(Button)Named(win,"FollowAspect");
        FixtureSupport.VideoWidth=1080; FixtureSupport.VideoHeight=1920;
        Until(()=>StatText().Contains("1080 × 1920") && video.Height>video.Width,"portrait geometry after rotated native frame");
        Check(video.Handle==target && Math.Abs((double)video.Width/video.Height-9d/16)<0.09,
            "real polling fits the portrait video surface without recreating its native HWND");
        follow.PerformClick();
        Rectangle fixedBounds=win.Bounds;
        FixtureSupport.VideoWidth=1920; FixtureSupport.VideoHeight=1080;
        Until(()=>StatText().Contains("1920 × 1080"),"landscape frame arrives with fitting disabled");
        Check(win.Bounds==fixedBounds && FixtureSupport.Attached==target,"manual-size mode leaves window bounds alone on rotation");
        follow.PerformClick();
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
