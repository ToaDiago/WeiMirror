// SPDX-License-Identifier: GPL-3.0-only
// Managed interop for iPhoneMirror Core ABI 18. See NOTICE.txt for its source.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace MirrorDesk
{
    internal sealed partial class Device
    {
        public string Id { get; private set; }
        public string Name { get; private set; }
        public bool Wireless { get; private set; }

        internal Device(string id, string name, bool wireless)
        {
            Id = id;
            Name = name;
            Wireless = wireless;
        }

        public override string ToString() { return Name; }
    }

    internal sealed partial class StreamStatus
    {
        public int State { get; internal set; }
        public double Fps { get; internal set; }
        public uint Width { get; internal set; }
        public uint Height { get; internal set; }
        public ulong Frames { get; internal set; }
        public string Message { get; internal set; }
        // ABI 18 reports this per snapshot. Prolonged dark content can also
        // trigger the detector, so the UI should present it as a possibility.
        public bool ProtectedAudioActive
        {
            get { return Message == "DRM_VIDEO_PROTECTED_AUDIO_ACTIVE"; }
        }
        public bool ProtectedVideo
        {
            get { return ProtectedAudioActive || Message == "DRM_VIDEO_PROTECTED_AUDIO_INACTIVE" || Message == "DRM_VIDEO_PROTECTED"; }
        }
    }

    internal sealed partial class MirrorCore : IDisposable
    {
        private const string CoreLibrary = "iPhoneMirror.Core.dll";
        private const uint SupportedVersion = 18;
        private const CallingConvention Convention = CallingConvention.Cdecl;
        private static readonly object processGate = new object();
        private static bool coreInUse;
        private readonly object gate = new object();
        private readonly string engineDirectory;
        private IntPtr module;
        private ulong session;
        private Device currentDevice;
        private IntPtr preview;
        private bool open;

        // These value types mirror CoreApi.h. Windows wchar_t is UTF-16 and
        // uint64_t / double retain the default eight-byte structure alignment.
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DeviceInfo
        {
            public uint Size, Version, DeviceId, MuxPort;
            public int State, UsbConnected, Paired, LockdownAccessible;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Id;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Name;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Model;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string OsVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Transport;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 192)] public string Description;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CaptureOptions
        {
            public uint Size, Version, Width, Height, TargetFps;
            public int PlayAudio;
            public float AudioVolume;
            public uint AdvancedUsbWidth, AdvancedUsbHeight, UsbMode, DecoderPreference, ColorPreference;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct CaptureStatus
        {
            public uint Size, Version;
            public int State;
            public uint Width, Height;
            public double Fps, ReceiverLatency;
            public ulong Frames, AudioPackets;
            public uint SampleRate, Channels;
            public int FailureKind, FailureStage, ErrorCode;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 192)] public string Message;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
        private static extern IntPtr LoadLibraryExW(string path, IntPtr reserved, uint flags);
        [DllImport("kernel32.dll", ExactSpelling = true)]
        private static extern bool FreeLibrary(IntPtr handle);

        [DllImport(CoreLibrary, ExactSpelling = true, CallingConvention = Convention)]
        private static extern uint im_api_version();
        [DllImport(CoreLibrary, ExactSpelling = true, CallingConvention = Convention)]
        private static extern int im_initialize();
        [DllImport(CoreLibrary, ExactSpelling = true, CallingConvention = Convention)]
        private static extern void im_shutdown();
        [DllImport(CoreLibrary, ExactSpelling = true, CallingConvention = Convention)]
        private static extern IntPtr im_last_error();
        [DllImport(CoreLibrary, ExactSpelling = true, CallingConvention = Convention)]
        private static extern int im_refresh_devices_ex(IntPtr buffer, ref uint count, int refresh);
        [DllImport(CoreLibrary, ExactSpelling = true, CallingConvention = Convention)]
        private static extern int im_refresh_wireless_devices(IntPtr buffer, ref uint count);
        [DllImport(CoreLibrary, ExactSpelling = true, CallingConvention = Convention, CharSet = CharSet.Unicode)]
        private static extern int im_session_create(string id, ref CaptureOptions options, out ulong handle);
        [DllImport(CoreLibrary, ExactSpelling = true, CallingConvention = Convention, CharSet = CharSet.Unicode)]
        private static extern int im_wireless_session_create(string id, ref CaptureOptions options, out ulong handle);
        [DllImport(CoreLibrary, ExactSpelling = true, CallingConvention = Convention)]
        private static extern int im_session_get_status(ulong handle, ref CaptureStatus status);
        [DllImport(CoreLibrary, ExactSpelling = true, CallingConvention = Convention)]
        private static extern int im_session_attach_preview(ulong handle, IntPtr hwnd);
        [DllImport(CoreLibrary, ExactSpelling = true, CallingConvention = Convention)]
        private static extern void im_session_detach_preview(ulong handle, IntPtr hwnd);
        [DllImport(CoreLibrary, ExactSpelling = true, CallingConvention = Convention)]
        private static extern int im_session_force_preview_refresh(ulong handle);
        [DllImport(CoreLibrary, ExactSpelling = true, CallingConvention = Convention)]
        private static extern int im_session_set_audio_enabled(ulong handle, int enabled);
        [DllImport(CoreLibrary, ExactSpelling = true, CallingConvention = Convention)]
        private static extern int im_session_stop(ulong handle);
        [DllImport(CoreLibrary, ExactSpelling = true, CallingConvention = Convention)]
        private static extern void im_session_destroy(ulong handle);

        public MirrorCore(string engineDirectory)
        {
            this.engineDirectory = Path.GetFullPath(engineDirectory);
            if (!Environment.Is64BitProcess)
                throw new PlatformNotSupportedException("接收引擎需要 64 位 Windows 进程。");
            lock (processGate)
            {
                if (coreInUse) throw new InvalidOperationException("此进程已有接收引擎。");
                string corePath = Path.Combine(this.engineDirectory, CoreLibrary);
                if (!File.Exists(corePath)) throw new FileNotFoundException("缺少接收核心。", corePath);
                // LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32.
                module = LoadLibraryExW(corePath, IntPtr.Zero, 0x100 | 0x800);
                if (module == IntPtr.Zero)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "无法载入接收核心或其运行组件。");
                try
                {
                    uint version = im_api_version();
                    if (version != SupportedVersion)
                        throw new NotSupportedException("接收核心接口版本不匹配。需要 18，实际为 " + version + "。");
                    Check(im_initialize(), "启动接收核心");
                    coreInUse = open = true;
                }
                catch
                {
                    FreeLibrary(module);
                    module = IntPtr.Zero;
                    throw;
                }
            }
        }

        private void EnsureOpen()
        {
            if (!open) throw new ObjectDisposedException("MirrorCore");
        }

        private static void Check(int result, string action)
        {
            if (result == 0) return;
            var detail = Marshal.PtrToStringUni(im_last_error());
            throw new InvalidOperationException(action + "失败（" + result + "）" +
                (String.IsNullOrWhiteSpace(detail) ? "。" : "：" + detail));
        }

        public bool HasSession { get { lock (gate) return session != 0; } }

        public Device[] GetUsbDevices(bool refreshMetadata)
        {
            return Enumerate(false, refreshMetadata);
        }

        public Device[] GetWirelessDevices()
        {
            return Enumerate(true, false);
        }

        private static int ReadDevices(bool wireless, bool metadata, IntPtr buffer, ref uint count)
        {
            return wireless ? im_refresh_wireless_devices(buffer, ref count) :
                im_refresh_devices_ex(buffer, ref count, metadata ? 1 : 0);
        }

        private Device[] Enumerate(bool wireless, bool metadata)
        {
            lock (gate)
            {
                EnsureOpen();
                var stride = Marshal.SizeOf(typeof(DeviceInfo));
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    uint capacity = 0;
                    Check(ReadDevices(wireless, metadata, IntPtr.Zero, ref capacity), "读取设备列表");
                    if (capacity == 0) return new Device[0];
                    IntPtr buffer = Marshal.AllocHGlobal(checked((int)capacity * stride));
                    try
                    {
                        uint count = capacity;
                        int result = ReadDevices(wireless, metadata, buffer, ref count);
                        if (result == -3) continue; // A device joined between the two calls.
                        Check(result, "读取设备列表");
                        if (count > capacity) throw new InvalidDataException("接收引擎返回的设备数量超出缓冲区。");
                        var devices = new List<Device>((int)count);
                        for (int i = 0; i < count; i++)
                        {
                            var item = (DeviceInfo)Marshal.PtrToStructure(IntPtr.Add(buffer, i * stride), typeof(DeviceInfo));
                            if (String.IsNullOrWhiteSpace(item.Id)) continue;
                            string name = !String.IsNullOrWhiteSpace(item.Name) ? item.Name : item.Model;
                            if (String.IsNullOrWhiteSpace(name)) name = wireless ? "AirPlay 设备" : "Apple USB 设备";
                            devices.Add(new Device(item.Id, name, wireless) {
                                ConnectionState=item.State, PairRecord=item.Paired!=0,
                                DeviceInfoReadable=item.LockdownAccessible!=0, ConnectionDetail=item.Description
                            });
                        }
                        return devices.ToArray();
                    }
                    finally { Marshal.FreeHGlobal(buffer); }
                }
                throw new InvalidOperationException("设备列表正在变化，请重新刷新。");
            }
        }

        public void Start(Device device, IntPtr previewHwnd, bool audio)
        {
            if (device == null || String.IsNullOrWhiteSpace(device.Id)) throw new ArgumentException("请选择设备。");
            if (previewHwnd == IntPtr.Zero) throw new ArgumentException("预览窗口尚未建立。");
            lock (gate)
            {
                EnsureOpen();
                if (session != 0) StopLocked();
                var options = new CaptureOptions
                {
                    Size = (uint)Marshal.SizeOf(typeof(CaptureOptions)), Version = SupportedVersion,
                    TargetFps = device.Wireless && wirelessFps == 120 ? 120u : 60u, PlayAudio = audio ? 1 : 0, AudioVolume = 1.0f,
                    DecoderPreference = device.Wireless ? 0u : 1u
                };
                // USB creation performs an exact-device preflight inside the selected backend.
                ulong handle;
                int result = device.Wireless ? im_wireless_session_create(device.Id, ref options, out handle) :
                    im_session_create(device.Id, ref options, out handle);
                Check(result, device.Wireless ? "连接 AirPlay 设备" : "连接 USB 设备");
                if (handle == 0) throw new InvalidDataException("接收核心未返回投屏会话。");
                try
                {
                    Check(im_session_attach_preview(handle, previewHwnd), "创建 Direct3D 预览");
                    session = handle;
                    preview = previewHwnd;
                    currentDevice = device;
                }
                catch
                {
                    // destroy() also stops capture and restores USB if attachment fails.
                    im_session_destroy(handle);
                    throw;
                }
            }
        }

        public void ReattachPreview(IntPtr newHwnd)
        {
            lock (gate)
            {
                EnsureOpen();
                if (session == 0 || newHwnd == preview) return;
                if (newHwnd != IntPtr.Zero) Check(im_session_attach_preview(session, newHwnd), "切换预览窗口");
                if (preview != IntPtr.Zero) im_session_detach_preview(session, preview);
                preview = newHwnd;
            }
        }

        public void RefreshPreview()
        {
            lock (gate)
            {
                EnsureOpen();
                if (session != 0 && preview != IntPtr.Zero)
                    Check(im_session_force_preview_refresh(session), "刷新预览");
            }
        }

        public void SetAudio(bool enabled)
        {
            lock (gate)
            {
                EnsureOpen();
                if (session != 0) Check(im_session_set_audio_enabled(session, enabled ? 1 : 0), "更改电脑声音");
            }
        }

        public StreamStatus GetStatus()
        {
            lock (gate)
            {
                EnsureOpen();
                if (session == 0) return new StreamStatus { Message = String.Empty };
                var status = new CaptureStatus { Size = (uint)Marshal.SizeOf(typeof(CaptureStatus)), Message = String.Empty };
                Check(im_session_get_status(session, ref status), "读取投屏状态");
                return new StreamStatus { State = status.State, Width = status.Width, Height = status.Height,
                    Fps = status.Fps, Frames = status.Frames, Message = status.Message ?? String.Empty,
                    FailureKind=status.FailureKind, FailureStage=status.FailureStage };
            }
        }

        public void Stop()
        {
            lock (gate) { EnsureOpen(); StopLocked(); }
        }

        private void StopLocked()
        {
            ulong handle = session;
            if (handle == 0) return;
            try { Check(im_session_stop(handle), "结束投屏"); }
            finally
            {
                session = 0;
                preview = IntPtr.Zero;
                currentDevice = null;
                im_session_destroy(handle);
            }
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (!open) return;
                try { StopLocked(); }
                finally
                {
                    lock (processGate)
                    {
                        try { im_shutdown(); }
                        finally
                        {
                            open = coreInUse = false;
                            FreeLibrary(module);
                            module = IntPtr.Zero;
                        }
                    }
                }
            }
        }
    }
}
