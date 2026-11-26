using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using MirrorDesk;
internal class RuntimeTests {
    static readonly Type Pack = typeof(RuntimePack);
    const BindingFlags Flags = BindingFlags.Static | BindingFlags.NonPublic;
    static void Check(bool x, string test) { if(!x) throw new Exception(test); Console.WriteLine("PASS " + test); }
    static void Put(ZipArchive z, string name, string value) { using(var w = new StreamWriter(z.CreateEntry(name).Open())) w.Write(value); }
    sealed class CancelOnRead : MemoryStream {
        readonly CancellationTokenSource signal;
        internal CancelOnRead(CancellationTokenSource signal) : base(new byte[256 * 1024]) { this.signal = signal; }
        public override int Read(byte[] buffer, int offset, int count) {
            int result = base.Read(buffer,offset,count);
            signal.Cancel();
            return result;
        }
    }
    static int Main() {
        try { Run(); Console.WriteLine("Runtime checks finished"); return 0; }
        catch (Exception ex) { Console.Error.WriteLine(ex.GetType().Name + ": " + ex.Message); return 1; }
    }
    static void Run() {
        string home=Path.Combine(Environment.CurrentDirectory, "work", "runtime-fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(home);
        try {
            Pack.GetField("DataDir",Flags).SetValue(null,Path.Combine(home,"Data"));
            string stage = Path.Combine(home,"engine");
            Directory.CreateDirectory(stage);
            var required=(HashSet<string>)Pack.GetField("Files",Flags).GetValue(null);
            var hashes=(Dictionary<string,string>)Pack.GetField("WirelessHashes",Flags).GetValue(null);
            using(var bytes=new MemoryStream()){
                using(var z=new ZipArchive(bytes, ZipArchiveMode.Create,true)){
                    string prefix = "iPhoneMirror-v1.8.3-win-x64/";
                    foreach(var rel in required) Put(z,prefix + rel,"Test-only synthetic file: " + rel);
                    Put(z,prefix+"CHANGELOG.md","## [Unreleased]\n## [1.8.3] - test\n");
                    Put(z,prefix+"Wireless/licenses/SOURCE.md","notice");
                    Put(z,prefix+"licenses/text.txt","notice");
                    Put(z,prefix+"iPhoneMirror.exe","ignored");
                    Put(z,prefix+"AppleMobileDeviceSupport64.msi","optional");
                    Put(z,prefix+"tools/ffmpeg/ffmpeg.exe","ignored");
                    Put(z,prefix+"tools/dummy/iPhoneMirror.Core.dll","ignored");
                }
                bytes.Position=0;
                using(var z=new ZipArchive(bytes,ZipArchiveMode.Read,true))
                    Pack.GetMethod("Extract",Flags).Invoke(null,new object[]{z,stage,null,CancellationToken.None});
            }
            Check(required.All(x=>File.Exists(Path.Combine(stage,x))) && File.Exists(Path.Combine(stage,@"Wireless\licenses\SOURCE.md")), "required files plus licences extracted from wrapped ZIP");
            Check(File.Exists(Path.Combine(stage,"AppleMobileDeviceSupport64.msi")),"optional official Apple MSI retained");
            Check(!File.Exists(Path.Combine(stage,"iPhoneMirror.exe"))&&!Directory.Exists(Path.Combine(stage,"tools")),"upstream GUI omitted");
            try { RuntimePack.UseDirectory(stage); throw new Exception("bad DLL accepted"); } catch (InvalidDataException) { Check(true,"invalid wireless codec hashes rejected"); }
            // QA fixtures never enter the deliverable. Permit the exact synthetic bytes for subsequent path tests.
            foreach(var name in hashes.Keys.ToArray()) using(var h=SHA256.Create()) using(var f=File.OpenRead(Path.Combine(stage,"Wireless",name)))
                hashes[name]=BitConverter.ToString(h.ComputeHash(f)).Replace("-","").ToLowerInvariant();
            Check(RuntimePack.UseDirectory(stage)==stage && RuntimePack.Find()==stage,"select and rediscover nested external package");
            string damaged = Path.Combine(home, "older-cached-engine");
            Directory.CreateDirectory(damaged);
            foreach (string rel in required.Concat(new[] { "CHANGELOG.md" })) {
                var path = Path.Combine(damaged,rel);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.Copy(Path.Combine(stage,rel),path);
            }
            File.WriteAllText(Path.Combine(damaged,"Wireless","dnssd.dll"),"Damaged cache");
            File.WriteAllText(Path.Combine(home,"Data","pack-v1.8.3.txt"), damaged);
            Check(RuntimePack.UseDirectory(stage)==stage && RuntimePack.Find()==stage,"explicit repair is remembered across restarts despite an older managed cache");
            using (var cancel = new CancellationTokenSource())
            using (var stream = new CancelOnRead(cancel)) {
                try { Pack.GetMethod("DigestStream",Flags).Invoke(null,new object[]{stream,cancel.Token}); throw new Exception("hash ignored mid-read cancellation"); }
                catch (TargetInvocationException e) { Check(e.InnerException is OperationCanceledException,"directory verification observes cancellation during hashing"); }
                try { RuntimePack.UseDirectory(damaged,cancel.Token); throw new Exception("cancelled selection accepted"); }
                catch (OperationCanceledException) { Check(RuntimePack.Find()==stage,"cancelled selection leaves previous verified directory active"); }
            }
            string source = Path.Combine(home,"myapp.exe");
            File.WriteAllText(source,"fictional exe");
            string target = RuntimePack.PrepareHost(stage,source);
            Check(File.Exists(target)&&File.Exists(Path.Combine(Path.GetDirectoryName(target),"iPhoneMirror.UsbConfigurationSwitch.exe")),"worker adjacent to switch");
            Check(RuntimePack.PrepareHost(stage,target)==target,"running worker is idempotent");
            File.Delete(Path.Combine(Path.GetDirectoryName(target),"libusb0.dll"));
            string repaired=RuntimePack.PrepareHost(stage,source);
            Check(repaired!=target && RuntimePack.PrepareHost(stage,repaired)==repaired,"damaged cache recovers");
            try { RuntimePack.PrepareAsync(source,null,CancellationToken.None).GetAwaiter().GetResult(); throw new Exception("bad zip accepted"); } catch(InvalidDataException) { Check(true,"incorrect ZIP byte size rejected"); }
            Check(RuntimePack.Find()==stage && !Directory.EnumerateFileSystemEntries(Path.Combine(home,"Data","packs")).Any(),"failed import leaves previous runtime intact / staging cleaned");
            using(var zbytes=new MemoryStream()){
                using(var z=new ZipArchive(zbytes,ZipArchiveMode.Create,true)){
                    Put(z,"iPhoneMirror.Core.dll","x");
                    Put(z,"licenses/../../escape","bad");
                }
                zbytes.Position=0;
                using(var z=new ZipArchive(zbytes,ZipArchiveMode.Read,true)){
                    try { Pack.GetMethod("Extract",Flags).Invoke(null,new object[]{z,stage,null,CancellationToken.None}); throw new Exception("bad path accepted"); }
                    catch(TargetInvocationException e) { Check(e.InnerException is InvalidDataException,"traversal rejected"); }
                }
            }
        } finally { Directory.Delete(home,true); }
    }
}
