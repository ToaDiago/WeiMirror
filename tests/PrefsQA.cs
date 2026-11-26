using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using MirrorDesk;

internal static class PrefsQA
{
    static int checks;
    static void Check(bool passed, string label)
    {
        if (!passed) throw new InvalidOperationException(label);
        checks++;
        Console.WriteLine("PASS " + label);
    }

    static bool Defaults(UserPreferences p)
    {
        return !p.Wireless && !p.AlwaysOnTop && p.PlayAudio && p.QualityIndex == 0 && p.ReceiverName == "WeiMirror";
    }

    static bool Custom(UserPreferences p)
    {
        return p.Wireless && p.AlwaysOnTop && !p.PlayAudio && p.QualityIndex == 2 && p.ReceiverName == "卧室 = iPad";
    }

    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] static extern bool TerminateProcess(IntPtr process, uint result);

    static int Main()
    {
        int result = 0;
        try { Run(); }
        catch (Exception ex) { result = 1; Console.Error.WriteLine(ex.GetBaseException().Message); }
        Console.Out.Flush();
        Console.Error.Flush();
        // This isolated Windows test host can hang during CLR teardown after parallel I/O.
        // The fixture has already been removed and all checks completed at this point.
        TerminateProcess(GetCurrentProcess(), (uint)result);
        return result;
    }

    static void Run()
    {
        string dir = Path.Combine(Environment.CurrentDirectory, "work", "prefs-fixture-" + Guid.NewGuid().ToString("N"));
        string file = Path.Combine(dir, "preferences.ini");
        var store = new PreferencesStore(file);
        try
        {
            Check(Defaults(store.Load()) && !Directory.Exists(dir), "missing configuration loads defaults without creating a directory");
            var custom = new UserPreferences { Wireless=true, AlwaysOnTop=true, PlayAudio=false, QualityIndex=2, ReceiverName="卧室 = iPad" };
            Check(store.TrySave(custom), "first write creates the directory and the configuration file");
            Check(Custom(new PreferencesStore(file).Load()), "all five preferences reload after restart, including Unicode and equals sign");
            File.AppendAllText(file, "\r\nfuture_option=on\r\nmalformed line\r\n");
            Check(Custom(store.Load()), "unknown settings and malformed lines leave existing fields intact");
            File.WriteAllText(file, "transport=airplay\naudio=false\nname=旧配置\n", new UTF8Encoding(true));
            var old = store.Load();
            Check(old.Wireless && !old.PlayAudio && old.ReceiverName == "旧配置" && !old.AlwaysOnTop && old.QualityIndex == 0,
                "missing fields in older BOM-encoded configuration get defaults");
            var previousHeights = new[] {1080,720,540};
            var previousRates = new[] {60,30,30};
            for (int index=0; index<previousHeights.Length; index++) {
                File.WriteAllText(file,"format=1\nquality="+index+"\n");
                var previous=WirelessProfile.Choices[store.Load().QualityIndex];
                Check(previous.Height==previousHeights[index] && previous.Fps==previousRates[index],
                    "1.2 saved profile still maps to "+previous);
            }
            const string placement="1|DISPLAY1|210|110|980|560";
            Check(store.TrySave(new UserPreferences { QualityIndex=3, PreviewLayout=placement }) &&
                  store.Load().QualityIndex==3 && store.Load().PreviewLayout==placement &&
                  WirelessProfile.Choices[3].Height==720 && WirelessProfile.Choices[3].Fps==60,
                  "new 720p60 profile and preview placement reload");
            Check(store.TrySave(new UserPreferences { PreviewLayout="bad\nquality=3" }) &&
                  store.Load().QualityIndex==0 && String.IsNullOrEmpty(store.Load().PreviewLayout),
                  "invalid placement cannot inject another preference");
            Check(store.TrySave(new UserPreferences { PreviewLayout=new String('x',513) }) &&
                  String.IsNullOrEmpty(store.Load().PreviewLayout),"oversized placement is ignored");
            File.WriteAllText(file, "transport=other\nquality=9000\naudio=sure\ntopmost=bad\nname=[broken]\n");
            Check(Defaults(store.Load()), "invalid individual values fall back safely");
            File.WriteAllBytes(file, new byte[] { 0xff, 0x41, 0x81 });
            Check(Defaults(store.Load()), "malformed UTF-8 returns usable defaults");
            File.WriteAllBytes(file, new byte[32769]);
            Check(Defaults(store.Load()), "oversized input is ignored");
            Check(store.TrySave(custom) && Custom(store.Load()), "valid settings replace a corrupt file atomically");
            using (var readLock = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                Check(!store.TrySave(new UserPreferences()), "locked destination reports save failure");
            Check(Custom(store.Load()) && Directory.GetFiles(dir).Length == 1, "locked save leaves previous settings and removes temporary file");
            var bad = new UserPreferences { ReceiverName = "name\ntopmost=true", QualityIndex = -1 };
            Check(store.TrySave(bad) && Defaults(store.Load()), "invalid name or preset cannot corrupt another field");
            File.Delete(file);
            Parallel.For(0, 20, i => {
                var io = new PreferencesStore(file);
                var value = i % 2 == 0 ? custom : new UserPreferences();
                if (!io.TrySave(value)) throw new InvalidOperationException("parallel write failed");
                var actual = io.Load();
                if (!Defaults(actual) && !Custom(actual)) throw new InvalidOperationException("torn read");
            });
            Check(Directory.GetFiles(dir).Length == 1, "concurrent first writes and replacements publish a complete snapshot");
            Console.WriteLine("Preference checks: " + checks);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                foreach (string item in Directory.GetFiles(dir)) File.Delete(item);
                Directory.Delete(dir);
            }
        }
    }
}
