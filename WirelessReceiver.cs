// SPDX-License-Identifier: GPL-3.0-only
// MirrorDesk's AirPlay adapter. The native host bridge follows the public
// iPhoneMirror v1.8.3 CoreApi.h ABI. The process-global hub owns the pipe and
// feeds video and audio to the same capture session used by the USB preview.
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace MirrorDesk
{
    internal enum WirelessState { Off, Starting, Ready, Failed }

    internal sealed class WirelessProfile
    {
        // The first three positions are persisted by 1.2 and sent to its worker process.
        internal static readonly WirelessProfile[] Choices = {
            new WirelessProfile(1920,1080,60), new WirelessProfile(1280,720,30),
            new WirelessProfile(960,540,30), new WirelessProfile(1280,720,60)
        };
        internal readonly int Width, Height, Fps;
        WirelessProfile(int width,int height,int fps) { Width=width; Height=height; Fps=fps; }
        internal bool Uses720Host { get { return Width==1280 && Height==720 && Fps==60; } }
        public override string ToString()
        {
            return Height + "p · " + Fps + " fps" + (Uses720Host ? "（试验）" : "");
        }
    }

    internal sealed partial class MirrorCore
    {
        private const string HostFile = "iPhoneMirror.WirelessHost.exe";
        internal const string AirPlay720Directory = "WEIMIRROR_720_RECEIVER_DIRECTORY";
        private static readonly string[] WirelessRuntimeFiles = {
            "airplay2dll.dll", "avcodec-58.dll", "avutil-56.dll",
            "dnssd.dll", "swresample-3.dll", "swscale-5.dll"
        };
        private readonly Stopwatch wirelessStartup = new Stopwatch();
        private bool wirelessRuntimeProbed;
        private string wirelessName;
        private int wirelessWidth, wirelessHeight, wirelessFps;

        [DllImport(CoreLibrary, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int im_wireless_receiver_start_ex(string receiverName,
            string hostPath, uint width, uint height, uint frameRate);

        [DllImport(CoreLibrary, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern int im_wireless_receiver_get_status(out int running, out int ready);

        [DllImport(CoreLibrary, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern void im_wireless_receiver_stop();

        public void StartWireless(string receiverName)
        {
            StartWireless(receiverName, 1920, 1080, 60);
        }

        public void StartWireless(string receiverName, int width, int height, int fps)
        {
            string name = ValidateReceiverName(receiverName);
            if (!IsWirelessProfile(width, height, fps))
                throw new ArgumentException("请选择支持的 AirPlay 分辨率和帧率。");

            lock (gate)
            {
                EnsureOpen();
                WirelessState previous = ReadWirelessState();
                bool sameSettings = string.Equals(wirelessName, name, StringComparison.Ordinal)
                    && wirelessWidth == width && wirelessHeight == height && wirelessFps == fps;
                if (sameSettings && (previous == WirelessState.Starting || previous == WirelessState.Ready))
                    return;

                string directory = Path.Combine(engineDirectory, "Wireless");
                string host = Path.Combine(directory, HostFile);
                if (!wirelessRuntimeProbed)
                {
                    CheckWirelessRuntime(directory, host);
                    wirelessRuntimeProbed = true;
                }
                if (previous != WirelessState.Off) StopWirelessLocked();

                if (width == 1280 && height == 720 && fps == 60) {
                    // Core v1.8.3 uses this capability only to build the child's command line.
                    // Its 720p carrier must be 30. Our dedicated host offers 60 to the real SDK,
                    // and the separately created capture session consumes up to 60 fps.
                    string inherited = Environment.GetEnvironmentVariable(AirPlay720Directory);
                    try {
                        Environment.SetEnvironmentVariable(AirPlay720Directory, directory);
                        Check(im_wireless_receiver_start_ex(name, typeof(MirrorCore).Assembly.Location,
                            1280,720,30), "启动 720p60 AirPlay 接收");
                    } finally { Environment.SetEnvironmentVariable(AirPlay720Directory, inherited); }
                } else {
                    Check(im_wireless_receiver_start_ex(name, host, (uint)width, (uint)height,
                        (uint)fps), "启动 AirPlay 接收");
                }
                wirelessName = name;
                wirelessWidth = width;
                wirelessHeight = height;
                wirelessFps = fps;
                wirelessStartup.Restart();
            }
        }

        public void StopWireless()
        {
            lock (gate)
            {
                EnsureOpen();
                StopWirelessLocked();
            }
        }

        private void StopWirelessLocked()
        {
            try
            {
                if (currentDevice != null && currentDevice.Wireless) StopLocked();
            }
            finally
            {
                im_wireless_receiver_stop();
                wirelessName = null;
                wirelessStartup.Reset();
            }
        }

        public WirelessState GetWirelessState()
        {
            lock (gate)
            {
                EnsureOpen();
                return ReadWirelessState();
            }
        }

        public bool WirelessReady { get { return GetWirelessState() == WirelessState.Ready; } }
        public bool WirelessRunning
        {
            get
            {
                WirelessState state = GetWirelessState();
                return state == WirelessState.Ready || state == WirelessState.Starting;
            }
        }

        private WirelessState ReadWirelessState()
        {
            int running, ready;
            Check(im_wireless_receiver_get_status(out running, out ready), "读取 AirPlay 接收状态");
            if (running == 0) return wirelessName == null ? WirelessState.Off : WirelessState.Failed;
            if (ready != 0) return WirelessState.Ready;
            return wirelessStartup.IsRunning && wirelessStartup.Elapsed.TotalSeconds >= 20
                ? WirelessState.Failed : WirelessState.Starting;
        }

        internal static bool IsWirelessProfile(int width, int height, int fps)
        {
            foreach (var profile in WirelessProfile.Choices)
                if (width==profile.Width && height==profile.Height && fps==profile.Fps) return true;
            return width==5120 && height==2880 && fps==60;
        }

        internal static string ValidateReceiverName(string name)
        {
            name = string.IsNullOrWhiteSpace(name) ? "WeiMirror" : name.Trim();
            // The combined host publishes a RAOP instance named 12 hex digits + '@' + name.
            // That complete DNS-SD label must fit in 63 octets (RFC 6763 §4.1.1).
            if (Encoding.UTF8.GetByteCount(name) > 50)
                throw new ArgumentException("接收名称最多可用 50 个 UTF-8 字节，请缩短名称。");
            foreach (char c in name)
            {
                if (char.IsControl(c) || c == '[' || c == ']' || c == ';' || c == '"')
                    throw new ArgumentException("接收名称含有不支持的符号，请使用文字、数字、空格或连字符。");
            }
            return name;
        }

        private static void CheckWirelessRuntime(string directory, string host)
        {
            if (!File.Exists(host)) throw new FileNotFoundException("AirPlay 接收组件不完整，缺少 " + HostFile, host);
            foreach (string file in WirelessRuntimeFiles)
            {
                string path = Path.Combine(directory, file);
                if (!File.Exists(path)) throw new FileNotFoundException("AirPlay 接收组件不完整，缺少 " + file, path);
            }
            // This upstream command only loads the decoder and checks exported
            // functions. It does not listen on any ports or advertise a receiver.
            var options = new ProcessStartInfo(host, "--check-runtime")
            {
                WorkingDirectory = directory,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            try
            {
                using (Process probe = Process.Start(options))
                {
                    if (probe == null) throw new IOException("无法运行 AirPlay 组件自检。");
                    if (!probe.WaitForExit(6000))
                    {
                        try { probe.Kill(); }
                        catch (InvalidOperationException) { }
                        throw new TimeoutException("AirPlay 组件自检超时，请检查组件后再试。");
                    }
                    int code = probe.ExitCode;
                    if (code == 40)
                        throw new InvalidOperationException("Windows 的代码完整性策略阻止加载 AirPlay 组件。");
                    if (code != 0)
                        throw new InvalidOperationException("AirPlay 组件无法加载，请检查 v1.8.3 运行库。自检代码 " + code + "。");
                }
            }
            catch (Win32Exception error)
            {
                throw new IOException("Windows 无法启动 AirPlay 组件自检。" + error.Message, error);
            }
        }
    }
}
