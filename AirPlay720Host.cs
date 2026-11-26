// SPDX-License-Identifier: GPL-3.0-only
// AirPlayServer v1.1.2 SDK ABI as packaged with iPhoneMirror v1.8.3.
// This process publishes only mirroring/audio and retains no frames after the pipe closes.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace MirrorDesk
{
    internal static class AirPlay720Host
    {
        const string StartSymbol = "?fgServerStart@@YAPEAXQEBDIIPEAVIAirServerCallback@@@Z";
        const string StopSymbol = "?fgServerStop@@YAXPEAX@Z";
        const string CapabilityPrefix = "IPHONE_MIRROR_AIRPLAY_";

        [DllImport("kernel32.dll", SetLastError=true)]
        static extern bool SetDefaultDllDirectories(uint flags);
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true, ExactSpelling=true)]
        static extern IntPtr AddDllDirectory(string directory);
        [DllImport("kernel32.dll", ExactSpelling=true)]
        static extern bool RemoveDllDirectory(IntPtr directory);
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true, ExactSpelling=true)]
        static extern IntPtr LoadLibraryExW(string path, IntPtr file, uint flags);
        [DllImport("kernel32.dll", CharSet=CharSet.Ansi, ExactSpelling=true)]
        static extern IntPtr GetProcAddress(IntPtr module, string procedure);
        [DllImport("kernel32.dll", ExactSpelling=true)]
        static extern bool FreeLibrary(IntPtr module);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate IntPtr StartServer(IntPtr utf8ReceiverName,uint raopPort,uint airplayPort,IntPtr callback);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void StopServer(IntPtr server);

        internal static int Run(string[] args)
        {
            try {
                var directory=Environment.GetEnvironmentVariable(MirrorCore.AirPlay720Directory);
                if (String.IsNullOrEmpty(directory) || !Path.IsPathRooted(directory)) return 4;
                var full=Path.GetFullPath(directory);
                RuntimePack.VerifyWirelessDirectory(full, CancellationToken.None);
                return RunWithLibrary(args,Path.Combine(full,"airplay2dll.dll"));
            } catch (Exception) { return 4; }
        }

        // The process entry point supplies only the pinned and verified vendor library.
        // The regression harness uses this same path with an isolated SDK test double.
        internal static int RunWithLibrary(string[] args,string libraryPath)
        {
            string pipe, stop, name;
            int pid;
            if (!TryReadArguments(args,out pipe,out stop,out name,out pid)) return 2;
            try {
                using (var parent=Process.GetProcessById(pid))
                using (var stopEvent=EventWaitHandle.OpenExisting(stop,
                    System.Security.AccessControl.EventWaitHandleRights.Synchronize))
                using (var output=AirPlayHostPipe.Connect(pipe)) {
                    if (parent.HasExited || stopEvent.WaitOne(0)) return 0;
                    string selection=Environment.GetEnvironmentVariable(MirrorCore.AirPlayProfileIndex);
                    int index=3;
                    if (selection!=null && (!Int32.TryParse(selection,NumberStyles.None,CultureInfo.InvariantCulture,out index) ||
                        index>=WirelessProfile.Choices.Length || !WirelessProfile.Choices[index].UsesCustomHost)) return 2;
                    ConfigureReceiver(name,WirelessProfile.Choices[index]);
                    return Serve(libraryPath,name,output,stopEvent,parent);
                }
            } catch (Exception) { return 3; }
        }

        static bool TryReadArguments(string[] args,out string pipe,out string stop,out string name,out int pid)
        {
            pipe=stop=name=null; pid=0;
            if (args==null || args.Length!=20) return false;
            var values=new Dictionary<string,string>(StringComparer.Ordinal);
            for (int i=0;i<args.Length;i+=2) {
                if (args[i]==null || args[i+1]==null || values.ContainsKey(args[i])) return false;
                values.Add(args[i],args[i+1]);
            }
            string parent,mode,width,height,fps,raop,airplay;
            if (!values.TryGetValue("--pipe",out pipe) || !values.TryGetValue("--stop-event",out stop) ||
                !values.TryGetValue("--name",out name) || !values.TryGetValue("--parent-pid",out parent) ||
                !values.TryGetValue("--width",out width) || !values.TryGetValue("--height",out height) ||
                !values.TryGetValue("--fps",out fps) || !values.TryGetValue("--mode",out mode) ||
                !values.TryGetValue("--raop-port",out raop) || !values.TryGetValue("--airplay-port",out airplay) ||
                width!="1280" || height!="720" || fps!="30" || mode!="combined" || raop!="5001" || airplay!="7001" ||
                !Int32.TryParse(parent,NumberStyles.None,CultureInfo.InvariantCulture,out pid) || pid<=0) return false;
            const string prefix=@"\\.\pipe\iPhoneMirror-AirPlay-Hub-";
            string expected=prefix+parent+"-";
            Guid nonce;
            if (!pipe.StartsWith(expected,StringComparison.Ordinal) ||
                !Guid.TryParseExact(pipe.Substring(expected.Length),"B",out nonce) ||
                !String.Equals(stop,@"Local\iPhoneMirror-AirPlay-Hub-Stop-"+pipe.Substring(prefix.Length),StringComparison.Ordinal))
                return false;
            try { return !String.IsNullOrWhiteSpace(name) && MirrorCore.ValidateReceiverName(name)==name; }
            catch (ArgumentException) { return false; }
        }

        static void ConfigureReceiver(string receiverName,WirelessProfile profile)
        {
            // Same stable identity and key seed as the stock host, so switching profile
            // retains pairing. Width, height, maxFPS and refreshRate go into the /info plist.
            ulong hash=1469598103934665603UL;
            foreach (var c in Environment.MachineName+"airplay-compat-v3")
                hash=unchecked((hash^(byte)c)*1099511628211UL);
            var address=new StringBuilder("02");
            for (int i=0;i<5;i++) address.Append(":").Append(((byte)(hash>>(8*i))).ToString("X2",CultureInfo.InvariantCulture));
            string deviceId=address.ToString(), seed;
            using (var sha=SHA256.Create())
                seed=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(deviceId+"iPhoneMirror AirPlay pairing identity v1")))
                    .Replace("-","").ToLowerInvariant();
            Environment.SetEnvironmentVariable(CapabilityPrefix+"WIDTH",profile.Width.ToString(CultureInfo.InvariantCulture));
            Environment.SetEnvironmentVariable(CapabilityPrefix+"HEIGHT",profile.Height.ToString(CultureInfo.InvariantCulture));
            Environment.SetEnvironmentVariable(CapabilityPrefix+"FPS",profile.Fps.ToString(CultureInfo.InvariantCulture));
            Environment.SetEnvironmentVariable(CapabilityPrefix+"MODE","mirror");
            Environment.SetEnvironmentVariable(CapabilityPrefix+"NAME",receiverName);
            Environment.SetEnvironmentVariable(CapabilityPrefix+"DEVICE_ID",deviceId);
            Environment.SetEnvironmentVariable(CapabilityPrefix+"PAIRING_SEED",seed);
            // Start must produce a public key for this invocation.
            Environment.SetEnvironmentVariable(CapabilityPrefix+"PUBLIC_KEY",null);
        }

        static int Serve(string libraryPath,string name,AirPlayHostPipe output,EventWaitHandle stopEvent,Process parent)
        {
            const uint system32=0x800, userDirectories=0x400, libraryDirectory=0x100;
            if (!Path.IsPathRooted(libraryPath) || !SetDefaultDllDirectories(system32|userDirectories)) return 4;
            string full=Path.GetFullPath(libraryPath);
            IntPtr cookie=AddDllDirectory(Path.GetDirectoryName(full));
            if (cookie==IntPtr.Zero) return 4;
            IntPtr module=IntPtr.Zero;
            try {
                module=LoadLibraryExW(full,IntPtr.Zero,libraryDirectory|system32|userDirectories);
                if (module==IntPtr.Zero) return 4;
                IntPtr startAddress=GetProcAddress(module,StartSymbol), stopAddress=GetProcAddress(module,StopSymbol);
                if (startAddress==IntPtr.Zero || stopAddress==IntPtr.Zero) return 5;
                var start=Marshal.GetDelegateForFunctionPointer<StartServer>(startAddress);
                var stop=Marshal.GetDelegateForFunctionPointer<StopServer>(stopAddress);
                using (var callbacks=new SdkCallbacks(output)) {
                    var nameBytes=Encoding.UTF8.GetBytes(name+"\0");
                    var pinned=GCHandle.Alloc(nameBytes,GCHandleType.Pinned);
                    try {
                        var server=start(pinned.AddrOfPinnedObject(),5001,7001,callbacks.Object);
                        if (server==IntPtr.Zero) return 6;
                        try {
                            string key=Environment.GetEnvironmentVariable(CapabilityPrefix+"PUBLIC_KEY");
                            if (key==null || key.Length!=64 || !Array.TrueForAll(key.ToCharArray(),Uri.IsHexDigit)) return 8;
                            output.Ready();
                            while (!stopEvent.WaitOne(120) && !parent.HasExited && !output.Broken) {}
                        } finally { stop(server); }
                    } finally { pinned.Free(); }
                }
                return 0;
            } finally {
                if(module!=IntPtr.Zero) FreeLibrary(module);
                RemoveDllDirectory(cookie);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        struct VideoFrame
        {
            public ulong Pts;
            public int Key, Width, Height, YPitch, UPitch, VPitch, YLen, ULen, VLen, Total;
            public IntPtr Data;
        }
        [StructLayout(LayoutKind.Sequential)]
        struct AudioFrame
        {
            public ulong Pts;
            public int SampleRate;
            public ushort Channels,Bits;
            public int Length;
            public IntPtr Data;
        }

        // The SDK retains a C++ IAirServerCallback* (eight x64 virtual methods).
        // Keep delegates, vtable and object valid until fgServerStop returns.
        sealed class SdkCallbacks : IDisposable
        {
            [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void DeviceFn(IntPtr self,IntPtr name,IntPtr id);
            [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void FrameFn(IntPtr self,IntPtr frame,IntPtr name,IntPtr id);
            [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void MediaFn(IntPtr self,IntPtr url,double volume,double start);
            [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void PlayInfoFn(IntPtr self,IntPtr duration,IntPtr position,IntPtr rate);
            [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void VolumeFn(IntPtr self,float volume,IntPtr name,IntPtr id);
            [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void LogFn(IntPtr self,int level,IntPtr message);
            readonly AirPlayHostPipe output;
            readonly Delegate[] slots;
            IntPtr table;
            public IntPtr Object { get; private set; }

            internal SdkCallbacks(AirPlayHostPipe pipe)
            {
                output=pipe;
                slots=new Delegate[] {new DeviceFn(Connected),new DeviceFn(Disconnected),new FrameFn(Audio),new FrameFn(Video),
                    new MediaFn(IgnoreMedia),new PlayInfoFn(NoPlayback),new VolumeFn(Volume),new LogFn(IgnoreLog)};
                try {
                    table=Marshal.AllocHGlobal(IntPtr.Size*slots.Length);
                    for (int i=0;i<slots.Length;i++)
                        Marshal.WriteIntPtr(table,i*IntPtr.Size,Marshal.GetFunctionPointerForDelegate(slots[i]));
                    Object=Marshal.AllocHGlobal(IntPtr.Size);
                    Marshal.WriteIntPtr(Object,table);
                } catch { Dispose(); throw; }
            }

            // Never unwind a managed exception into the native decoder.
            void Connected(IntPtr self,IntPtr name,IntPtr id) { try { output.Connected(name,id); } catch(Exception) {} }
            void Disconnected(IntPtr self,IntPtr name,IntPtr id) { try { output.Disconnected(name,id); } catch(Exception) {} }
            void Volume(IntPtr self,float db,IntPtr name,IntPtr id) {
                try { if (!Single.IsNaN(db) && !Single.IsInfinity(db)) output.Volume(name,id,db); } catch(Exception) {}
            }
            void Audio(IntPtr self,IntPtr frame,IntPtr name,IntPtr id)
            {
                if (frame==IntPtr.Zero) return;
                try {
                    var f=Marshal.PtrToStructure<AudioFrame>(frame);
                    if (f.Data==IntPtr.Zero || f.Length<1 || f.Length>64*1024*1024) return;
                    output.Audio(name,id,f.SampleRate,f.Channels,f.Bits,f.Data,f.Length);
                } catch(Exception) {}
            }
            void Video(IntPtr self,IntPtr frame,IntPtr name,IntPtr id)
            {
                if (frame==IntPtr.Zero) return;
                try {
                    var f=Marshal.PtrToStructure<VideoFrame>(frame);
                    if (f.Data==IntPtr.Zero || f.Width<1 || f.Height<1 || f.Width>8192 || f.Height>8192 ||
                        f.Total<1 || f.Total>64*1024*1024 || f.YPitch<f.Width || f.UPitch<(f.Width+1)/2 ||
                        f.VPitch<(f.Width+1)/2 || f.YLen<(long)f.YPitch*f.Height ||
                        f.ULen<(long)f.UPitch*((f.Height+1)/2) || f.VLen<(long)f.VPitch*((f.Height+1)/2) ||
                        (long)f.YLen+f.ULen+f.VLen>f.Total) return;
                    output.Video(name,id,f.Width,f.Height,f.YPitch,f.UPitch,f.VPitch,f.YLen,f.ULen,f.VLen,f.Data,f.Total);
                } catch(Exception) {}
            }
            static void IgnoreMedia(IntPtr self,IntPtr url,double volume,double start) {}
            static void IgnoreLog(IntPtr self,int level,IntPtr message) {}
            static void NoPlayback(IntPtr self,IntPtr duration,IntPtr position,IntPtr rate)
            {
                try {
                    if (duration!=IntPtr.Zero) Marshal.WriteInt64(duration,0);
                    if (position!=IntPtr.Zero) Marshal.WriteInt64(position,0);
                    if (rate!=IntPtr.Zero) Marshal.WriteInt64(rate,0);
                } catch(Exception) {}
            }
            public void Dispose()
            {
                if (Object!=IntPtr.Zero) { Marshal.FreeHGlobal(Object); Object=IntPtr.Zero; }
                if (table!=IntPtr.Zero) { Marshal.FreeHGlobal(table); table=IntPtr.Zero; }
                GC.KeepAlive(slots);
            }
        }
    }
}
