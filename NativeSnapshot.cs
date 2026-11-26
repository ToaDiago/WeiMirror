// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace MirrorDesk
{
    internal sealed partial class MirrorCore
    {
        // CoreApi.h v1.8.3: full-size, top-down, straight-alpha BGRA8 when both limits are zero.
        [StructLayout(LayoutKind.Sequential)]
        private struct VideoFrameInfo
        {
            public uint Size, Version, Width, Height, Stride, PixelFormat;
            public long Timestamp100Ns;
        }

        [DllImport(CoreLibrary, ExactSpelling=true, CallingConvention=Convention)]
        private static extern int im_session_copy_latest_video_frame(ulong handle, ref VideoFrameInfo info,
            [Out] byte[] buffer, ref uint capacity, uint maxWidth, uint maxHeight);

        [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true, ExactSpelling=true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool MoveFileExW(string source, string target, uint flags);

        private void CheckSnapshotAvailable()
        {
            var status = GetStatus();
            if (status.ProtectedVideo)
                throw new InvalidOperationException("当前画面可能受保护，无法保存截图。");
            if (status.State != 4 || status.Frames == 0 || status.Width == 0 || status.Height == 0)
                throw new InvalidOperationException("当前没有可保存的画面，请等投屏画面出现后重试。");
        }

        private static void ValidateSnapshotInfo(VideoFrameInfo info, uint size)
        {
            if (info.Version != SupportedVersion || info.PixelFormat != 1 || info.Width == 0 || info.Height == 0 ||
                (ulong)info.Width * 4 > info.Stride || (ulong)info.Stride * info.Height > size ||
                size > int.MaxValue)
                throw new InvalidDataException("接收组件返回的画面尺寸或像素格式无效。");
        }

        public Bitmap CaptureSnapshot()
        {
            var info = new VideoFrameInfo { Size=(uint)Marshal.SizeOf(typeof(VideoFrameInfo)), Version=SupportedVersion };
            byte[] pixels = null;
            lock (gate)
            {
                EnsureOpen();
                CheckSnapshotAvailable();
                // The source may rotate between the size query and the actual copy.
                for (int attempt=0; ; attempt++)
                {
                    uint size = pixels == null ? 0U : (uint)pixels.Length;
                    int result = im_session_copy_latest_video_frame(session, ref info, pixels, ref size, 0, 0);
                    if (result == -3)
                    {
                        ValidateSnapshotInfo(info, size);
                        if (attempt >= 3) throw new InvalidOperationException("画面尺寸正在变化，请再截一次。");
                        pixels = new byte[checked((int)size)];
                        continue;
                    }
                    Check(result, "获取投屏画面");
                    ValidateSnapshotInfo(info, size);
                    if (pixels == null || size > pixels.Length)
                        throw new InvalidDataException("接收组件返回的画面数据不完整。");
                    break;
                }
                CheckSnapshotAvailable();
            }

            int width=(int)info.Width, height=(int)info.Height, stride=(int)info.Stride;
            var image = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            try
            {
                var data = image.LockBits(new Rectangle(0,0,width,height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try
                {
                    int rowBytes=checked(width*4);
                    if (stride==rowBytes && data.Stride==rowBytes)
                        Marshal.Copy(pixels,0,data.Scan0,checked(rowBytes*height));
                    else
                        for (int y=0; y<height; y++)
                            Marshal.Copy(pixels,checked(y*stride),IntPtr.Add(data.Scan0,checked(y*data.Stride)),rowBytes);
                }
                finally { image.UnlockBits(data); }
                return image;
            }
            catch { image.Dispose(); throw; }
        }

        public static void SaveSnapshot(Bitmap frame, string destination)
        {
            if (frame==null) throw new ArgumentNullException(nameof(frame));
            string target=Path.GetFullPath(destination);
            string dir=Path.GetDirectoryName(target);
            string temp=Path.Combine(dir,".weimirror-"+Guid.NewGuid().ToString("N")+".tmp");
            bool replace=File.Exists(target);
            bool ownsTemp=false;
            try
            {
                using (var output=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                {
                    ownsTemp=true;
                    frame.Save(output,ImageFormat.Png);
                    output.Flush(true);
                }
                // Commit the complete PNG on the same volume; preserve an old file if encoding or rename fails.
                const uint replaceExisting=1, writeThrough=8;
                if (!MoveFileExW(temp,target,writeThrough | (replace ? replaceExisting : 0)))
                    throw new IOException("无法保存截图：" + new Win32Exception(Marshal.GetLastWin32Error()).Message);
                ownsTemp=false;
            }
            finally
            {
                if (ownsTemp) File.Delete(temp);
            }
        }
    }
}
