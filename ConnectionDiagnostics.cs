// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Threading.Tasks;

namespace MirrorDesk
{
    internal static class ConnectionDiagnostics
    {
        internal static string UsbDevice(Device device)
        {
            string prefix=device.Name+" · ";
            if (device.DeviceInfoReadable) return prefix+"设备信息可读，可开始 USB 投屏。";
            string detail=device.ConnectionDetail ?? String.Empty;
            // A false pair-record flag alone also occurs when querying metadata fails.
            if (detail=="Waiting for this computer to be trusted" || detail=="等待在 iPhone 上信任此电脑" ||
                detail.IndexOf("PairingDialogResponsePending",StringComparison.OrdinalIgnoreCase)>=0)
                return prefix+"等待在 iPad/iPhone 上信任此电脑。请解锁设备并确认提示，然后点「刷新」。";
            if (detail.IndexOf("UserDeniedPairing",StringComparison.OrdinalIgnoreCase)>=0)
                return prefix+"设备已拒绝配对。请重连并在设备上确认信任，然后点「刷新」。";
            if (device.PairRecord) return prefix+"已有配对记录，暂时无法读取设备信息。请解锁设备后点「刷新」。";
            if (device.ConnectionState==1) return prefix+"Windows 已识别 USB 设备，配对通道尚未就绪。请检查 USB 驱动。";
            return prefix+"已检测到 USB 连接，配对状态尚未确认。请解锁设备后点「刷新」。";
        }

        internal static string NoUsb(MirrorCore.UsbEnvironment? state)
        {
            if (!state.HasValue) return "接收组件未列出 USB 设备，暂时无法读取 Windows 的识别状态。可检查线缆后点「刷新」。";
            var env=state.Value;
            if (env.PhysicalDevices==0) return "Windows 尚未识别到可投屏的 Apple USB 设备。请用支持数据传输的线缆直连，再换一个 USB 接口试试。";
            string found="Windows 已识别 Apple USB 设备。";
            if (env.StandardMux==0 && env.CaptureMux==0)
            {
                if (env.ServiceInstalled==0) return found+"未检测到 Apple 移动设备服务，请检查「USB 驱动管理」。";
                if (env.ServiceRunning==0) return found+"Apple 移动设备服务未运行。请打开 Apple Devices 或检查 USB 驱动，再点「刷新」。";
                return found+"Apple 移动设备服务正在运行，连接通道尚未就绪。请解锁并重新连接。";
            }
            return found+"配对通道可用，接收组件尚未列出设备。请解锁，若出现信任提示请确认，再点「刷新」。";
        }

        internal static string AirWaiting(string name)
        {
            return "AirPlay 广播已就绪，等待 iPad 连接。在「屏幕镜像」中选择「"+name.Trim()+"」。若列表为空，请确认双方在同一局域网。";
        }

        internal static string FrameWait(StreamStatus status,bool wireless,long elapsedMs)
        {
            if (!wireless && status.State==1) return elapsedMs>=12000
                ? "USB 设备切换到投屏模式已超过 12 秒。请检查 USB 驱动，或停止后重试。"
                : "USB 会话已建立，正在切换设备到投屏模式。";
            if (!wireless && status.State==2) return elapsedMs>=12000
                ? "USB 设备尚未重新出现。请检查数据线连接并解锁设备，或停止后重试。"
                : "USB 正在等待设备重新出现。请保持数据线连接并解锁设备。";
            if (elapsedMs>=12000) return wireless
                ? "AirPlay 设备已连接，仍未收到可显示的画面。请在 iPad 上停止屏幕镜像后重连。"
                : "USB 会话已建立，仍未收到可显示的画面。请解锁设备，若仍无画面可停止后重新连接。";
            if (status.State==3) return wireless ? "AirPlay 设备已连接，正在协商画面。" : "USB 设备已连接，正在协商画面。";
            string session=wireless ? "AirPlay 设备已连接" : "USB 会话已建立";
            return session+(status.Frames==0 ? "，等待视频首帧。" : "，等待视频画面恢复。");
        }

        internal static string Failure(StreamStatus status,bool wireless)
        {
            string stage;
            switch (status.FailureKind)
            {
                case 3: stage=wireless ? "无线接收组件报告驱动异常。" : "接收组件报告驱动异常。请打开「USB 驱动管理」检查。"; break;
                case 8: stage=wireless ? "AirPlay 设备已断开。" : "USB 设备已断开。"; break;
                case 6: stage="视频连接未收到可显示的帧。请解锁设备并重新连接。"; break;
                case 10: stage="已有投屏会话占用设备。请结束其他接收会话后重试。"; break;
                default:
                    if (status.FailureStage==7) stage="解码视频时出错。请重新连接。";
                    else if (status.FailureStage==5) stage="USB 视频握手失败。请解锁设备后重连。";
                    else stage=wireless ? "AirPlay 视频会话已结束。" : "USB 视频会话已结束。";
                    break;
            }
            return String.IsNullOrWhiteSpace(status.Message) ? stage : stage+" "+status.Message;
        }
    }

    internal sealed partial class MainForm
    {
        long usbProbeAt=-15000;
        bool previousUsbListEmpty, applyingUsbList;
        MirrorCore.UsbEnvironment? lastUsbEnvironment;
        string previousUsbDiagnosis;

        void ShowSelectedUsbDiagnosis()
        {
            var device=devices.SelectedItem as Device;
            if (device==null || core==null || Wireless || playing || busy || closing || applyingUsbList) return;
            previousUsbDiagnosis=ConnectionDiagnostics.UsbDevice(device);
            footer.Text=previousUsbDiagnosis;
        }

        async Task ShowUsbDiscovery(Device[] list,bool explicitRefresh,int expectedRevision)
        {
            if (Wireless || closing || expectedRevision!=revision) return;
            applyingUsbList=true;
            try { SetDevices(list); }
            finally { applyingUsbList=false; }
            bool empty=list.Length==0;
            bool changed=empty!=previousUsbListEmpty;
            previousUsbListEmpty=empty;
            string message;
            if (empty)
            {
                if (explicitRefresh || changed || clock.ElapsedMilliseconds-usbProbeAt>=15000)
                {
                    var environment=await OnCore(() => core.ReadUsbEnvironment());
                    if (closing || expectedRevision!=revision || Wireless) return;
                    usbProbeAt=clock.ElapsedMilliseconds;
                    lastUsbEnvironment=environment;
                }
                message=ConnectionDiagnostics.NoUsb(lastUsbEnvironment);
            }
            else message=ConnectionDiagnostics.UsbDevice((devices.SelectedItem as Device) ?? list[0]);
            if (explicitRefresh || changed || previousUsbDiagnosis!=message) footer.Text=message;
            previousUsbDiagnosis=message;
        }
    }
}
