// Test-only native ABI implementation. Never install or ship this as a capture engine.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

public static class FixtureSupport
{
    public const string UsbId = "USB-TEST-1095";
    public const string ForeignUsbId = "USB-TEST-2072";
    public const string AirId = "airplay:ipad-test";
    public const string SecondAirId = "airplay:second-ipad";
    public static readonly List<string> Calls = new List<string>();
    public static volatile bool Audio, AirStarted, AttachFails, ShowAirDevice = true;
    public static volatile bool ShowSecondAirDevice, RefuseFirstAirDevice, WaitingForFrames;
    public static int SessionFps;
    public static volatile bool ShowUsbDevice = true, ShowForeignUsb, UsbLossIsTerminal, RefuseUsbSession;
    public static int UsbEnumerations, ForeignUsbAttempts, UsbAttempts;
    public static volatile int VideoWidth=1920, VideoHeight=1080;
    public static volatile bool UsbNeedsTrust;
    public static volatile int PhysicalAppleDevices = 1;
    public static volatile string ActiveId;
    public static volatile int FailedAirStarts;
    public static volatile bool FrameProtected, ProtectOnCopy, GrowOnCopy, BadFrameFormat;
    public static volatile int FrameCopies;
    static bool grown;
    public static readonly ManualResetEventSlim ContinueAttach = new ManualResetEventSlim(true);
    public static readonly ManualResetEventSlim EnteredAttach = new ManualResetEventSlim(false);
    public static readonly ManualResetEventSlim ContinueCopy = new ManualResetEventSlim(true);
    public static readonly ManualResetEventSlim EnteredCopy = new ManualResetEventSlim(false);
    public static bool LastDestroyHadLivePreview;
    public static int Version = 18;
    public static IntPtr Attached;
    public static bool Allocated, Streaming;
    public static int FrameSerial;
    public static string ReceiverName, ReceiverHost, ReceiverLibraryDirectory;
    public static int ReceiverWidth, ReceiverHeight, ReceiverFps;
    static readonly IntPtr LastError = Marshal.StringToHGlobalUni("Synthetic fixture error");
    static readonly object Sync = new object();
    static void WriteText(IntPtr p, int offset, string text) {
        char[] units = (text + "\0").ToCharArray();
        Marshal.Copy(units, 0, IntPtr.Add(p, offset), units.Length);
    }
    static int Trace(string text) { lock (Sync) Calls.Add(text); return 0; }
    public static int ApiVersion() { return Version; }
    public static int Initialize() { return Trace("initialize"); }
    public static void Shutdown() { Trace("shutdown"); }
    public static IntPtr Error() { return LastError; }
    public static int EnvironmentInfo(IntPtr info) {
        if (info==IntPtr.Zero || Marshal.ReadInt32(info,0)!=1136) return -1;
        Marshal.WriteInt32(info,4,18);
        for (int offset=8; offset<=20; offset+=4) Marshal.WriteInt32(info,offset,1);
        Marshal.WriteInt32(info,24,PhysicalAppleDevices);
        return 0;
    }
    public static int UsbDevices(IntPtr buffer, IntPtr count, int refresh) { return Enumerate(buffer, count, false); }
    public static int AirDevices(IntPtr buffer, IntPtr count) { return Enumerate(buffer, count, true); }
    static int Enumerate(IntPtr buffer, IntPtr count, bool air) {
        int capacity = Marshal.ReadInt32(count);
        bool first=air ? AirStarted && ShowAirDevice : ShowUsbDevice;
        bool second=air ? AirStarted && ShowSecondAirDevice : ShowForeignUsb;
        int n=(first ? 1 : 0)+(second ? 1 : 0);
        if (!air && buffer == IntPtr.Zero) Interlocked.Increment(ref UsbEnumerations);
        Marshal.WriteInt32(count, n);
        if (n == 0 || buffer == IntPtr.Zero) return 0;
        if (capacity < n) return -3;
        if (air) {
            if (first) WriteDevice(buffer, AirId, "客厅 iPad", true);
            if (second) WriteDevice(IntPtr.Add(buffer, first ? 1184 : 0), SecondAirId, "第二台 iPad", true);
        } else {
            // The unrelated USB peer deliberately has the same friendly name and comes first.
            if (second) WriteDevice(buffer, ForeignUsbId, "书桌 iPad", false);
            if (first) WriteDevice(IntPtr.Add(buffer, second ? 1184 : 0), UsbId, "书桌 iPad", false);
        }
        return 0;
    }
    static void WriteDevice(IntPtr buffer, string id, string name, bool air) {
        Marshal.Copy(new byte[1184], 0, buffer, 1184);
        Marshal.WriteInt32(buffer, 0, 1184); Marshal.WriteInt32(buffer, 4, 18);
        bool untrusted=!air && UsbNeedsTrust;
        Marshal.WriteInt32(buffer, 8, air ? 2 : 1); Marshal.WriteInt32(buffer, 16, untrusted ? 2 : 4);
        Marshal.WriteInt32(buffer, 20, air ? 0 : 1); Marshal.WriteInt32(buffer, 24, untrusted ? 0 : 1);
        Marshal.WriteInt32(buffer, 28, untrusted ? 0 : 1);
        WriteText(buffer, 32, id);
        WriteText(buffer, 288, name);
        WriteText(buffer, 544, "iPad14,3"); WriteText(buffer, 672, "18.7");
        WriteText(buffer, 736, air ? "AirPlay" : "USB");
        WriteText(buffer, 800, untrusted ? "Waiting for this computer to be trusted" : "Ready");
    }
    public static int StartAir(IntPtr name, IntPtr host, uint width, uint height, uint fps) {
        ReceiverName = Marshal.PtrToStringUni(name); ReceiverHost=Marshal.PtrToStringUni(host);
        ReceiverLibraryDirectory=Environment.GetEnvironmentVariable("WEIMIRROR_720_RECEIVER_DIRECTORY");
        ReceiverWidth = (int)width;
        ReceiverHeight = (int)height; ReceiverFps = (int)fps;
        AirStarted = true; return Trace("air-start");
    }
    public static int AirStatus(IntPtr running, IntPtr ready) {
        Marshal.WriteInt32(running, AirStarted ? 1 : 0); Marshal.WriteInt32(ready, AirStarted ? 1 : 0); return 0;
    }
    public static void StopAir() { AirStarted = false; Trace("air-stop"); }
    public static int UsbSession(IntPtr id, IntPtr options, IntPtr handle) { return Session(id, options, handle, false); }
    public static int AirSession(IntPtr id, IntPtr options, IntPtr handle) { return Session(id, options, handle, true); }
    static int Session(IntPtr id, IntPtr options, IntPtr handle, bool air) {
        string name = Marshal.PtrToStringUni(id);
        if (air) {
            if (!AirStarted || (name == AirId ? !ShowAirDevice : name != SecondAirId || !ShowSecondAirDevice)) return -6;
            if (name == AirId && RefuseFirstAirDevice) { FailedAirStarts++; return -6; }
        } else {
            Interlocked.Increment(ref UsbAttempts);
            if (name == ForeignUsbId) Interlocked.Increment(ref ForeignUsbAttempts);
            if ((name == UsbId ? !ShowUsbDevice : name != ForeignUsbId || !ShowForeignUsb) || RefuseUsbSession)
                return -6;
        }
        if (Marshal.ReadInt32(options) != 48 || Marshal.ReadInt32(options, 4) != 18) return -1;
        ActiveId = name;
        SessionFps=Marshal.ReadInt32(options,16);
        Audio = Marshal.ReadInt32(options, 20) != 0; Streaming = true; Allocated = true;
        Marshal.WriteInt64(handle, 12345678909L); return Trace(air ? "air-session" : "usb-session");
    }
    const ulong H = 12345678909UL;
    public static int Status(ulong handle, IntPtr p) {
        if (!Allocated || handle != H || Marshal.ReadInt32(p, 0) != 464) return -1;
        Marshal.WriteInt32(p, 4, 18);
        bool gone = (ActiveId == AirId && !ShowAirDevice) || (ActiveId == SecondAirId && !ShowSecondAirDevice);
        bool missingUsb=(ActiveId==UsbId && !ShowUsbDevice) || (ActiveId==ForeignUsbId && !ShowForeignUsb);
        bool waiting = WaitingForFrames || gone || missingUsb;
        Marshal.WriteInt32(p, 8, !Streaming || (missingUsb && UsbLossIsTerminal) ? 6 : waiting ? 2 : 4);
        Marshal.WriteInt32(p, 12, waiting ? 0 : VideoWidth); Marshal.WriteInt32(p, 16, waiting ? 0 : VideoHeight);
        Marshal.WriteInt64(p, 24, BitConverter.DoubleToInt64Bits(waiting ? 0 : 59.5)); Marshal.WriteInt64(p, 32, BitConverter.DoubleToInt64Bits(8.25));
        Marshal.WriteInt64(p, 40, 9876543210L); Marshal.WriteInt64(p, 48, 1234); Marshal.WriteInt32(p, 56, 48000);
        Marshal.WriteInt32(p, 60, 2); WriteText(p, 76, FrameProtected ? "DRM_VIDEO_PROTECTED_AUDIO_ACTIVE" : "测试画面"); return 0;
    }
    public static int CopyFrame(ulong handle, IntPtr info, IntPtr pixels, IntPtr length, uint maxWidth, uint maxHeight) {
        if (!Allocated || !Streaming || handle!=H || WaitingForFrames || maxWidth!=0 || maxHeight!=0 ||
            Marshal.ReadInt32(info)!=32 || Marshal.ReadInt32(info,4)!=18) return -1;
        if (GrowOnCopy && pixels!=IntPtr.Zero) { GrowOnCopy=false; grown=true; }
        int width = grown ? 4 : 3;
        // Extra pitch bytes make misinterpreted BGRA / inter-row stride visible in decoded PNGs.
        int pitch = width*4 + 4, bytes = pitch*2;
        uint capacity = (uint)Marshal.ReadInt32(length);
        Marshal.WriteInt32(length,bytes); Marshal.WriteInt32(info,8,width); Marshal.WriteInt32(info,12,2);
        Marshal.WriteInt32(info,16,pitch); Marshal.WriteInt32(info,20,BadFrameFormat ? 99 : 1); Marshal.WriteInt64(info,24,991);
        if (pixels==IntPtr.Zero || capacity<bytes) return -3;
        EnteredCopy.Set();
        if (!ContinueCopy.Wait(TimeSpan.FromSeconds(8))) return -7;
        byte[] image = new byte[bytes];
        for (int y=0;y<2;y++) for (int x=0;x<width;x++) {
            int offset=y*pitch+x*4;
            image[offset]=(byte)(40+x); image[offset+1]=(byte)(60+y); image[offset+2]=(byte)(80+x+y);
            image[offset+3]=255;
        }
        Marshal.Copy(image,0,pixels,bytes);
        if (ProtectOnCopy) FrameProtected=true;
        FrameCopies++;
        return 0;
    }
    public static int EnableAudio(ulong handle, int enabled) { if (!Allocated || handle != H) return -1; Audio = enabled != 0; return Trace("audio:" + enabled); }
    public static int Attach(ulong handle, IntPtr hwnd) {
        EnteredAttach.Set();
        if (!ContinueAttach.Wait(TimeSpan.FromSeconds(8))) return -7;
        if (!Allocated || handle != H || !IsWindow(hwnd) || AttachFails) return -7;
        Attached = hwnd; Trace("attach"); return Refresh(handle);
    }
    public static void Detach(ulong handle, IntPtr hwnd) { if (handle == H && hwnd == Attached) { Attached = IntPtr.Zero; Trace("detach"); } }
    public static int Stop(ulong handle) { if (handle != H) return -1; Streaming = false; return Trace("stop"); }
    public static void Destroy(ulong handle) {
        if (handle == H) {
            LastDestroyHadLivePreview = IsWindow(Attached);
            Allocated = false; ActiveId = null; Trace("destroy");
        }
    }
    public static int Refresh(ulong handle) {
        if (handle != H || Attached == IntPtr.Zero) return -7;
        IntPtr dc = GetDC(Attached);
        try {
            RECT bounds; GetClientRect(Attached, out bounds);
            IntPtr brush = CreateSolidBrush((uint)(FrameSerial % 2 == 0 ? 0x00FF5544 : 0x0033CC77));
            try { FillRect(dc, ref bounds, brush); GdiFlush(); }
            finally { DeleteObject(brush); }
            FrameSerial++;
        } finally { ReleaseDC(Attached, dc); }
        return Trace("frame");
    }
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr window, out RECT rect);
    [DllImport("gdi32.dll")] static extern IntPtr CreateSolidBrush(uint color);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr handle);
    [DllImport("gdi32.dll")] static extern bool GdiFlush();
    [DllImport("user32.dll")] static extern int FillRect(IntPtr dc, ref RECT rect, IntPtr brush);
}
