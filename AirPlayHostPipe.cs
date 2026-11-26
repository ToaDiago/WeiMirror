// SPDX-License-Identifier: GPL-3.0-only
// Wire layout and message semantics: iPhoneMirror v1.8.3 src/WirelessHost/IpcProtocol.h.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace MirrorDesk
{
    internal sealed class AirPlayHostPipe : IDisposable
    {
        const int HeaderSize = 392, MaxPayload = 64 * 1024 * 1024;
        const int MaxPackets = 48, MaxBytes = MaxPayload + 1024 * 1024;
        enum Kind : ushort { Ready=1, Connected=2, Disconnected=3, Video=4, Audio=5, Volume=15 }

        sealed class Packet
        {
            internal readonly Kind Type;
            internal readonly byte[] Header = new byte[HeaderSize];
            internal byte[] Body;
            internal int Length;
            internal bool Media { get { return Type==Kind.Video || Type==Kind.Audio; } }
            internal Packet(Kind type, IntPtr name, IntPtr device)
            {
                Type=type;
                Set32(Header,0,0x50414d49);
                Set16(Header,4,7);
                Set16(Header,6,(int)type);
                CopyText(Header,104,64,device);
                CopyText(Header,168,128,name);
            }
            internal bool SameSource(Packet other)
            {
                int start=Header[104]==0 && other.Header[104]==0 ? 168 : 104;
                int limit=start==104 ? 168 : 296;
                for (int i=start;i<limit;i++) {
                    if (Header[i]!=other.Header[i]) return false;
                    if (Header[i]==0) break;
                }
                return true;
            }
        }

        readonly NamedPipeClientStream stream;
        readonly object gate = new object();
        readonly LinkedList<Packet> queue = new LinkedList<Packet>();
        readonly List<byte[]> videoBuffers = new List<byte[]>(3);
        readonly Thread writer;
        bool closing;
        volatile bool broken;
        int bytes, disposed;
        long sequence;

        AirPlayHostPipe(NamedPipeClientStream stream)
        {
            this.stream=stream;
            writer=new Thread(WritePackets) { IsBackground=true, Name="WeiMirror AirPlay frames" };
            writer.Start();
            // Drain reverse-direction status even in mirror-only mode, and notice a closed parent at idle.
            ObserveParent();
        }
        public bool Broken { get { return broken; } }
        [DllImport("kernel32.dll",ExactSpelling=true,CharSet=CharSet.Unicode,SetLastError=true)]
        static extern SafePipeHandle CreateFileW(string name,uint access,uint sharing,IntPtr security,
            uint creation,uint attributes,IntPtr template);

        public static AirPlayHostPipe Connect(string fullLocalPipeName)
        {
            const string prefix=@"\\.\pipe\";
            if (String.IsNullOrEmpty(fullLocalPipeName) || !fullLocalPipeName.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("需要本机接收通道。");
            string name=fullLocalPipeName.Substring(prefix.Length);
            if (name.Length==0 || name.IndexOfAny(new[]{'\\','/',':'})>=0) throw new ArgumentException("本机接收通道名称无效。");
            // Match the native hub: try opening the already-created instance first. A system
            // pipe-availability probe can report it missing even while CreateFileW succeeds.
            const uint readWrite=0xc0000000, openExisting=3, overlapped=0x40000000;
            const uint sqosPresent=0x100000, identification=0x10000;
            var limit=Stopwatch.StartNew();
            while (true) {
                var handle=CreateFileW(fullLocalPipeName,readWrite,0,IntPtr.Zero,openExisting,
                    overlapped|sqosPresent|identification,IntPtr.Zero);
                if (!handle.IsInvalid) {
                    try {
                        var client=new NamedPipeClientStream(PipeDirection.InOut,true,true,handle);
                        try { return new AirPlayHostPipe(client); }
                        catch { client.Dispose(); throw; }
                    } catch { handle.Dispose(); throw; }
                }
                int error=Marshal.GetLastWin32Error();
                handle.Dispose();
                if ((error!=2 && error!=231) || limit.ElapsedMilliseconds>=6000)
                    throw new IOException("无法连接本机投屏通道。",new Win32Exception(error));
                Thread.Sleep(40);
            }
        }

        static void Set16(byte[] h,int i,int value)
        {
            h[i]=(byte)value; h[i+1]=(byte)(value>>8);
        }
        static void Set32(byte[] h,int i,int value)
        {
            Set16(h,i,value); Set16(h,i+2,value>>16);
        }
        static void Set64(byte[] h,int i,long value)
        {
            Set32(h,i,(int)value); Set32(h,i+4,(int)(value>>32));
        }
        static void CopyText(byte[] dest,int start,int capacity,IntPtr text)
        {
            if (text==IntPtr.Zero) return;
            for (int i=0;i<capacity-1;i++) {
                byte b=Marshal.ReadByte(text,i);
                if (b==0) break;
                dest[start+i]=b;
            }
        }
        public void Ready() { Send(new Packet(Kind.Ready,IntPtr.Zero,IntPtr.Zero),IntPtr.Zero,0); }
        public void Connected(IntPtr name,IntPtr device) { Send(new Packet(Kind.Connected,name,device),IntPtr.Zero,0); }
        public void Disconnected(IntPtr name,IntPtr device) { Send(new Packet(Kind.Disconnected,name,device),IntPtr.Zero,0); }
        public void Volume(IntPtr name,IntPtr device,float db)
        {
            if (float.IsNaN(db) || float.IsInfinity(db) || device==IntPtr.Zero || Marshal.ReadByte(device)==0) return;
            var p=new Packet(Kind.Volume,name,device);
            Set64(p.Header,92,BitConverter.DoubleToInt64Bits(Math.Max(0,Math.Min(1,(db+30.0)/30.0))));
            Set32(p.Header,100,1); // MirroredStream
            Send(p,IntPtr.Zero,0);
        }
        public void Video(IntPtr name,IntPtr device,int width,int height,int yPitch,int uPitch,int vPitch,
            int yLen,int uLen,int vLen,IntPtr data,int length)
        {
            if (data==IntPtr.Zero || length<=0 || length>MaxPayload || width<=0 || height<=0 || width>8192 || height>8192) return;
            int cw=(width+1)/2, ch=(height+1)/2;
            if (yPitch<width || uPitch<cw || vPitch<cw || yLen<(long)yPitch*(height-1)+width ||
                uLen<(long)uPitch*(ch-1)+cw || vLen<(long)vPitch*(ch-1)+cw || (long)yLen+uLen+vLen>length) return;
            var p=new Packet(Kind.Video,name,device);
            Set32(p.Header,12,width); Set32(p.Header,16,height);
            Set32(p.Header,20,yPitch); Set32(p.Header,24,uPitch); Set32(p.Header,28,vPitch);
            Set32(p.Header,32,yLen); Set32(p.Header,36,uLen); Set32(p.Header,40,vLen);
            Send(p,data,length);
        }
        public void Audio(IntPtr name,IntPtr device,int sampleRate,int channels,int bits,IntPtr data,int length)
        {
            if (data==IntPtr.Zero || length<=0 || length>MaxPayload || sampleRate<=0 || channels<=0 || channels>65535 || bits<=0 || bits>65535) return;
            var p=new Packet(Kind.Audio,name,device);
            Set32(p.Header,44,sampleRate); Set16(p.Header,48,channels); Set16(p.Header,50,bits);
            Send(p,data,length);
        }

        byte[] RentVideo(int size)
        {
            for (int i=0;i<videoBuffers.Count;i++) if (videoBuffers[i].Length==size) {
                var data=videoBuffers[i]; videoBuffers.RemoveAt(i); return data;
            }
            return new byte[size];
        }
        void Release(Packet p)
        {
            bytes-=HeaderSize+p.Length;
            if (p.Type==Kind.Video && p.Body!=null && p.Body.Length<=4*1024*1024 && videoBuffers.Count<3) videoBuffers.Add(p.Body);
            p.Body=null;
        }
        void Send(Packet p,IntPtr data,int length)
        {
            lock (gate) {
                if (closing) return;
                if (p.Type==Kind.Video) {
                    for (var node=queue.First;node!=null;node=node.Next)
                        if (node.Value.Type==Kind.Video && node.Value.SameSource(p)) {
                            Release(node.Value); queue.Remove(node); break;
                        }
                }
                while (queue.Count>=MaxPackets || (long)bytes+HeaderSize+length>MaxBytes) {
                    var drop=queue.First;
                    while (drop!=null && !drop.Value.Media) drop=drop.Next;
                    if (drop==null) {
                        if (!p.Media) { broken=true; closing=true; Monitor.PulseAll(gate); }
                        return;
                    }
                    Release(drop.Value); queue.Remove(drop);
                }
                if (length>0) {
                    p.Body=p.Type==Kind.Video ? RentVideo(length) : new byte[length];
                    Marshal.Copy(data,p.Body,0,length);
                }
                p.Length=length;
                Set32(p.Header,8,length); Set64(p.Header,52,++sequence);
                if (p.Type==Kind.Volume) Set64(p.Header,60,sequence);
                queue.AddLast(p);
                bytes+=HeaderSize+length;
                Monitor.Pulse(gate);
            }
        }

        void WritePackets()
        {
            try {
                while (true) {
                    Packet p;
                    lock (gate) {
                        while (!closing && queue.Count==0) Monitor.Wait(gate);
                        if (closing) return;
                        p=queue.First.Value; queue.RemoveFirst();
                    }
                    try {
                        stream.WriteAsync(p.Header,0,p.Header.Length).GetAwaiter().GetResult();
                        for (int pos=0;pos<p.Length;) {
                            int n=Math.Min(1024*1024,p.Length-pos);
                            stream.WriteAsync(p.Body,pos,n).GetAwaiter().GetResult();
                            pos+=n;
                        }
                    } finally { lock(gate) Release(p); }
                }
            } catch (Exception ex) when (ex is IOException || ex is InvalidOperationException || ex is OperationCanceledException) { Stop(); }
        }
        async void ObserveParent()
        {
            try {
                var buffer=new byte[4096];
                while (await stream.ReadAsync(buffer,0,buffer.Length).ConfigureAwait(false)>0) {}
            } catch (Exception ex) when (ex is IOException || ex is InvalidOperationException || ex is OperationCanceledException) {}
            finally { Stop(); }
        }
        void Stop()
        {
            lock (gate) { broken=true; closing=true; Monitor.PulseAll(gate); }
        }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed,1)!=0) return;
            Stop();
            stream.Dispose();
            writer.Join(1500);
            lock (gate) { queue.Clear(); videoBuffers.Clear(); }
        }
    }
}
