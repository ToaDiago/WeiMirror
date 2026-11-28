using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MirrorDesk
{
    public sealed class RuntimeProgress
    {
        public int Percent { get; private set; }
        public string Message { get; private set; }
        internal RuntimeProgress(int percent, string message) { Percent = percent; Message = message; }
    }

    public static class RuntimePack
    {
        public const string ZipName = "iPhoneMirror-v1.8.3-win-x64.zip";
        public const string ZipUrl = "https://github.com/RayrenSX/iPhoneMirror/releases/download/v1.8.3/" + ZipName;
        public const string ZipSha256 = "97c5ded39a9437069cb24fd4ee59fe9775c1ebcf2700e3fd6305c9e424396bf0";
        public const long ZipBytes = 338966817;
        const int BufferSize = 131072;
        const string Driver = "iPhoneMirror.Driver.exe";
        const string Switch = "iPhoneMirror.UsbConfigurationSwitch.exe";
        const string Core = "iPhoneMirror.Core.dll";
        const string Wireless = "Wireless/";
        const string Host = Wireless + "iPhoneMirror.WirelessHost.exe";
        const string Changelog = "CHANGELOG.md";
        const string SelectedPointer = "selected-v1.8.3.txt";
        const string ManagedPointer = "pack-v1.8.3.txt";
        const string ExternalPointer = "external-v1.8.3.txt";

        static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WeiMirror");
        public static string ComponentDownloadDirectory { get { return Path.Combine(DataDir, "packs"); } }
        internal static string PackageDirectory(Guid id)
        {
            return Path.Combine(ComponentDownloadDirectory, "v1.8.3-" + id.ToString("N"));
        }
        static readonly string[] Native = { Core, Switch, "libusb0.dll", "libusb-1.0.dll", "msvcp140.dll", "vcruntime140.dll", "vcruntime140_1.dll" };
        // Published in the tagged src/App/Services/RuntimeBinaryIntegrity.cs.
        static readonly Dictionary<string, string> WirelessHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "airplay2dll.dll", "4a534dacac5cd36f9aaa6e016db75db899e0678ffb8b0c191cabf230a2002bd9" },
            { "avcodec-58.dll", "e785b030667c0f4709ba299cb494d405e58bce6e3035d441832d35715fbadc3c" },
            { "avutil-56.dll", "33e0a0a733302bbb09456a6e6c4b5036278f0a6d95a0233ff7edeb33b59ee05d" },
            { "dnssd.dll", "003eeb7ea109df21e62d236e24937971bd9738b6648df81f6effb810524d92bd" },
            { "swresample-3.dll", "bb8defd0517fd12379eddb0b20b0998356bfd36862b6180a9d2315f3e6128117" },
            { "swscale-5.dll", "db405507989aed9915287c79a34fd6421fc7683a98edebcf4ba370a917eef311" }
        };
        static readonly HashSet<string> Files = new HashSet<string>(Native.Concat(new[] {
            Driver, Host,
            Wireless + "msvcp140.dll", Wireless + "vcruntime140.dll", Wireless + "vcruntime140_1.dll"
        }).Concat(WirelessHashes.Keys.Select(name => Wireless + name)), StringComparer.OrdinalIgnoreCase);
        static readonly HashSet<string> SupportFiles = new HashSet<string>(new[] {
            "LICENSE", Changelog, "THIRD_PARTY_NOTICES.md", "DRIVER_DEPENDENCIES.md", "AppleMobileDeviceSupport64.msi"
        }, StringComparer.OrdinalIgnoreCase);

        // Existing v1.0 installations may still have separate pack and external pointers.
        public static string Find()
        {
            foreach (string candidate in new[] { ReadPointer(SelectedPointer), ReadPointer(ManagedPointer), ReadPointer(ExternalPointer),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "iPhoneMirror"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "iPhoneMirror") })
            {
                if (HasLayout(candidate)) return Path.GetFullPath(candidate);
            }
            return null;
        }

        public static string UseDirectory(string path, CancellationToken cancel = default(CancellationToken))
        {
            cancel.ThrowIfCancellationRequested();
            if (String.IsNullOrWhiteSpace(path)) throw new ArgumentException("请选择组件目录。");
            string root = Path.GetFullPath(path);
            if (!HasLayout(root) && Directory.Exists(root))
            {
                var matches = Directory.EnumerateDirectories(root, "iPhoneMirror*", SearchOption.TopDirectoryOnly)
                    .Where(HasLayout).Take(2).ToArray();
                if (matches.Length == 1) root = matches[0];
            }
            Validate(root, true, cancel);
            cancel.ThrowIfCancellationRequested();
            WritePointer(SelectedPointer, root);
            return root;
        }

        // Runs progress reports on the caller's IProgress dispatcher; pass null for an online download.
        public static Task<string> PrepareAsync(string zipPathOrNull, IProgress<RuntimeProgress> progress, CancellationToken cancel)
        {
            return PreparePackageAsync(Guid.NewGuid(), zipPathOrNull, progress, cancel);
        }

        internal static Task<string> PreparePackageAsync(Guid packageId, string zipPathOrNull, IProgress<RuntimeProgress> progress, CancellationToken cancel)
        {
            return Task.Run(() =>
            {
                cancel.ThrowIfCancellationRequested();
                string packs = ComponentDownloadDirectory;
                Directory.CreateDirectory(packs);
                string id = packageId.ToString("N");
                string stage = Path.Combine(packs, ".unpack-" + id);
                string downloaded = null;
                try
                {
                    string zip = zipPathOrNull;
                    if (zip == null)
                    {
                        downloaded = Path.Combine(packs, ".download-" + id + ".zip");
                        Download(downloaded, progress, cancel);
                        zip = downloaded;
                    }
                    Directory.CreateDirectory(stage);
                    using (var source = new FileStream(zip, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize))
                    {
                        VerifyArchive(source, progress, cancel);
                        using (var archive = new ZipArchive(source, ZipArchiveMode.Read, false))
                            Extract(archive, stage, progress, cancel);
                    }
                    Validate(stage, false);
                    cancel.ThrowIfCancellationRequested();
                    string ready = PackageDirectory(packageId);
                    Directory.Move(stage, ready);
                    try { WritePointer(SelectedPointer, ready); }
                    catch { TryDeleteStage(ready); throw; }
                    Report(progress, 100, "投屏组件已就绪");
                    return ready;
                }
                finally
                {
                    if (downloaded != null) TryDeleteFile(downloaded);
                    TryDeleteStage(stage);
                }
            }, cancel);
        }

        static string ReadPointer(string name)
        {
            string file = Path.Combine(DataDir, name);
            try { return File.Exists(file) ? File.ReadAllText(file).Trim() : null; }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        static void WritePointer(string name, string value)
        {
            Directory.CreateDirectory(DataDir);
            string file = Path.Combine(DataDir, name);
            string temp = file + "." + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(temp, value, new UTF8Encoding(false));
                try { File.Move(temp, file); }
                catch (IOException)
                {
                    if (!File.Exists(file)) throw;
                    File.Replace(temp, file, null);
                }
            }
            finally { TryDeleteFile(temp); }
        }

        static bool HasLayout(string root)
        {
            try
            {
                if (String.IsNullOrWhiteSpace(root) || !Path.IsPathRooted(root) || !Directory.Exists(root)) return false;
                if (!Files.All(name => File.Exists(Path.Combine(root, name)))) return false;
                var version = FileVersionInfo.GetVersionInfo(Path.Combine(root, Driver));
                if (version.FileMajorPart == 1 && version.FileMinorPart == 8 && version.FileBuildPart == 3) return true;
                // Older installer apphost file metadata can describe the .NET host. Accept the tagged changelog there.
                string log = Path.Combine(root, Changelog);
                return File.Exists(log) && File.ReadLines(log).Take(40)
                    .FirstOrDefault(line => line.StartsWith("## [") && !line.StartsWith("## [Unreleased]"))?.StartsWith("## [1.8.3]") == true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            catch (ArgumentException) { return false; }
            catch (NotSupportedException) { return false; }
        }

        static void Validate(string root, bool checkWirelessHashes, CancellationToken cancel = default(CancellationToken))
        {
            if (!HasLayout(root))
                throw new InvalidDataException("需要完整的 iPhoneMirror v1.8.3 x64 组件。请选择解压后的目录，或导入官方便携 ZIP。");
            if (checkWirelessHashes) VerifyWirelessDirectory(Path.Combine(root, "Wireless"), cancel);
        }

        internal static void VerifyWirelessDirectory(string directory, CancellationToken cancel)
        {
            foreach (var item in WirelessHashes)
                using (var stream = File.OpenRead(Path.Combine(directory, item.Key)))
                    if (DigestStream(stream, cancel) != item.Value)
                        throw new InvalidDataException("无线组件校验失败（" + item.Key + "），请导入官方 v1.8.3 ZIP。");
        }

        static void Download(string target, IProgress<RuntimeProgress> progress, CancellationToken cancel)
        {
            Report(progress, 0, "正在连接 GitHub 下载组件…");
            // Preserve Windows' system default (0); opt older .NET profiles into TLS 1.2.
            if ((int)ServicePointManager.SecurityProtocol != 0)
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var request = (HttpWebRequest)WebRequest.Create(ZipUrl);
            request.UserAgent = "WeiMirror/" + typeof(RuntimePack).Assembly.GetName().Version.ToString(2);
            request.Timeout = 30000;
            request.ReadWriteTimeout = 60000;
            using (cancel.Register(request.Abort))
            {
                try
                {
                    using (var response = (HttpWebResponse)request.GetResponse())
                    using (var input = response.GetResponseStream())
                    using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize))
                    {
                        if (response.StatusCode != HttpStatusCode.OK || response.ResponseUri.Scheme != Uri.UriSchemeHttps ||
                            (response.ContentLength >= 0 && response.ContentLength != ZipBytes))
                            throw new InvalidDataException("下载服务器返回的内容与官方 v1.8.3 ZIP 不匹配。");
                        byte[] buffer = new byte[BufferSize];
                        long total = 0;
                        int last = -1, read;
                        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            cancel.ThrowIfCancellationRequested();
                            total += read;
                            if (total > ZipBytes) throw new InvalidDataException("下载大小超过官方发布记录。");
                            output.Write(buffer, 0, read);
                            int pct = (int)(total * 75 / ZipBytes);
                            if (pct != last)
                            {
                                last = pct;
                                Report(progress, pct, "正在下载组件：" + (total / 1048576) + " / " + (ZipBytes / 1048576) + " MiB");
                            }
                        }
                        if (total != ZipBytes) throw new EndOfStreamException("下载中断。请重试或选择已下载的官方便携 ZIP。");
                    }
                }
                catch (WebException) { cancel.ThrowIfCancellationRequested(); throw; }
                catch (IOException) { cancel.ThrowIfCancellationRequested(); throw; }
            }
        }

        static void VerifyArchive(Stream source, IProgress<RuntimeProgress> progress, CancellationToken cancel)
        {
            if (source.Length != ZipBytes) throw new InvalidDataException("文件大小与官方便携 ZIP 不匹配，请选择 " + ZipName + "。");
            Report(progress, 76, "正在验证官方 ZIP 的 SHA-256…");
            if (DigestStream(source, cancel) != ZipSha256) throw new InvalidDataException("文件内容与官方 SHA-256 不符，请重新下载。");
            source.Position = 0;
        }

        static void Extract(ZipArchive zip, string stage, IProgress<RuntimeProgress> progress, CancellationToken cancel)
        {
            // Accept either a flat archive or the official single wrapping directory.
            const string officialRoot = "iPhoneMirror-v1.8.3-win-x64/";
            var cores = zip.Entries.Where(e => {
                string name = e.FullName.Replace('\\', '/');
                return String.Equals(name, Core, StringComparison.OrdinalIgnoreCase) ||
                    String.Equals(name, officialRoot + Core, StringComparison.OrdinalIgnoreCase);
            }).ToArray();
            if (cores.Length != 1) throw new InvalidDataException("ZIP 的组件目录不匹配。");
            string coreEntry = cores[0].FullName.Replace('\\', '/');
            string prefix = coreEntry.Substring(0, coreEntry.Length - Core.Length);
            var selected = new List<KeyValuePair<string, ZipArchiveEntry>>();
            foreach (var entry in zip.Entries)
            {
                string full = entry.FullName.Replace('\\', '/');
                if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || full.EndsWith("/")) continue;
                string relative = full.Substring(prefix.Length);
                if (!Files.Contains(relative) && !SupportFiles.Contains(relative) && !relative.StartsWith("licenses/", StringComparison.OrdinalIgnoreCase) &&
                    !relative.StartsWith(Wireless + "licenses/", StringComparison.OrdinalIgnoreCase)) continue;
                if (relative.Split('/').Any(s => s.Length == 0 || s == "." || s == ".." || s.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
                    throw new InvalidDataException("ZIP 包含无法提取的组件路径。");
                selected.Add(new KeyValuePair<string, ZipArchiveEntry>(relative, entry));
            }
            long sum = selected.Sum(p => p.Value.Length);
            long handled = 0;
            byte[] buffer = new byte[BufferSize];
            int last = -1;
            string safePrefix = Path.GetFullPath(stage) + Path.DirectorySeparatorChar;
            foreach (var item in selected)
            {
                string path = Path.GetFullPath(Path.Combine(stage, item.Key));
                if (!path.StartsWith(safePrefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("组件路径超出目标目录。");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using (var input = item.Value.Open())
                using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize))
                {
                    int count;
                    while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
                    {
                        cancel.ThrowIfCancellationRequested();
                        output.Write(buffer, 0, count);
                        handled += count;
                        int percent = 80 + (int)(handled * 19 / Math.Max(1, sum));
                        if (percent != last) { last = percent; Report(progress, percent, "正在准备 USB 与 AirPlay 组件…"); }
                    }
                }
            }
        }

        // The native core finds its USB configuration helper next to the process image.
        // Place this application and the helper's dependencies together in user-owned storage.
        public static string PrepareHost(string engineRoot, string currentExe)
        {
            Validate(engineRoot, true);
            string source = Path.GetFullPath(currentExe);
            var files = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>("WeiMirror.exe", source) };
            foreach (string name in Native) files.Add(new KeyValuePair<string, string>(name, Path.Combine(engineRoot, name)));
            if (File.Exists(source + ".config")) files.Add(new KeyValuePair<string, string>("WeiMirror.exe.config", source + ".config"));
            var hashes = files.Select(p => DigestFile(p.Value)).ToArray();
            int bundledCore = NativeCoreRuntime.BundledHash == null ? -1 : files.FindIndex(p => p.Key == Core);
            if (bundledCore >= 0) hashes[bundledCore] = NativeCoreRuntime.BundledHash;
            string key;
            using (var hash = SHA256.Create()) key = Hex(hash.ComputeHash(Encoding.UTF8.GetBytes(String.Join("|", hashes))));
            string hostRoot = Path.Combine(DataDir, "hosts");
            Directory.CreateDirectory(hostRoot);
            for (int generation = 0; generation < 100; generation++)
            {
                string final = Path.Combine(hostRoot, generation == 0 ? key : key + "-" + generation);
                if (Directory.Exists(final))
                {
                    if (Matches(final, files, hashes)) return Path.Combine(final, "WeiMirror.exe");
                    continue; // Recover from a damaged cache without altering another running worker.
                }
                string stage = Path.Combine(hostRoot, ".prepare-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(stage);
                try
                {
                    for (int i = 0; i < files.Count; i++)
                    {
                        string dest = Path.Combine(stage, files[i].Key);
                        if (i == bundledCore) NativeCoreRuntime.WriteCore(dest);
                        else File.Copy(files[i].Value, dest);
                        if (DigestFile(dest) != hashes[i]) throw new IOException("准备投屏进程期间文件发生变化，请重试。");
                    }
                    try { Directory.Move(stage, final); return Path.Combine(final, "WeiMirror.exe"); }
                    catch (IOException) { if (!Directory.Exists(final)) throw; }
                }
                finally { TryDeleteStage(stage); }
                if (Matches(final, files, hashes)) return Path.Combine(final, "WeiMirror.exe");
            }
            throw new IOException("无法在本机缓存中建立完整的投屏进程。");
        }

        static bool Matches(string dir, List<KeyValuePair<string, string>> files, string[] hashes)
        {
            for (int i = 0; i < files.Count; i++)
            {
                string candidate = Path.Combine(dir, files[i].Key);
                if (!File.Exists(candidate) || DigestFile(candidate) != hashes[i]) return false;
            }
            return true;
        }
        static string DigestFile(string path)
        {
            using (var stream = File.OpenRead(path))
                return DigestStream(stream, CancellationToken.None);
        }
        static string DigestStream(Stream stream, CancellationToken cancel)
        {
            using (var hash = SHA256.Create())
            {
                if (!cancel.CanBeCanceled) return Hex(hash.ComputeHash(stream));
                byte[] buffer = new byte[BufferSize];
                while (true)
                {
                    cancel.ThrowIfCancellationRequested();
                    int count = stream.Read(buffer, 0, buffer.Length);
                    if (count == 0) break;
                    hash.TransformBlock(buffer, 0, count, buffer, 0);
                }
                hash.TransformFinalBlock(buffer, 0, 0);
                return Hex(hash.Hash);
            }
        }
        static string Hex(byte[] bytes) { return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant(); }
        static void Report(IProgress<RuntimeProgress> progress, int percent, string message)
        { if (progress != null) progress.Report(new RuntimeProgress(percent, message)); }
        static void TryDeleteFile(string file)
        {
            try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        static void TryDeleteStage(string path)
        {
            // Only the freshly assigned, private GUID staging directories are passed here.
            try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
