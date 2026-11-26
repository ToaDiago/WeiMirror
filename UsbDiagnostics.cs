// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Runtime.InteropServices;

namespace MirrorDesk
{
    internal sealed partial class Device
    {
        internal int ConnectionState { get; set; }
        internal bool PairRecord { get; set; }
        internal bool DeviceInfoReadable { get; set; }
        internal string ConnectionDetail { get; set; }

        internal bool SameUsbDiagnosis(Device other)
        {
            return ConnectionState==other.ConnectionState && PairRecord==other.PairRecord &&
                DeviceInfoReadable==other.DeviceInfoReadable && ConnectionDetail==other.ConnectionDetail;
        }
    }

    internal sealed partial class StreamStatus
    {
        internal int FailureKind { get; set; }
        internal int FailureStage { get; set; }
    }

    internal sealed partial class MirrorCore
    {
        // CoreApi.h v1.8.3 EnvironmentInfo. Automatic probes read the physical mobile-device count
        // reported by Windows PnP; the libusb enumeration fields at the end may be unknown.
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
        internal struct UsbEnvironment
        {
            internal uint Size, Version;
            internal int ServiceInstalled, ServiceRunning, StandardMux, CaptureMux;
            internal uint PhysicalDevices;
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=512)] internal string Diagnostic;
            internal int LibUsbRuntime, UsbDk;
            internal uint LibUsbDevices;
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)] internal string LibUsbVersion;
            internal int UsbDkKnown, LibUsbDevicesKnown;
        }

        [DllImport(CoreLibrary,CallingConvention=Convention,ExactSpelling=true)]
        private static extern int im_get_environment(ref UsbEnvironment result);

        internal UsbEnvironment? ReadUsbEnvironment()
        {
            lock (gate)
            {
                EnsureOpen();
                var env = new UsbEnvironment { Size=(uint)Marshal.SizeOf(typeof(UsbEnvironment)), Version=SupportedVersion };
                try { return im_get_environment(ref env)==0 && env.Version==SupportedVersion ? env : (UsbEnvironment?)null; }
                catch (EntryPointNotFoundException) { return null; }
            }
        }
    }
}
