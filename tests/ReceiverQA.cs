using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MirrorDesk;

static class ReceiverQA
{
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] static extern uint GetPixel(IntPtr dc, int x, int y);
    static int checks;
    static void Check(bool condition, string text) {
        if (!condition) throw new Exception("FAIL: " + text);
        checks++; Console.WriteLine("PASS " + text);
    }
    static void Expect<T>(Action work, string name) where T : Exception {
        try { work(); } catch (T) { Check(true, name); return; }
        throw new Exception("Expected " + typeof(T).Name + ": " + name);
    }
    static uint Pixel(Control c) {
        var dc = GetDC(c.Handle);
        try { return GetPixel(dc, 8, 8); } finally { ReleaseDC(c.Handle, dc); }
    }
    static void VerifySnapshots(MirrorCore core, string runtime) {
        string dir=Path.Combine(Path.GetDirectoryName(runtime),"snapshots");
        Directory.CreateDirectory(dir);
        string destination=Path.Combine(dir,"native.png");
        using (Bitmap frame=core.CaptureSnapshot()) {
            Check(frame.Size==new Size(3,2) && frame.GetPixel(0,0).ToArgb()==Color.FromArgb(255,80,60,40).ToArgb() &&
                frame.GetPixel(2,1).ToArgb()==Color.FromArgb(255,83,61,42).ToArgb(),"native snapshot decodes padded top-down BGRA at source size");
            MirrorCore.SaveSnapshot(frame,destination);
            using (var restored=new Bitmap(destination)) {
                Check(restored.RawFormat.Guid==System.Drawing.Imaging.ImageFormat.Png.Guid &&
                    restored.GetPixel(0,1).ToArgb()==Color.FromArgb(255,81,61,40).ToArgb(),"saved PNG contains source frame pixels independent of preview size");
            }
            byte[] original=File.ReadAllBytes(destination);
            using (File.Open(destination,FileMode.Open,FileAccess.Read,FileShare.None)) {
                Expect<IOException>(()=>MirrorCore.SaveSnapshot(frame,destination),"locked destination reports snapshot write failure");
            }
            Check(File.ReadAllBytes(destination).SequenceEqual(original) && Directory.GetFiles(dir).Length==1,
                "failed screenshot preserves prior PNG and cleans temporary file");
        }
        FixtureSupport.GrowOnCopy=true;
        using (Bitmap resized=core.CaptureSnapshot()) {
            Check(resized.Size==new Size(4,2) && resized.GetPixel(3,1).B==43,"resolution change between query and copy uses new dimensions");
            MirrorCore.SaveSnapshot(resized,destination);
        }
        using (var image=new Bitmap(destination)) Check(image.Size==new Size(4,2),"atomic PNG overwrite commits complete updated frame");
        FixtureSupport.WaitingForFrames=true;
        Expect<InvalidOperationException>(()=>core.CaptureSnapshot(),"no-frame state cannot produce stale image");
        FixtureSupport.WaitingForFrames=false; FixtureSupport.FrameProtected=true;
        int count=FixtureSupport.FrameCopies;
        Expect<InvalidOperationException>(()=>core.CaptureSnapshot(),"protected frame rejected");
        Check(count==FixtureSupport.FrameCopies,"protected status prevents copy");
        FixtureSupport.FrameProtected=false; FixtureSupport.ProtectOnCopy=true;
        Expect<InvalidOperationException>(()=>core.CaptureSnapshot(),"status switching to protected during frame copy rejects result");
        FixtureSupport.ProtectOnCopy=false; FixtureSupport.FrameProtected=false; FixtureSupport.BadFrameFormat=true;
        Expect<InvalidDataException>(()=>core.CaptureSnapshot(),"unsupported pixel format is rejected");
        FixtureSupport.BadFrameFormat=false;
        using (Bitmap recovered=core.CaptureSnapshot()) Check(recovered.Width==4,"snapshot recovers after signal or permission changes");
    }
    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] static extern bool TerminateProcess(IntPtr process, uint code);
    [STAThread] static int Main(string[] args) {
        int result = Run(args);
        Console.Out.Flush(); Console.Error.Flush();
        // The IL native-export fixture loads the CLR into an instrumented test
        // process. The application and its native core were disposed in Run.
        TerminateProcess(GetCurrentProcess(), (uint)result);
        return result;
    }
    static int Run(string[] args) {
        try {
            if (args.Length != 1) throw new Exception("Requires an isolated fixture runtime.");
            string runtime = args[0];
            FixtureSupport.Version = 17;
            Expect<NotSupportedException>(() => { using (var bad = new MirrorCore(runtime)) { } }, "ABI mismatch rejected");
            FixtureSupport.Version = 18;
            using (var form = new Form())
            using (var pane = new Panel())
            using (var second = new Panel())
            using (var core = new MirrorCore(runtime)) {
                Expect<InvalidOperationException>(() => new MirrorCore(runtime), "concurrent native core owner rejected");
                form.ShowInTaskbar = false; form.Text = "Receiver integration fixture";
                form.StartPosition = FormStartPosition.Manual; form.Location = new Point(40,40);
                form.ClientSize = new Size(100,50);
                pane.SetBounds(0,0,50,50); second.SetBounds(50,0,50,50);
                form.Controls.Add(pane); form.Controls.Add(second);
                form.Show(); Application.DoEvents();
                Check(core.GetWirelessState() == WirelessState.Off, "wireless starts idle");
                Device usb = core.GetUsbDevices(true).Single();
                Check(usb.Id == FixtureSupport.UsbId && usb.Name == "书桌 iPad" && !usb.Wireless, "USB enumeration and UTF-16 source identity");
                var env=core.ReadUsbEnvironment();
                Check(env.HasValue && env.Value.PhysicalDevices==1 && env.Value.ServiceRunning!=0,
                    "USB environment native ABI reads Windows physical count and mux/service evidence");
                FixtureSupport.UsbNeedsTrust=true;
                var trust=core.GetUsbDevices(true).Single();
                Check(trust.ConnectionState==2 && !trust.PairRecord && !trust.DeviceInfoReadable &&
                    trust.ConnectionDetail=="Waiting for this computer to be trusted",
                    "USB state and trust evidence arrive from the same device record");
                FixtureSupport.UsbNeedsTrust=false;
                core.Start(usb, pane.Handle, false);
                Check(core.HasSession && FixtureSupport.Attached == pane.Handle && !FixtureSupport.Audio, "USB capture attaches child HWND with audio disabled");
                uint first = Pixel(pane);
                core.RefreshPreview();
                uint next = Pixel(pane);
                Check(first == 0x00ff5544 && next == 0x0033cc77, "two generated frames painted and read back from the live child HWND (" + first.ToString("x") + ", " + next.ToString("x") + ")");
                var status = core.GetStatus();
                Check(status.Width == 1920 && status.Height == 1080 && status.Fps == 59.5 && status.Frames == 9876543210 && status.Message == "测试画面", "x64 status fields and Unicode marshalled");
                VerifySnapshots(core,runtime);
                core.SetAudio(true); Check(FixtureSupport.Audio, "live audio switch propagated to session");
                core.ReattachPreview(second.Handle);
                Check(FixtureSupport.Attached == second.Handle, "preview survives HWND replacement");
                core.Stop(); Check(!core.HasSession && !FixtureSupport.Allocated && !FixtureSupport.Streaming, "USB stop destroys session");
                FixtureSupport.AttachFails = true;
                Expect<InvalidOperationException>(() => core.Start(usb, pane.Handle, false), "render attachment failure surfaced");
                Check(!core.HasSession && !FixtureSupport.Allocated, "failed attachment releases session for retry");
                FixtureSupport.AttachFails = false;
                Check(MirrorCore.ValidateReceiverName(new string('x', 50)).Length == 50, "maximum RAOP instance friendly-name length accepted");
                Expect<ArgumentException>(() => MirrorCore.ValidateReceiverName(new string('x',51)), "RAOP instance prefix counts toward 63-byte DNS-SD limit");
                Expect<ArgumentException>(() => MirrorCore.ValidateReceiverName(new string('汉',17)), "RAOP instance name limit uses UTF-8 bytes");
                Expect<ArgumentException>(() => core.StartWireless(new string('汉',22)), "DNS-SD receiver name length checked as UTF-8");
                Expect<ArgumentException>(() => core.StartWireless("valid",123,456,60), "unsupported wireless profile rejected before launch");
                string dep = Path.Combine(runtime,"Wireless","swscale-5.dll");
                File.Delete(dep);
                Expect<FileNotFoundException>(() => core.StartWireless("客厅接收"), "missing wireless dependency reported");
                File.WriteAllBytes(dep, new byte[0]);
                core.StartWireless("客厅接收",1280,720,30);
                Check(core.WirelessReady && FixtureSupport.ReceiverName == "客厅接收" && FixtureSupport.ReceiverWidth == 1280 && FixtureSupport.ReceiverHeight == 720 && FixtureSupport.ReceiverFps == 30, "wireless start passes negotiated name and profile to core");
                core.StartWireless("客厅接收",1280,720,30);
                Check(FixtureSupport.Calls.Count(x => x == "air-start") == 1, "repeated wireless startup is idempotent");
                Device air = core.GetWirelessDevices().Single();
                Check(air.Wireless && air.Id == FixtureSupport.AirId && air.Name == "客厅 iPad", "wireless enumeration and source identity");
                core.Start(air,pane.Handle,true);
                Check(FixtureSupport.Audio && FixtureSupport.Calls.Contains("air-session") && FixtureSupport.Attached == pane.Handle, "wireless source uses independent session with audio");
                core.StopWireless();
                Check(!core.WirelessReady && !core.HasSession && !FixtureSupport.Allocated, "wireless shutdown releases active wireless session");
                string oldDirectory=Environment.GetEnvironmentVariable("WEIMIRROR_720_RECEIVER_DIRECTORY");
                core.StartWireless("低延迟",1280,720,60);
                Check(FixtureSupport.ReceiverFps==30 && FixtureSupport.ReceiverWidth==1280 && FixtureSupport.ReceiverHeight==720 &&
                    FixtureSupport.ReceiverHost==typeof(MirrorCore).Assembly.Location && FixtureSupport.ReceiverLibraryDirectory==Path.Combine(runtime,"Wireless") &&
                    Environment.GetEnvironmentVariable("WEIMIRROR_720_RECEIVER_DIRECTORY")==oldDirectory,
                    "720p60 routes only its custom host through the compatible carrier and restores launch environment");
                core.StopWireless();
                core.Start(usb,pane.Handle,false);
                core.StartWireless("QA");
                core.StopWireless();
                Check(core.HasSession, "stopping wireless listener preserves USB capture");
                core.Dispose(); core.Dispose();
                Check(!FixtureSupport.Allocated && FixtureSupport.Calls.Last() == "shutdown", "double disposal cleans up once");
                Expect<ObjectDisposedException>(() => core.GetStatus(), "disposed core cannot read status");
            }
            Console.WriteLine("Checks: " + checks);
            return 0;
        } catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
