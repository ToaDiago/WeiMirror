using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace MirrorDesk
{
    internal sealed partial class MainForm
    {
        // Present from a successful USB start until an explicit stop or transport change.
        // This is the device the user selected, even when other devices share its name.
        Device usbTarget;
        bool WaitingForUsb { get { return usbTarget!=null && !playing; } }
        long nextUsbProbeAt, usbFrameChangedAt, usbRetryAt;
        ulong usbLastFrames;
        int usbAbsentSamples, usbStatusErrors, usbRetryAttempts;

        void ResetUsbPresenceProbe()
        {
            usbAbsentSamples=usbStatusErrors=0;
            usbLastFrames=0;
            usbFrameChangedAt=clock.ElapsedMilliseconds;
            nextUsbProbeAt=usbFrameChangedAt+1000;
        }

        bool UsbRecoveryCurrent(int check, PreviewWindow window)
        {
            return WaitingForUsb && !Wireless && !closing && !previewStopQueued &&
                check==revision && window!=null && window==preview && !window.IsDisposed;
        }

        static bool SameUsb(Device candidate, Device target)
        {
            return String.Equals(candidate.Id,target.Id,StringComparison.OrdinalIgnoreCase);
        }

        async Task<bool?> ProbeUsbPresence(Device target)
        {
            // A busy scanner is not evidence of removal. A terminal session can
            // still show its own failure if the secondary presence check fails.
            try {
                var list=await OnCore(() => core.GetUsbDevices(false));
                return list.Any(d=>SameUsb(d,target));
            }
            catch (InvalidOperationException) { return null; }
            catch (IOException) { return null; }
        }

        // Fast enumeration can briefly lose the USB identity while Apple's interface changes.
        // Only probe a stalled session; require two missing samples before disrupting it.
        async Task<bool> CheckStalledUsb(StreamStatus status, int check)
        {
            long now=clock.ElapsedMilliseconds;
            bool streaming=status.State==4 && status.Frames>0 && status.Width>0 && status.Height>0;
            if (streaming && status.Frames!=usbLastFrames) {
                usbLastFrames=status.Frames; usbFrameChangedAt=now;
                usbAbsentSamples=usbRetryAttempts=0;
                return false;
            }
            // Before the first frame the native USB switch can deliberately remove the
            // device: its helper waits up to 15 s for re-enumeration, then for stability.
            if (now<nextUsbProbeAt || (usbLastFrames==0 && now-sessionStarted<20000) ||
                (streaming && now-usbFrameChangedAt<3500)) return false;
            nextUsbProbeAt=now+1000;
            var target=usbTarget;
            bool? present=await ProbeUsbPresence(target);
            if (check!=revision || closing || usbTarget!=target) return true;
            if (present!=false) { usbAbsentSamples=0; return false; }
            if (++usbAbsentSamples<2) return false;
            if (streaming) {
                // A still screen can stop producing new frames. Some USB
                // configurations also hide the mux identity during capture.
                // Keep a nominally live session unless Windows confirms removal.
                var hardware=await OnCore(() => core.ReadUsbEnvironment());
                if (check!=revision || closing || usbTarget!=target) return true;
                if (!hardware.HasValue || hardware.Value.PhysicalDevices!=0) {
                    usbAbsentSamples=0; nextUsbProbeAt=clock.ElapsedMilliseconds+5000;
                    return false;
                }
            }
            await ActionAsync(() => SuspendUsb(null));
            return true;
        }

        async Task SuspendUsb(string detailMessage)
        {
            playing=hadVideo=false;
            timer.Interval=700;
            long now=clock.ElapsedMilliseconds;
            nextUsbProbeAt=now+700;
            usbRetryAt=now+UsbRetryDelay();
            string prompt="USB 连接已中断。请重新连接「"+usbTarget.Name+"」，投屏会在此窗口恢复。";
            preview.SetSnapshotAvailable(false);
            preview.SetStats("");
            preview.SetOverlay(prompt);
            footer.Text=String.IsNullOrWhiteSpace(detailMessage) ? prompt : detailMessage+" 等待原设备重新连接。";
            UpdateControls();
            // Keep the render HWND alive until native stop and destruction have finished.
            try { await OnCore(() => core.Stop()); }
            catch (InvalidOperationException ex) { footer.Text=prompt+" "+ex.Message; }
        }

        int UsbRetryDelay()
        {
            return Math.Min(5000,750*(1<<Math.Min(usbRetryAttempts,3)));
        }

        async Task TryResumeUsb(int check)
        {
            long now=clock.ElapsedMilliseconds;
            if (now<nextUsbProbeAt) return;
            nextUsbProbeAt=now+900;
            var target=usbTarget;
            var window=preview;
            var list=await OnCore(() => core.GetUsbDevices(false));
            if (!UsbRecoveryCurrent(check,window)) return;
            var same=list.FirstOrDefault(d=>SameUsb(d,target));
            if (same==null) { usbRetryAttempts=0; return; }
            if (now<usbRetryAt) return;
            usbRetryAttempts=Math.Min(usbRetryAttempts+1,3);
            try { await StartDevice(same,window); }
            catch (Exception ex) {
                if (!UsbRecoveryCurrent(check,window)) return;
                usbRetryAt=clock.ElapsedMilliseconds+UsbRetryDelay();
                footer.Text="已发现原设备，正在等待重试。"+ex.GetBaseException().Message;
                window.SetOverlay("「"+target.Name+"」已重新出现，正在重试 USB 连接…");
            }
        }
    }
}
