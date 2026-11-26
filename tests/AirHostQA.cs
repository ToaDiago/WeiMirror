// Test-only SDK exports and wire reader for the optional 720p60 transport.
// Runs a separate process, with a real pipe and a non-networking ABI fixture.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public static class FixtureSdk
{
    static readonly IntPtr Server=new IntPtr(0x1234);
    static Thread sender;
    static string Prefix {get {return Environment.GetEnvironmentVariable("WEIMIRROR_QA_IPC_PATH");}}
    static string Utf8(IntPtr ptr)
    {
        int size=0;
        while (Marshal.ReadByte(ptr,size)!=0 && size<512) size++;
        byte[] data=new byte[size]; Marshal.Copy(ptr,data,0,size);
        return Encoding.UTF8.GetString(data);
    }
    static void Callback(int slot,IntPtr self,params IntPtr[] arguments)
    {
        // This unmanaged SDK always invokes void callbacks through the x64 C++ vtable.
        // CoreCLR may return the original (private) delegate for reverse P/Invoke
        // stubs; DynamicInvoke avoids casts to the fixture's independent signature.
        var fn=Marshal.ReadIntPtr(Marshal.ReadIntPtr(self),IntPtr.Size*slot);
        var signature=arguments.Length==2 ? typeof(PeerCall) : typeof(FrameCall);
        Marshal.GetDelegateForFunctionPointer(fn,signature).DynamicInvoke(new[]{self}.Concat(arguments).Cast<object>().ToArray());
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void PeerCall(IntPtr self,IntPtr name,IntPtr id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void FrameCall(IntPtr self,IntPtr data,IntPtr name,IntPtr id);
    static void EmitFrames(IntPtr self)
    {
        using (var step=EventWaitHandle.OpenExisting(Environment.GetEnvironmentVariable("WEIMIRROR_QA_IPC_STEP")))
        {
            var remote=Encoding.UTF8.GetBytes("测试 iPad\0");
            IntPtr name=Marshal.AllocHGlobal(remote.Length), id=Marshal.StringToHGlobalAnsi("QA-13-device");
            Marshal.Copy(remote,0,name,remote.Length);
            IntPtr frame=Marshal.AllocHGlobal(64), data=Marshal.AllocHGlobal(6);
            try
            {
                Action wait=()=>{if (!step.WaitOne(10000)) throw new TimeoutException("SDK step");};
                wait(); Callback(0,self,name,id);
                wait();
                Marshal.Copy(new byte[64],0,frame,64); Marshal.Copy(new byte[]{31,32,33,34,111,112},0,data,6);
                Marshal.WriteInt32(frame,12,2); Marshal.WriteInt32(frame,16,2);
                Marshal.WriteInt32(frame,20,2); Marshal.WriteInt32(frame,24,1); Marshal.WriteInt32(frame,28,1);
                Marshal.WriteInt32(frame,32,4); Marshal.WriteInt32(frame,36,1); Marshal.WriteInt32(frame,40,1);
                Marshal.WriteInt32(frame,44,6); Marshal.WriteIntPtr(frame,48,data);
                Callback(3,self,frame,name,id);
                wait();
                Marshal.Copy(new byte[64],0,frame,64); Marshal.Copy(new byte[]{9,8,7,6},0,data,4);
                Marshal.WriteInt32(frame,8,48000); Marshal.WriteInt16(frame,12,2); Marshal.WriteInt16(frame,14,16);
                Marshal.WriteInt32(frame,16,4); Marshal.WriteIntPtr(frame,24,data);
                Callback(2,self,frame,name,id);
                wait(); Callback(1,self,name,id);
            }
            catch (Exception ex) {File.WriteAllText(Prefix+".callback-error",ex.ToString());}
            finally {foreach (var ptr in new[]{name,id,frame,data}) Marshal.FreeHGlobal(ptr);}
        }
    }
    public static IntPtr Start(IntPtr name,uint raop,uint air,IntPtr callbacks)
    {
        string[] capture = new[]{"WIDTH","HEIGHT","FPS","MODE","NAME"}.Select(k=>
            Environment.GetEnvironmentVariable("IPHONE_MIRROR_AIRPLAY_"+k) ?? "").ToArray();
        File.WriteAllLines(Prefix+".advert",capture.Concat(new[]{Utf8(name),raop.ToString(),air.ToString()}));
        if (Environment.GetEnvironmentVariable("WEIMIRROR_QA_NO_PUBLIC_KEY")!="1")
            Environment.SetEnvironmentVariable("IPHONE_MIRROR_AIRPLAY_PUBLIC_KEY",new string('a',64));
        if (Environment.GetEnvironmentVariable("WEIMIRROR_QA_NO_PUBLIC_KEY")!="1") {
            sender=new Thread(()=>EmitFrames(callbacks)) {IsBackground=true}; sender.Start();
        }
        return Server;
    }
    public static void Stop(IntPtr server)
    {
        if(server!=Server) throw new InvalidOperationException("Incorrect SDK server handle");
        if(sender!=null) sender.Join(1000);
        File.WriteAllText(Prefix+".stopped","ok");
    }
}

static class AirHostQA
{
    static int checks;
    static void Check(bool valid,string label)
    {
        if(!valid) throw new InvalidDataException(label);
        Console.WriteLine("PASS HOST "+label); checks++;
    }
    static string Quote(string arg) {return "\""+arg.Replace("\"","\\\"")+"\"";}
    static byte[] ReadExact(Stream stream,int count)
    {
        byte[] buffer=new byte[count];
        for(int pos=0;pos<count;) {
            int read=stream.Read(buffer,pos,count-pos);
            if(read==0) throw new EndOfStreamException(); pos+=read;
        }
        return buffer;
    }
    sealed class Packet {
        internal byte[] Header,Data;
        internal int Type {get{return BitConverter.ToUInt16(Header,6);}}
        internal uint U32(int off) {return BitConverter.ToUInt32(Header,off);}
        internal string Text(int offset,int length) {
            int end=Array.IndexOf(Header,(byte)0,offset,length);
            return Encoding.UTF8.GetString(Header,offset,end<0 ? length : end-offset);
        }
    }
    static Packet PacketOf(Stream stream,int expected)
    {
        for(int i=0;i<100;i++) {
            var msg=new Packet{Header=ReadExact(stream,392)};
            if(msg.U32(0)!=0x50414d49 || BitConverter.ToUInt16(msg.Header,4)!=7 || msg.U32(8)>2*1024*1024)
                throw new InvalidDataException("Malformed native IPC v7 header");
            msg.Data=ReadExact(stream,(int)msg.U32(8));
            if(msg.Type==expected)return msg;
            if(msg.Type!=6 && msg.Type!=7)throw new InvalidDataException("Expected wire type "+expected+", got "+msg.Type);
        }
        throw new InvalidDataException("Missing expected native message");
    }
    static void Wire(Stream pipe,EventWaitHandle step,string prefix,string receiver,string profile)
    {
        PacketOf(pipe,1);
        var advertised=File.ReadAllLines(prefix+".advert");
        string[] expected=profile.Split('x');
        Check(advertised.SequenceEqual(new[]{expected[0],expected[1],expected[2],"mirror",receiver,receiver,"5001","7001"}),
            "SDK receives " + profile + ", UTF-8 name and both mirroring ports despite stock core carrier at 30 fps");
        step.Set(); var connected=PacketOf(pipe,2);
        Check(connected.Text(104,64)=="QA-13-device" && connected.Text(168,128)=="测试 iPad","connected IPC carries exact device identity and UTF-8 peer name");
        step.Set(); var video=PacketOf(pipe,4);
        Check(video.U32(12)==2 && video.U32(16)==2 && video.U32(20)==2 && video.U32(32)==4 &&
            video.U32(36)==1 && video.U32(40)==1 && video.Text(104,64)=="QA-13-device" &&
            video.Data.SequenceEqual(new byte[]{31,32,33,34,111,112}),"native YUV frame metadata and all plane bytes survive SDK-to-core IPC");
        step.Set(); var audio=PacketOf(pipe,5);
        Check(audio.U32(44)==48000 && BitConverter.ToUInt16(audio.Header,48)==2 && BitConverter.ToUInt16(audio.Header,50)==16 &&
            audio.Data.SequenceEqual(new byte[]{9,8,7,6}),"48kHz stereo PCM samples and metadata survive SDK-to-core IPC");
        step.Set(); var gone=PacketOf(pipe,3);
        Check(gone.Text(104,64)=="QA-13-device","disconnect IPC releases the same device");
    }
    static int Child(string[] args)
    {
        string receipt=Environment.GetEnvironmentVariable("WEIMIRROR_QA_IPC_PATH")+".child";
        File.AppendAllText(receipt,"started\n");
        var type=Assembly.LoadFrom(args[1]).GetType("MirrorDesk.AirPlay720Host",true);
        var entry=type.GetMethod("RunWithLibrary",BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public);
        int code=(int)entry.Invoke(null,new object[]{args.Skip(3).ToArray(),args[2]});
        File.AppendAllText(receipt,"returned "+code+"\n");
        return code;
    }
    static void Run(string app,string sdk,string scenario)
    {
        bool valid=scenario!="no-key", disconnectPipe=scenario=="pipe-loss";
        string profile=scenario=="1080p120" ? "1920x1080x120" : scenario=="2k60" ? "2560x1440x60" : "1280x720x60";
        string unique=Guid.NewGuid().ToString("B");
        string suffix=Process.GetCurrentProcess().Id+"-"+unique, receiver="同步 iPad";
        string pipeName="iPhoneMirror-AirPlay-Hub-"+suffix, stop="Local\\iPhoneMirror-AirPlay-Hub-Stop-"+suffix;
        string stepName="Local\\WeiMirror-QA-"+unique, prefix=Path.Combine(Path.GetDirectoryName(sdk),"sdk-"+unique);
        string[] args={"--child",app,sdk,"--pipe",@"\\.\pipe\"+pipeName,"--stop-event",stop,"--name",receiver,
            "--parent-pid",Process.GetCurrentProcess().Id.ToString(),"--width","1280","--height","720","--fps","30",
            "--mode","combined","--raop-port","5001","--airplay-port","7001"};
        var options=new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName,String.Join(" ",args.Select(Quote)))
            {UseShellExecute=false,CreateNoWindow=true};
        options.EnvironmentVariables["WEIMIRROR_QA_IPC_PATH"]=prefix;
        options.EnvironmentVariables["WEIMIRROR_QA_IPC_STEP"]=stepName;
        options.EnvironmentVariables["WEIMIRROR_QA_NO_PUBLIC_KEY"]=valid ? "0":"1";
        options.EnvironmentVariables.Remove("IPHONE_MIRROR_AIRPLAY_PUBLIC_KEY");
        options.EnvironmentVariables["WEIMIRROR_AIRPLAY_PROFILE"]=scenario=="1080p120" ? "4" : scenario=="2k60" ? "5" : "3";
        using(var pipe=new NamedPipeServerStream(pipeName,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous))
        using(var stopEvent=new EventWaitHandle(false,EventResetMode.ManualReset,stop))
        using(var step=new EventWaitHandle(false,EventResetMode.AutoReset,stepName))
        using(var child=Process.Start(options))
        {
            try {
                var connection=pipe.WaitForConnectionAsync();
                var timeout=Stopwatch.StartNew();
                while (!connection.IsCompleted && !child.HasExited && timeout.ElapsedMilliseconds<9000) Thread.Sleep(30);
                if (!connection.IsCompleted) throw new IOException("SDK host never connected. Exit="+(child.HasExited ? child.ExitCode.ToString() : "running"));
                connection.GetAwaiter().GetResult();
                Task traffic=Task.Run(()=>{
                    if(valid) Wire(pipe,step,prefix,receiver,profile);
                    else {
                        try { PacketOf(pipe,1); throw new Exception("Host reported ready without public key"); }
                        catch(EndOfStreamException) {}
                    }
                });
                if(!traffic.Wait(12000)) throw new TimeoutException("Host or callback did not complete. Exit="+(child.HasExited ? child.ExitCode.ToString() : "running"));
                if(disconnectPipe) pipe.Dispose(); else stopEvent.Set();
                var limit=Stopwatch.StartNew();
                while(!child.HasExited && limit.ElapsedMilliseconds<10000) Thread.Sleep(30);
                Check(child.HasExited && (valid ? child.ExitCode==0 : child.ExitCode!=0) && File.Exists(prefix+".stopped"),
                    !valid ? "missing pairing public key prevents readiness and cleans SDK" :
                    disconnectPipe ? "loss of the parent pipe stops SDK and child without a stop event" : "parent stop event closes SDK and child cleanly");
                // Each SDK scenario is a separate process; the child and its SDK
                // have already stopped. Release instrumentation-owned pipe handles
                // at process exit, as in ReceiverQA/GuiQA with their CLR exports.
                Console.WriteLine("Host scenario checks: "+checks);
                Console.Out.Flush(); Console.Error.Flush(); TerminateProcess(GetCurrentProcess(),0);
            } finally {
                if(!child.HasExited){stopEvent.Set();step.Set();child.Kill();}
                if(File.Exists(prefix+".callback-error"))Console.Error.WriteLine(File.ReadAllText(prefix+".callback-error"));
            }
        }
    }
    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] static extern bool TerminateProcess(IntPtr handle,uint code);
    static int Main(string[] args)
    {
        int result=0;
        try {
            if(args[0]=="--child") result=Child(args);
            else if(args.Length==3) Run(args[0],args[1],args[2]);
            else throw new ArgumentException("Requires app, SDK fixture and scenario.");
        }
        catch(Exception ex) {Console.Error.WriteLine(ex);result=1;}
        Console.Out.Flush();Console.Error.Flush();
        TerminateProcess(GetCurrentProcess(),(uint)result);
        return result;
    }
}
