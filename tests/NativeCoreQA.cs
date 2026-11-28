using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;

static class NativeCoreQA
{
    const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static int checks;
    static void Check(bool ok, string message) {
        if (!ok) throw new Exception(message);
        checks++; Console.WriteLine("PASS " + message);
    }
    static string Hash(Stream data) {
        using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
    }
    static string HashFile(string path) { using (var f = File.OpenRead(path)) return Hash(f); }
    static object Call(Type type, string name, params object[] args) { return type.GetMethod(name, Static).Invoke(null, args); }

    static int Main(string[] args) {
        try {
            if (args[0] == "--worker") {
                var assembly = Assembly.LoadFrom(args[1]);
                var runtime = assembly.GetType("MirrorDesk.NativeCoreRuntime", true);
                string core = (string)Call(runtime, "ResolveLibrary", args[2]);
                Check(core == Path.Combine(Path.GetDirectoryName(args[1]), "iPhoneMirror.Core.dll"),
                    "worker resolves the patched adjacent core rather than the old component core");
                var type = assembly.GetType("MirrorDesk.MirrorCore", true);
                using (var instance = (IDisposable)Activator.CreateInstance(type, new object[] { args[2] }))
                    Check(!(bool)type.GetProperty("HasSession").GetValue(instance, null),
                        "bundled ABI 18 core loads, initializes and shuts down without a capture session");
            } else Run(args[0], args[1], args[2]);
            Console.WriteLine(checks + " bundled core checks passed");
            return 0;
        } catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    static void Run(string application, string libusb, string crt) {
        application = Path.GetFullPath(application);
        var assembly = Assembly.LoadFrom(application);
        var pack = assembly.GetType("MirrorDesk.RuntimePack", true);
        var runtime = assembly.GetType("MirrorDesk.NativeCoreRuntime", true);
        string expected;
        using (var stream = assembly.GetManifestResourceStream("WeiMirror.NativeCore.dll")) {
            Check(stream != null, "deliverable contains the rebuilt native audio core");
            expected = Hash(stream);
        }
        string root = Path.Combine(Environment.CurrentDirectory, "work", "native-core-qa-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        pack.GetField("DataDir", Static).SetValue(null, Path.Combine(root, "data"));
        string engine = Path.Combine(root, "original-engine");
        var files = (HashSet<string>)pack.GetField("Files", Static).GetValue(null);
        foreach (string name in files) {
            string path = Path.Combine(engine, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "synthetic upstream file: " + name);
        }
        File.Copy(libusb, Path.Combine(engine, "libusb-1.0.dll"), true);
        foreach (var name in new[] { "vcruntime140.dll", "vcruntime140_1.dll", "msvcp140.dll" })
            File.Copy(Path.Combine(crt, name), Path.Combine(engine, name), true);
        File.WriteAllText(Path.Combine(engine, "CHANGELOG.md"), "## [1.8.3] - fixture\n");
        var wirelessHashes = (Dictionary<string, string>)pack.GetField("WirelessHashes", Static).GetValue(null);
        foreach (var name in wirelessHashes.Keys.ToArray()) wirelessHashes[name] = HashFile(Path.Combine(engine, "Wireless", name));
        string originalHash = HashFile(Path.Combine(engine, "iPhoneMirror.Core.dll"));
        string worker = (string)Call(pack, "PrepareHost", engine, application);
        string localCore = Path.Combine(Path.GetDirectoryName(worker), "iPhoneMirror.Core.dll");
        Check(HashFile(localCore) == expected, "worker cache uses embedded core bytes instead of upstream bytes");
        Check((string)Call(pack, "PrepareHost", engine, worker) == worker,
            "reopening the cached worker does not create a restart loop");
        Check(HashFile(Path.Combine(engine, "iPhoneMirror.Core.dll")) == originalHash,
            "original component core is preserved");
        try {
            Call(runtime, "ResolveLibrary", engine);
            throw new Exception("unprepared app accepted the original core");
        } catch (TargetInvocationException error) {
            Check(error.InnerException is InvalidDataException, "unprepared app cannot silently fall back to the old core");
        }
        using (var child = Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location,
            "--worker \"" + worker + "\" \"" + engine + "\"") {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        })) {
            string output = child.StandardOutput.ReadToEnd();
            string errors = child.StandardError.ReadToEnd();
            child.WaitForExit();
            Console.Write(output);
            if (child.ExitCode != 0) throw new Exception(errors);
            Check(true, "isolated worker successfully used the real patched core");
        }
        File.WriteAllText(localCore, "damaged cached core");
        string repaired = (string)Call(pack, "PrepareHost", engine, application);
        Check(repaired != worker && HashFile(Path.Combine(Path.GetDirectoryName(repaired), "iPhoneMirror.Core.dll")) == expected,
            "damaged cached core is repaired in a new generation");
        Check((string)Call(pack, "PrepareHost", engine, repaired) == repaired,
            "repaired worker cache is stable");
    }
}
