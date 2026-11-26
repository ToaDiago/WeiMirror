using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

internal static class DiagnosticsQA
{
    static int checks;
    const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static void Check(bool condition,string label)
    {
        if (!condition) throw new Exception(label);
        checks++; Console.WriteLine("PASS "+label);
    }

    static int Main(string[] args)
    {
        var app=Assembly.LoadFrom(Path.GetFullPath(args[0]));
        var report=app.GetType("MirrorDesk.ConnectionDiagnostics",true);
        Func<string,object[],string> format=(name,values)=>(string)report.GetMethod(name,All).Invoke(null,values);
        var envType=app.GetType("MirrorDesk.MirrorCore+UsbEnvironment",true);
        Check(Marshal.SizeOf(envType)==1136 && Marshal.OffsetOf(envType,"PhysicalDevices").ToInt32()==24 &&
            Marshal.OffsetOf(envType,"LibUsbDevicesKnown").ToInt32()==1132,"v18 environment ABI layout");
        var env=Activator.CreateInstance(envType);
        Func<string> noUsb=()=>format("NoUsb",new[]{env});
        Check(noUsb().Contains("Windows 尚未识别"),"no physical device");
        Check(format("NoUsb",new object[]{null}).Contains("暂时无法读取"),"unknown probe never reported as absent");
        envType.GetField("PhysicalDevices",All).SetValue(env,1U);
        Check(noUsb().Contains("未检测到 Apple 移动设备服务"),"physical device with missing service");
        envType.GetField("ServiceInstalled",All).SetValue(env,1);
        Check(noUsb().Contains("服务未运行"),"physical device with stopped service");
        envType.GetField("ServiceRunning",All).SetValue(env,1);
        Check(noUsb().Contains("通道尚未就绪"),"physical device with live service but no mux");
        envType.GetField("CaptureMux",All).SetValue(env,1);
        Check(noUsb().Contains("配对通道可用"),"alternate mux available");

        var dev=app.GetType("MirrorDesk.Device",true);
        var device=Activator.CreateInstance(dev,All,null,new object[]{"test","桌面的 iPad",false},null);
        Func<string> usb=()=>format("UsbDevice",new[]{device});
        Check(usb().Contains("配对状态尚未确认"),"unknown pairing never reported as rejected or awaiting trust");
        dev.GetProperty("ConnectionDetail",All).SetValue(device,"Waiting for this computer to be trusted",null);
        Check(usb().Contains("等待在 iPad/iPhone 上信任"),"explicit trust pending");
        dev.GetProperty("ConnectionDetail",All).SetValue(device,"设备已连接，但 Lockdown 拒绝访问：UserDeniedPairing",null);
        Check(usb().Contains("已拒绝"),"explicit denial");
        dev.GetProperty("ConnectionDetail",All).SetValue(device,"",null);
        dev.GetProperty("PairRecord",All).SetValue(device,true,null);
        Check(usb().Contains("已有配对记录"),"known pair record without readable metadata");
        dev.GetProperty("DeviceInfoReadable",All).SetValue(device,true,null);
        Check(usb().Contains("可开始 USB 投屏"),"readable and ready");

        var st=app.GetType("MirrorDesk.StreamStatus",true);
        var status=Activator.CreateInstance(st,true);
        Func<bool,long,string> waiting=(wireless,elapsed)=>format("FrameWait",new object[]{status,wireless,elapsed});
        st.GetProperty("State",All).SetValue(status,3,null);
        Check(waiting(false,1).Contains("正在协商") && waiting(true,1).Contains("AirPlay 设备已连接"),"connected handshaking distinct from broadcast");
        Check(waiting(true,13000).Contains("停止屏幕镜像") && !waiting(false,13000).Contains("屏幕镜像"),"transport-specific first-frame timeout");
        st.GetProperty("State",All).SetValue(status,1,null);
        Check(waiting(false,13000).Contains("USB 驱动"),"stalled USB activation has its own guidance");
        st.GetProperty("State",All).SetValue(status,2,null);
        Check(waiting(false,13000).Contains("USB 设备尚未重新出现"),"stalled USB reenumeration has its own guidance");
        st.GetProperty("State",All).SetValue(status,4,null);
        Check(waiting(true,1).Contains("首帧"),"empty session identified as waiting for first frame");
        st.GetProperty("Frames",All).SetValue(status,4UL,null);
        Check(waiting(true,1).Contains("恢复"),"interrupted stream distinguished from new session");
        Check(format("AirWaiting",new object[]{"办公室"}).Contains("「办公室」"),"AirPlay ready names correct target");
        st.GetProperty("FailureStage",All).SetValue(status,7,null);
        st.GetProperty("Message",All).SetValue(status,"decoder returned an error",null);
        string failure=format("Failure",new object[]{status,true});
        Check(failure.Contains("解码") && failure.Contains("decoder returned an error"),"typed failure retains exact native cause");
        Console.WriteLine("Diagnostic checks: "+checks);
        return 0;
    }
}
