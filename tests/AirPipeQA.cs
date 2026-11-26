using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

// Measures the local decoded-frame transfer only, with generated full-size I420 frames.
internal static class AirPipeQA
{
    const int Width=1280,Height=720,Y=Width*Height,UV=Y/4,Size=Y+2*UV;
    static readonly IntPtr Zero=IntPtr.Zero;
    static void Read(Stream s,byte[] buffer)
    {
        for(int pos=0;pos<buffer.Length;) {
            int n=s.Read(buffer,pos,buffer.Length-pos);
            if(n==0) throw new EndOfStreamException();
            pos+=n;
        }
    }
    static void Run(Type host,bool stall)
    {
        string name="WeiMirror-local-frames-"+Guid.NewGuid().ToString("N");
        using(var server=new NamedPipeServerStream(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous,4096,4096))
        using(var gate=new ManualResetEventSlim(!stall)) {
            var waiting=server.WaitForConnectionAsync();
            string path=@"\\.\pipe\"+name;
            using(var pipe=(IDisposable)host.GetMethod("Connect").Invoke(null,new object[]{path})) {
                if(!waiting.Wait(7000)) throw new TimeoutException("Local pipe connect");
                var delays=new List<double>();
                int final=-1, count=stall ? 25 : 120;
                var clock=Stopwatch.StartNew();
                Task reader=Task.Run(()=>{
                    gate.Wait();
                    var header=new byte[392]; var frame=new byte[Size];
                    for(;;) {
                        Read(server,header);
                        int kind=BitConverter.ToUInt16(header,6);
                        if(kind==3) break;
                        if(kind!=4 || BitConverter.ToInt32(header,8)!=Size || BitConverter.ToInt32(header,12)!=Width ||
                           BitConverter.ToInt32(header,16)!=Height) throw new InvalidDataException("Wrong YUV frame header");
                        Read(server,frame);
                        int id=BitConverter.ToInt32(frame,8);
                        if(id<=final || frame[Size-1]!=(byte)id) throw new InvalidDataException("Stale, damaged or reordered frame");
                        final=id;
                        delays.Add((clock.ElapsedTicks-BitConverter.ToInt64(frame,0))*1000.0/Stopwatch.Frequency);
                    }
                });
                IntPtr data=Marshal.AllocHGlobal(Size);
                try {
                    var video=host.GetMethod("Video");
                    var args=new object[]{Zero,Zero,Width,Height,Width,Width/2,Width/2,Y,UV,UV,data,Size};
                    long start=clock.ElapsedTicks;
                    for(int i=0;i<count;i++) {
                        if(!stall) while(clock.ElapsedTicks<start+i*Stopwatch.Frequency/60) Thread.Sleep(1);
                        Marshal.WriteInt64(data,0,clock.ElapsedTicks);
                        Marshal.WriteInt32(data,8,i);
                        Marshal.WriteByte(data,Size-1,(byte)i);
                        video.Invoke(pipe,args);
                    }
                    host.GetMethod("Disconnected").Invoke(pipe,new object[]{Zero,Zero});
                    gate.Set();
                    if(!reader.Wait(15000)) throw new TimeoutException("Queued frames did not drain");
                    if(final!=count-1 || delays.Count==0 || (stall && delays.Count>2))
                        throw new InvalidDataException("Newest video frame did not replace the stale queue");
                    var sorted=delays.OrderBy(d=>d).ToArray();
                    Console.WriteLine(stall ? "PASS stalled reader kept newest of {0} frames; delivered {1}" :
                        "PASS generated 720p60 local transfer {1}/{0} frames; median {2:0.0} ms, P95 {3:0.0} ms",
                        count,sorted.Length,sorted[(sorted.Length-1)/2],sorted[(int)Math.Ceiling(sorted.Length*.95)-1]);
                } finally {gate.Set(); Marshal.FreeHGlobal(data);}
            }
        }
    }
    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] static extern bool TerminateProcess(IntPtr p,uint code);
    static int Main(string[] args)
    {
        int result=0;
        try {
            var t=Assembly.LoadFrom(Path.GetFullPath(args[0])).GetType("MirrorDesk.AirPlayHostPipe",true);
            Run(t,false); Run(t,true);
        }catch(Exception e){Console.Error.WriteLine(e);result=1;}
        Console.Out.Flush();Console.Error.Flush();TerminateProcess(GetCurrentProcess(),(uint)result);
        return result;
    }
}
