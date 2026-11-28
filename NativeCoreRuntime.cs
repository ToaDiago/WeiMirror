// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.IO;
using System.Security.Cryptography;

namespace MirrorDesk
{
    // The patched audio core lives in the immutable per-build worker cache.
    internal static class NativeCoreRuntime
    {
        internal const string FileName = "iPhoneMirror.Core.dll";
        const string ResourceName = "WeiMirror.NativeCore.dll";
        static readonly Lazy<string> bundledHash = new Lazy<string>(() => {
            using (var stream = typeof(NativeCoreRuntime).Assembly.GetManifestResourceStream(ResourceName))
                return stream == null ? null : Digest(stream);
        });

        internal static string BundledHash { get { return bundledHash.Value; } }

        static string Digest(Stream stream)
        {
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        internal static void WriteCore(string destination)
        {
            using (var input = typeof(NativeCoreRuntime).Assembly.GetManifestResourceStream(ResourceName)) {
                if (input == null) throw new InvalidDataException("程序缺少音频接收核心，请重新构建或下载微映。");
                using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    input.CopyTo(output);
            }
        }

        internal static string ResolveLibrary(string engineDirectory)
        {
            // Source-only regression harnesses have no bundled core and use
            // an isolated ABI fixture supplied by the harness.
            if (BundledHash == null) return Path.Combine(engineDirectory, FileName);
            string path = Path.Combine(Path.GetDirectoryName(typeof(NativeCoreRuntime).Assembly.Location), FileName);
            if (File.Exists(path))
                using (var input = File.OpenRead(path))
                    if (Digest(input) == BundledHash) return path;
            throw new InvalidDataException("投屏工作目录中的音频核心不完整，请关闭后重新打开微映。");
        }
    }
}
