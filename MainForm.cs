using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MirrorDesk
{
    internal sealed partial class MainForm : Form, IGlassHost
    {
        readonly Label footer = GlassUi.Label("",10.5f,Theme.Muted);
        readonly Label hint = GlassUi.Label("",10.5f,Theme.Muted), pathCaption = GlassUi.Label("",11,Theme.Ink,true);
        readonly Button primary = new GlassButton("",true), refresh = new GlassButton("刷新"), driver = new GlassButton("USB 驱动管理…");
        readonly Button openPath = new GlassButton("打开目录"), showPreview = new GlassButton("显示投屏窗口");
        SetupPage setupPage;
        readonly TextBox componentPath = new TextBox { ReadOnly=true, Multiline=true, WordWrap=true };
        readonly TransportPicker mode = new TransportPicker();
        readonly PalettePicker devices = Picker();
        readonly ComboBox quality = Picker();
        readonly TextBox name = new TextBox { Text = "WeiMirror", MaxLength = 60, Dock = DockStyle.Top, Margin = Padding.Empty };
        readonly CheckBox audio = new GlassToggle("在电脑上播放声音") { Checked = true };
        PreviewWindow preview;
        bool previewStopQueued;
        readonly Panel input = new GlassPanel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 700 };
        readonly System.Windows.Forms.Timer preferenceTimer = new System.Windows.Forms.Timer { Interval = 500 };
        readonly PreferencesStore preferencesStore;
        bool preferencesDirty, pinned;
        string previewLayout = "";
        bool alwaysShowToolbar;
        internal Func<Form,string> SnapshotDestination = form => ((PreviewWindow)form).ChooseSnapshotPath();
        readonly ToolTip detail = new ToolTip();
        readonly SemaphoreSlim serial = new SemaphoreSlim(1,1);
        readonly string requestedEngine;
        MirrorCore core;
        string engineRoot, sourceName, sourceId;
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly Dictionary<string,long> retryAfter = new Dictionary<string,long>(StringComparer.Ordinal);
        long sessionStarted;
        Mutex workerMutex;
        bool changing, busy, polling, listening, playing, hadVideo, closing, canClose;
        int revision, idlePolls;
        bool Wireless { get { return mode.SelectedIndex == 1; } }

        static PalettePicker Picker()
        {
            return new PalettePicker { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top,
                IntegralHeight = true, Margin = Padding.Empty };
        }
        public MainForm(string initialEngine) : this(initialEngine, null) {}
        internal MainForm(string initialEngine, PreferencesStore store)
        {
            requestedEngine = initialEngine;
            preferencesStore = store;
            Text = "微映 · 投屏控制台";
            using (var ico = Assembly.GetExecutingAssembly().GetManifestResourceStream("WeiMirror.ico"))
                if (ico != null) Icon = new Icon(ico);
            BuildControlPanel();
            var saved = preferencesStore?.Load();
            if (saved != null) {
                mode.SelectedIndex=saved.Wireless ? 1 : 0;
                quality.SelectedIndex=saved.QualityIndex; name.Text=saved.ReceiverName;
                audio.Checked=saved.PlayAudio; pinned=saved.AlwaysOnTop; previewLayout=saved.PreviewLayout;
                appearance.SelectedIndex=saved.ThemeIndex;
                darkMode.Checked=saved.DarkMode;
                alwaysShowToolbar=showToolbar.Checked=saved.AlwaysShowToolbar;
                int fontIndex=interfaceFont.FindStringExact(saved.InterfaceFont);
                interfaceFont.SelectedIndex=Math.Max(0,fontIndex);
                textPercent.SelectedIndex=(saved.TextPercent-90)/10;
                interfaceSize.SelectedIndex=saved.InterfaceSize;
            }
            ChangeAppearance();
            ChangeInterface(false);
            appearance.SelectedIndexChanged+=delegate { ChangeAppearance(); PreferencesChanged(); };
            darkMode.CheckedChanged+=delegate { ChangeAppearance(); PreferencesChanged(); };
            interfaceFont.SelectedIndexChanged+=delegate { ChangeInterface(false); PreferencesChanged(); };
            textPercent.SelectedIndexChanged+=delegate { ChangeInterface(false); PreferencesChanged(); };
            interfaceSize.SelectedIndexChanged+=delegate { ChangeInterface(true); PreferencesChanged(); };
            showToolbar.CheckedChanged+=delegate {
                alwaysShowToolbar=showToolbar.Checked;
                if (preview!=null) preview.AlwaysShowToolbar=alwaysShowToolbar;
                PreferencesChanged();
            };
            preferenceTimer.Tick += delegate { SavePreferences(); };
            footer.TextChanged += delegate { detail.SetToolTip(footer, footer.Text); };
            UpdateControls();
            mode.SelectedIndexChanged += async delegate { if (!changing) { PreferencesChanged(); await ChangeMode(); } };
            quality.SelectedIndexChanged += delegate { PreferencesChanged(); };
            name.TextChanged += delegate { UpdateAirReceiverLabel(); PreferencesChanged(); };
            refresh.Click += async delegate { await ActionAsync(() => Scan(true)); };
            primary.Click += async delegate { await ActionAsync(PrimaryAction); };
            devices.SelectedIndexChanged += delegate { UpdateControls(); ShowSelectedUsbDiagnosis(); };
            audio.CheckedChanged += async delegate {
                if (changing) return;
                if (core == null || !playing) { PreferencesChanged(); return; }
                bool value = audio.Checked;
                await ActionAsync(async () => {
                    try { await OnCore(() => core.SetAudio(value)); PreferencesChanged(); }
                    catch { changing = true; audio.Checked = !value; changing = false; throw; }
                });
            };
            driver.Click += delegate { OpenDriver(); };
            openPath.Click += delegate { OpenComponentFolder(); };
            showPreview.Click += delegate {
                var window = preview;
                if (window==null || window.IsDisposed) return;
                if (window.WindowState==FormWindowState.Minimized) window.WindowState=FormWindowState.Normal;
                window.Show(); window.Activate();
            };
            timer.Tick += async delegate { await Poll(); };
            FormClosing += OnClosing;
            Shown += async delegate { await ActionAsync(Initialize); if (!IsDisposed && !closing && !canClose) timer.Start(); };
        }

        void PreferencesChanged()
        {
            if (preferencesStore==null || changing || closing) return;
            preferencesDirty=true;
            preferenceTimer.Stop();
            preferenceTimer.Start();
        }

        void SavePreferences()
        {
            preferenceTimer.Stop();
            if (!preferencesDirty || preferencesStore==null) return;
            var next=new UserPreferences {
                Wireless=Wireless, ReceiverName=name.Text, QualityIndex=quality.SelectedIndex,
                PlayAudio=audio.Checked, AlwaysOnTop=pinned, PreviewLayout=previewLayout,
                ThemeIndex=appearance.SelectedIndex, DarkMode=darkMode.Checked, AlwaysShowToolbar=alwaysShowToolbar,
                InterfaceFont=InterfaceFont, TextPercent=TextPercent, InterfaceSize=interfaceSize.SelectedIndex
            };
            preferencesDirty=!preferencesStore.TrySave(next);
            if (preferencesDirty && !closing) footer.Text="当前设置已生效，写入本机偏好文件失败。";
        }

        void UpdateControls()
        {
            if (IsDisposed || Disposing) return;
            if (usbDetails == null) return;
            bool wifi = Wireless;
            UpdateAirReceiverLabel();
            ShowConnectionDetails(wifi);
            mode.SelectionLocked = busy || closing;
            bool sessionRequested = playing || WaitingForUsb;
            refresh.Enabled = core != null && !busy && !sessionRequested;
            devices.EmptyText = core == null ? "等待准备投屏组件" : "未检测到 USB 设备";
            devices.Enabled = devices.Items.Count>0 && !sessionRequested && !busy;
            name.Enabled = quality.Enabled = !listening && !busy;
            audio.Enabled = !busy && !closing;
            primary.Enabled = !busy && !closing && (core == null || wifi || sessionRequested || devices.SelectedItem is Device);
            primary.Text = core == null ? "准备投屏组件…" : WaitingForUsb ? "停止并取消重连" : playing || listening ? "停止接收" : wifi ? "开启 AirPlay 接收" : "开始 USB 投屏";
            driver.Enabled = engineRoot != null && !busy && !sessionRequested;
            hint.Text = wifi ? "同一网络下，打开设备的「屏幕镜像」。" : "连接并解锁设备，按提示选择「信任」。";
            showPreview.Enabled = preview != null && !preview.IsDisposed && !closing;
            settingsButton.Enabled = !closing;
            string path = engineRoot ?? RuntimePack.ComponentDownloadDirectory;
            pathCaption.Text = engineRoot == null ? "组件下载目录" : "当前组件目录";
            if (!String.Equals(componentPath.Text,path,StringComparison.Ordinal)) {
                componentPath.Text=path;
                detail.SetToolTip(componentPath,path);
            }
            openPath.Enabled = Directory.Exists(path);
            UpdateDashboardStatus();
        }

        void UpdateAirReceiverLabel()
        {
            string receiver=name.Text.Trim();
            if (receiver.Length==0) receiver="未命名";
            if (airReceiver.Text==receiver) return;
            airReceiver.Text=receiver;
            detail.SetToolTip(airReceiver,receiver);
        }

        void OpenComponentFolder()
        {
            string path = engineRoot ?? RuntimePack.ComponentDownloadDirectory;
            try {
                if (!Directory.Exists(path)) { footer.Text="准备组件后即可打开该目录。"; return; }
                using (Process.Start(new ProcessStartInfo(path) { UseShellExecute=true })) {}
            } catch (Exception ex) { footer.Text=ex.GetBaseException().Message; }
        }

        void ClosePreview()
        {
            var window=preview;
            preview=null;
            if (window==null) return;
            window.StopRequested-=OnPreviewClose;
            window.PinChanged-=OnPinChanged;
            window.PreviewLayoutChanged-=OnPreviewLayoutChanged;
            window.SnapshotRequested-=OnSnapshotRequested;
            window.CloseFromController();
        }

        void OnPinChanged(object sender,EventArgs args)
        {
            if (sender!=preview || closing) return;
            pinned=preview.Pinned;
            PreferencesChanged();
        }

        void OnPreviewLayoutChanged(object sender,EventArgs args)
        {
            if (sender!=preview || closing) return;
            string value=preview.CapturePreviewLayout();
            if (String.IsNullOrEmpty(value) || previewLayout==value) return;
            previewLayout=value;
            PreferencesChanged();
        }

        async void OnSnapshotRequested(object sender,EventArgs args)
        {
            var window=sender as PreviewWindow;
            if (window==null || window!=preview || !playing || !hadVideo || busy || closing || previewStopQueued) return;
            await ActionAsync(async () => {
                window.SetSnapshotBusy(true);
                try {
                    using (Bitmap frame=await OnCore(() => core.CaptureSnapshot())) {
                        if (closing || previewStopQueued) return;
                        string path=SnapshotDestination(window);
                        if (String.IsNullOrEmpty(path)) return;
                        await Task.Run(() => MirrorCore.SaveSnapshot(frame,path));
                        window.ShowSnapshotResult("截图已保存");
                        footer.Text="截图已保存于 " + path;
                    }
                } catch (Exception ex) {
                    window.ShowSnapshotResult("截图失败：" + ex.GetBaseException().Message);
                    throw;
                } finally { window.SetSnapshotBusy(false); }
            });
        }

        async void OnPreviewClose(object sender, EventArgs args)
        {
            if (closing || sender!=preview) return;
            previewStopQueued=true;
            await DrainPreviewClose();
        }

        async Task DrainPreviewClose()
        {
            if (!previewStopQueued || busy || closing) return;
            previewStopQueued=false;
            await ActionAsync(() => StopReception());
        }

        async Task ActionAsync(Func<Task> action)
        {
            if (busy || closing) return;
            busy = true; revision++; UpdateControls();
            try { await action(); }
            catch (Exception ex) { if (!closing) footer.Text = ex.GetBaseException().Message; }
            finally { busy = false; UpdateControls(); }
            await DrainPreviewClose();
        }
        async Task<T> OnCore<T>(Func<T> op)
        {
            await serial.WaitAsync();
            try { return await Task.Run(op); }
            finally { serial.Release(); }
        }
        async Task OnCore(Action op) { await OnCore(() => { op(); return true; }); }
        async Task Initialize()
        {
            string path = requestedEngine ?? await Task.Run(() => RuntimePack.Find());
            if (!String.IsNullOrWhiteSpace(path)) await OpenEngine(path);
            else footer.Text = "请先准备组件。";
        }
        async Task OpenEngine(string path)
        {
            string current = Application.ExecutablePath;
            string worker = await Task.Run(() => RuntimePack.PrepareHost(path, current));
            if (closing) return;
            if (!String.Equals(Path.GetFullPath(worker),Path.GetFullPath(current),StringComparison.OrdinalIgnoreCase))
            {
                SavePreferences();
                string arg = "--engine \"" + Path.GetFullPath(path).TrimEnd('\\') + "\" --view " + ViewToken();
                using (Process.Start(new ProcessStartInfo(worker,arg) { WorkingDirectory=path, UseShellExecute=false })) {}
                canClose=true; Close(); return;
            }
            if (workerMutex == null)
            {
                bool owned;
                workerMutex = new Mutex(true, @"Local\WeiMirror-Desktop-1", out owned);
                if (!owned) { workerMutex.Dispose(); workerMutex=null; MessageBox.Show(this,"微映接收端已打开。","微映"); canClose=true; Close(); return; }
            }
            footer.Text = "正在启动接收组件…";
            engineRoot = path;
            core = await OnCore(() => new MirrorCore(path));
            if (!Wireless) await Scan(true); else footer.Text = "接收组件已就绪。";
        }
        internal string ViewToken()
        {
            return String.Join(".", mode.SelectedIndex, ((WirelessProfile)quality.SelectedItem).Id, audio.Checked ? 1 : 0,
                Convert.ToBase64String(Encoding.UTF8.GetBytes(name.Text)), pinned ? 1 : 0);
        }
        internal void RestoreView(string token)
        {
            if (String.IsNullOrEmpty(token) || token.Length > 512) return;
            string[] bits = token.Split('.');
            int transport, profileId;
            if ((bits.Length!=4 && bits.Length!=5) || !Int32.TryParse(bits[0],out transport) || transport<0 || transport>1 ||
                !Int32.TryParse(bits[1],out profileId) ||
                (bits[2]!="1" && bits[2]!="0") || (bits.Length==5 && bits[4]!="1" && bits[4]!="0")) return;
            int preset=WirelessProfile.IndexFromSavedId(profileId);
            if (preset<0) return;
            string receiver;
            try { receiver=Encoding.UTF8.GetString(Convert.FromBase64String(bits[3])); } catch (FormatException) { return; }
            changing=true;
            try { mode.SelectedIndex=transport; quality.SelectedIndex=preset; audio.Checked=bits[2]=="1"; name.Text=receiver;
                if (bits.Length==5) pinned=bits[4]=="1"; }
            finally { changing=false; UpdateControls(); }
        }
        async Task ChangeMode()
        {
            if (busy || closing) return;
            if (core==null) {
                footer.Text="请先准备组件。";
                UpdateControls();
                return;
            }
            bool wireless=Wireless;
            await ActionAsync(async () => {
                if (playing || listening || WaitingForUsb) await StopReception();
                if (!wireless) await Scan(true);
                else footer.Text="点「开启 AirPlay 接收」，让 iPhone 或 iPad 发现这台电脑。";
            });
        }
        async Task Scan(bool metadata)
        {
            if (core == null || closing) return;
            var list = await OnCore(() => core.GetUsbDevices(metadata));
            if (closing || Wireless) return;
            await ShowUsbDiscovery(list,true,revision);
        }
        void SetDevices(Device[] list)
        {
            string old = (devices.SelectedItem as Device)?.Id;
            if (devices.Items.Count == list.Length && devices.Items.Cast<Device>().Zip(list,(a,b)=>a.Id==b.Id && a.Name==b.Name && a.SameUsbDiagnosis(b)).All(x=>x)) return;
            devices.BeginUpdate();
            try {
                devices.Items.Clear(); devices.Items.AddRange(list);
                var match = list.FirstOrDefault(d=>d.Id==old) ?? list.FirstOrDefault();
                if (match != null) devices.SelectedItem = match;
            } finally { devices.EndUpdate(); }
            UpdateControls();
        }
        async Task PrimaryAction()
        {
            if (core == null) {
                ShowConsolePage(setupPage);
                return;
            }
            if (playing || listening || WaitingForUsb) { await StopReception(); return; }
            if (Wireless) {
                string receiver = name.Text.Trim();
                if (receiver.Length == 0) { footer.Text = "请在「设置 → 投屏」中填写接收端名称。"; return; }
                var profile=(WirelessProfile)quality.SelectedItem;
                footer.Text = "正在启动 AirPlay 接收…";
                await OnCore(() => core.StartWireless(receiver,profile.Width,profile.Height,profile.Fps));
                listening=true; timer.Interval=180;
                footer.Text="正在广播 AirPlay 名称…";
            } else {
                var device = devices.SelectedItem as Device;
                if (device != null) await StartDevice(device);
            }
        }
        PreviewWindow CreatePreview(Device device)
        {
            var window = new PreviewWindow { Icon=this.Icon, Pinned=pinned, AlwaysShowToolbar=alwaysShowToolbar };
            window.ApplyPalette(palette);
            window.ToolbarPreferenceChanged+=delegate { showToolbar.Checked=window.AlwaysShowToolbar; };
            window.RestorePreviewLayout(previewLayout);
            preview=window;
            window.SetSource(device.Name);
            window.StopRequested+=OnPreviewClose;
            window.PinChanged+=OnPinChanged;
            window.PreviewLayoutChanged+=OnPreviewLayoutChanged;
            window.SnapshotRequested+=OnSnapshotRequested;
            window.Surface.HandleCreated+=async delegate {
                if (!playing || closing || preview!=window) return;
                IntPtr newHwnd=window.Surface.Handle;
                try { await OnCore(() => core.ReattachPreview(newHwnd)); }
                catch (Exception ex) { if (!closing && preview==window) footer.Text=ex.GetBaseException().Message; }
            };
            return window;
        }

        async Task StartDevice(Device device, PreviewWindow existingWindow=null)
        {
            bool recovering=existingWindow!=null;
            int check=revision;
            var window=existingWindow ?? CreatePreview(device);
            bool attached=false;
            try {
                if (!recovering) window.Show();
                IntPtr hwnd=window.Surface.Handle;
                bool playAudio=audio.Checked;
                footer.Text="正在连接 " + (recovering ? usbTarget.Name : device.Name) + "…";
                await OnCore(() => core.Start(device,hwnd,playAudio));
                attached=true;
                // Stop, mode changes and close can run while USB renegotiates off the UI thread.
                // Their stop/dispose operation is queued behind Start on the same native gate.
                if (recovering && !UsbRecoveryCurrent(check,window)) return;
                IntPtr currentHwnd=window.Surface.Handle;
                if (currentHwnd!=hwnd) await OnCore(() => core.ReattachPreview(currentHwnd));
                while (recovering && UsbRecoveryCurrent(check,window) && audio.Checked!=playAudio) {
                    playAudio=audio.Checked;
                    bool next=playAudio;
                    await OnCore(() => core.SetAudio(next));
                }
                if (recovering && !UsbRecoveryCurrent(check,window)) return;
            } catch {
                try { if (attached && (!recovering || UsbRecoveryCurrent(check,window))) await OnCore(() => core.Stop()); }
                finally { if (!recovering) ClosePreview(); }
                throw;
            }
            if (!device.Wireless) {
                if (!recovering) { usbTarget=device; usbRetryAttempts=0; }
                ResetUsbPresenceProbe();
            }
            playing=true; hadVideo=false; sourceName=device.Wireless ? device.Name : usbTarget.Name; sourceId=device.Id;
            sessionStarted=clock.ElapsedMilliseconds; timer.Interval=700;
            footer.Text="正在等待 "+sourceName+" 的第一帧…";
            if (recovering) { window.SetOverlay(footer.Text); UpdateControls(); }
        }
        async Task StopReception(bool keepBroadcast = false)
        {
            if (core == null) return;
            usbTarget=null; usbRetryAttempts=0; idlePolls=4;
            bool stopBroadcast=listening && !keepBroadcast;
            try { await OnCore(() => { try { core.Stop(); } finally { if(stopBroadcast) core.StopWireless(); } }); }
            finally {
                if (!keepBroadcast) { listening=false; retryAfter.Clear(); }
                playing=hadVideo=false; sourceId=sourceName=null; timer.Interval=listening ? 180 : 700;
                ClosePreview();
            }
            footer.Text=listening ? "AirPlay 接收已就绪，等待 iPad 连接。" : "已停止接收。";
        }
        async Task Poll()
        {
            if (busy || polling || previewStopQueued || closing || core == null) return;
            polling=true;
            int check=revision;
            try {
                Device[] peers=null;
                if (listening) {
                    var state=await OnCore(() => core.GetWirelessState());
                    if (check!=revision || closing) return;
                    if (state==WirelessState.Failed || state==WirelessState.Off) {
                        var profile=(WirelessProfile)quality.SelectedItem;
                        string guidance=profile.UsesCustomHost
                            ? profile + " 接收服务未能启动或已中断。请检查组件与端口占用，或切换到高清自动 / 1080p60 后重连。"
                            : "AirPlay 接收服务已中断。请检查其他接收程序或专用网络防火墙权限，然后重新开启接收。";
                        await StopAfterPoll(guidance); return;
                    }
                    if (state!=WirelessState.Ready) { footer.Text="正在启动 AirPlay 广播…"; return; }
                    peers=await OnCore(() => core.GetWirelessDevices());
                    if (check!=revision || closing) return;
                    foreach (string id in retryAfter.Keys.Where(id => !peers.Any(p=>p.Id==id)).ToArray()) retryAfter.Remove(id);
                    if (playing && !peers.Any(p=>p.Id==sourceId)) {
                        await ActionAsync(() => StopReception(true)); return;
                    }
                }
                if (WaitingForUsb) { await TryResumeUsb(check); return; }
                if (playing) {
                    StreamStatus s;
                    try { s=await OnCore(() => core.GetStatus()); }
                    catch (Exception ex) when (usbTarget!=null) {
                        if (check!=revision || closing) return;
                        if (++usbStatusErrors<2) throw;
                        await ActionAsync(() => SuspendUsb(ex.GetBaseException().Message));
                        return;
                    }
                    if (check!=revision || closing) return;
                    usbStatusErrors=0;
                    if (s.State==6 || s.State==7 || s.State==0) {
                        string message=ConnectionDiagnostics.Failure(s,listening);
                        if (usbTarget!=null) {
                            bool recover=s.FailureKind==0 || s.FailureKind==8;
                            if (!recover) {
                                bool? present=await ProbeUsbPresence(usbTarget);
                                if (check!=revision || closing) return;
                                recover=present==false;
                            }
                            if (recover) { await ActionAsync(() => SuspendUsb(message)); return; }
                        }
                        if (listening) retryAfter[sourceId]=clock.ElapsedMilliseconds+5000;
                        await StopAfterPoll(message,listening);
                        return;
                    }
                    if (usbTarget!=null && await CheckStalledUsb(s,check)) return;
                    if (check!=revision || closing) return;
                    if (s.State==4 && s.Frames>0 && s.Width>0 && s.Height>0) {
                        if (!hadVideo) { hadVideo=true; UpdateDashboardStatus(); }
                        timer.Interval=700;
                        preview.SetOverlay(null);
                        preview.SetSnapshotAvailable(!s.ProtectedVideo);
                        preview.SetVideoSize(s.Width,s.Height);
                        preview.SetStats(s.Width+" × "+s.Height+"   "+s.Fps.ToString("0.#")+" fps");
                        if (s.ProtectedVideo) {
                            footer.Text="画面持续为黑，可能是受保护内容。" + (s.ProtectedAudioActive ? "仍在接收音频。" : "");
                        } else footer.Text=(Wireless ? "AirPlay  ·  " : "USB  ·  ")+sourceName+
                            "  ·  "+s.Width+" × "+s.Height+"  ·  "+s.Fps.ToString("0.#")+" fps";
                    } else {
                        preview.SetStats("");
                        preview.SetSnapshotAvailable(false);
                        if (hadVideo) {
                            hadVideo=false; UpdateDashboardStatus(); sessionStarted=clock.ElapsedMilliseconds; timer.Interval=180;
                            preview.SetOverlay("视频信号已中断，等待画面恢复…");
                        }
                        footer.Text=ConnectionDiagnostics.FrameWait(s,listening,clock.ElapsedMilliseconds-sessionStarted);
                    }
                } else if (listening) {
                    if (peers.Length==0) { footer.Text=ConnectionDiagnostics.AirWaiting(name.Text); return; }
                    Device next=peers.FirstOrDefault(p=>!retryAfter.ContainsKey(p.Id) || retryAfter[p.Id]<=clock.ElapsedMilliseconds);
                    if (next==null) return;
                    await ActionAsync(async () => {
                        try { await StartDevice(next); retryAfter.Remove(next.Id); }
                        catch { retryAfter[next.Id]=clock.ElapsedMilliseconds+5000; throw; }
                    });
                } else if (!Wireless && ++idlePolls % 5 == 0) {
                    var list=await OnCore(() => core.GetUsbDevices(false));
                    if (check==revision && !closing) await ShowUsbDiscovery(list,false,check);
                }
            } catch (Exception ex) {
                if (check==revision && !closing) footer.Text=ex.GetBaseException().Message;
            } finally { polling=false; }
        }
        async Task StopAfterPoll(string message,bool keepBroadcast=false)
        {
            await ActionAsync(async () => {
                await StopReception(keepBroadcast);
                if (!String.IsNullOrWhiteSpace(message)) footer.Text=message;
            });
        }
        void OpenDriver()
        {
            if (engineRoot==null) return;
            try {
                using (Process.Start(new ProcessStartInfo(Path.Combine(engineRoot,"iPhoneMirror.Driver.exe")) { WorkingDirectory=engineRoot,UseShellExecute=true })) {}
                footer.Text="完成 USB 驱动配置后，请点「刷新」。";
            } catch (Exception ex) { footer.Text=ex.GetBaseException().Message; }
        }
        async void OnClosing(object sender,FormClosingEventArgs e)
        {
            if (canClose) return;
            e.Cancel=true;
            if (closing) return;
            closing=true; revision++; timer.Stop(); UpdateControls(); footer.Text="正在释放接收设备…";
            setupPage.CancelPreparation();
            setupPage.Enabled=false;
            try {
                while (busy || polling || setupPage.IsPreparing) await Task.Delay(30);
                SavePreferences();
                if (core!=null) await OnCore(() => core.Dispose());
            } catch (Exception) { }
            finally {
                ClosePreview();
                if (workerMutex!=null) { workerMutex.ReleaseMutex(); workerMutex.Dispose(); workerMutex=null; }
                canClose=true; BeginInvoke((Action)Close);
            }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) {
                preferenceTimer.Dispose(); timer.Dispose(); detail.Dispose(); serial.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
