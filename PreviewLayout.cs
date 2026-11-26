using System;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

namespace MirrorDesk
{
    internal sealed class PreviewPlacement
    {
        internal readonly string Display;
        internal readonly Rectangle RelativeBounds;
        internal readonly Size WorkSize;
        internal readonly bool Follow, Maximized;

        internal PreviewPlacement(string display, Rectangle relative, Size work, bool follow, bool maximized)
        {
            Display=display; RelativeBounds=relative; WorkSize=work; Follow=follow; Maximized=maximized;
        }

        internal string Encode()
        {
            return String.Format(CultureInfo.InvariantCulture,"1|{0}|{1}|{2}|{3}|{4}|{5}|{6}|{7}|{8}",
                Convert.ToBase64String(Encoding.UTF8.GetBytes(Display)),Follow?1:0,Maximized?1:0,
                RelativeBounds.X,RelativeBounds.Y,RelativeBounds.Width,RelativeBounds.Height,WorkSize.Width,WorkSize.Height);
        }

        internal static PreviewPlacement Decode(string token)
        {
            if (String.IsNullOrEmpty(token) || token.Length>512) return null;
            var fields=token.Split('|');
            if (fields.Length!=10 || fields[0]!="1" || (fields[2]!="0" && fields[2]!="1") ||
                (fields[3]!="0" && fields[3]!="1")) return null;
            var values=new int[6];
            for (int i=0;i<values.Length;i++)
                if (!Int32.TryParse(fields[i+4],NumberStyles.Integer,CultureInfo.InvariantCulture,out values[i]) ||
                    values[i] < (i<2?-100000:1) || values[i]>100000) return null;
            string display;
            try { display=new UTF8Encoding(false,true).GetString(Convert.FromBase64String(fields[1])); }
            catch (FormatException) { return null; }
            catch (DecoderFallbackException) { return null; }
            return new PreviewPlacement(display,new Rectangle(values[0],values[1],values[2],values[3]),
                new Size(values[4],values[5]),fields[2]=="1",fields[3]=="1");
        }

        internal Rectangle Restore(Rectangle work, Size minimum)
        {
            double cx=(RelativeBounds.Left+RelativeBounds.Width/2.0)/WorkSize.Width;
            double cy=(RelativeBounds.Top+RelativeBounds.Height/2.0)/WorkSize.Height;
            return Centered(work,minimum,RelativeBounds.Size,work.Left+cx*work.Width,work.Top+cy*work.Height);
        }

        internal static Rectangle Centered(Rectangle work, Size minimum, Size size, double cx, double cy)
        {
            int w=Math.Min(work.Width,Math.Max(minimum.Width,size.Width));
            int h=Math.Min(work.Height,Math.Max(minimum.Height,size.Height));
            int x=Math.Min(work.Right-w,Math.Max(work.Left,(int)Math.Round(cx-w/2.0)));
            int y=Math.Min(work.Bottom-h,Math.Max(work.Top,(int)Math.Round(cy-h/2.0)));
            return new Rectangle(x,y,w,h);
        }
    }

    internal sealed partial class PreviewWindow
    {
        const int DisplayChange=0x007e;
        bool followAspect=true, geometryReady, arrangingWindow, manualResize, pendingFit, fitQueued;
        Size sourceSize, dragStartSize, designMinimum;
        double preferredVideoArea;
        string placementToken="";
        PreviewPlacement restored;
        public event EventHandler PreviewLayoutChanged;

        public string CapturePreviewLayout() { return placementToken; }

        public void RestorePreviewLayout(string token)
        {
            if (geometryReady) return;
            restored=PreviewPlacement.Decode(token);
            followAspect=restored==null || restored.Follow;
            UpdateAspectButton();
        }

        void UpdateAspectButton()
        {
            StyleButton(aspect,followAspect);
            string message=followAspect ? "已开启：窗口随画面比例调整" : "已关闭：保留当前窗口大小";
            aspect.AccessibleDescription=message;
            tips.SetToolTip(aspect,message+"（Ctrl+R 切换）");
        }

        void ChangeAspectMode()
        {
            followAspect=!followAspect;
            UpdateAspectButton();
            preferredVideoArea=0;
            pendingFit=followAspect && !sourceSize.IsEmpty;
            FitIfPending();
            RecordPlacement();
        }

        Screen SavedDisplay()
        {
            if (restored==null) return Screen.FromPoint(Cursor.Position);
            foreach (var screen in Screen.AllScreens)
                if (String.Equals(screen.DeviceName,restored.Display,StringComparison.OrdinalIgnoreCase)) return screen;
            return Screen.PrimaryScreen ?? Screen.FromPoint(Cursor.Position);
        }

        void LoadPlacement()
        {
            var work=SavedDisplay().WorkingArea;
            designMinimum=MinimumSize;
            LimitMinimum(work);
            Bounds=restored==null
                ? PreviewPlacement.Centered(work,MinimumSize,new Size(work.Width*4/5,work.Height*4/5),
                    work.Left+work.Width/2.0,work.Top+work.Height/2.0)
                : restored.Restore(work,MinimumSize);
            if (restored!=null && restored.Maximized) WindowState=FormWindowState.Maximized;
            geometryReady=true;
            RecordPlacement();
        }

        void LimitMinimum(Rectangle work)
        {
            var limit=new Size(Math.Min(work.Width,designMinimum.Width),Math.Min(work.Height,designMinimum.Height));
            if (MinimumSize!=limit) MinimumSize=limit;
        }

        void RecordPlacement()
        {
            if (!geometryReady || arrangingWindow || manualResize || !Visible || IsDisposed || Disposing ||
                WindowState==FormWindowState.Minimized) return;
            bool maximized=IsFullscreen ? previousState==FormWindowState.Maximized : WindowState==FormWindowState.Maximized;
            var normal=IsFullscreen ? previousBounds : maximized ? RestoreBounds : Bounds;
            if (normal.Width<=0 || normal.Height<=0) return;
            var screen=Screen.FromControl(this);
            var work=screen.WorkingArea;
            normal.Offset(-work.Left,-work.Top);
            string token=new PreviewPlacement(screen.DeviceName,normal,work.Size,followAspect,maximized).Encode();
            if (token==placementToken) return;
            placementToken=token;
            PreviewLayoutChanged?.Invoke(this,EventArgs.Empty);
        }

        public void SetVideoSize(uint width,uint height)
        {
            if (width==0 || height==0 || width>65535 || height>65535) return;
            var next=new Size((int)width,(int)height);
            if (next==sourceSize) return;
            bool changed=sourceSize.IsEmpty || Math.Abs((double)sourceSize.Width*next.Height/(sourceSize.Height*(double)next.Width)-1)>.005;
            sourceSize=next;
            if (changed && followAspect) { pendingFit=true; FitIfPending(); }
        }

        void FitIfPending()
        {
            if (!pendingFit || !followAspect || sourceSize.IsEmpty || !geometryReady || manualResize || arrangingWindow ||
                !Visible || IsFullscreen || WindowState!=FormWindowState.Normal || IsDisposed) return;
            pendingFit=false;
            var work=Screen.FromControl(this).WorkingArea;
            var current=Bounds;
            var chrome=new Size(Width-ClientSize.Width,Height-ClientSize.Height);
            if (preferredVideoArea<=0)
                preferredVideoArea=(double)Math.Max(1,surface.ClientSize.Width)*Math.Max(1,surface.ClientSize.Height);
            double ratio=(double)sourceSize.Width/sourceSize.Height;
            int wanted=(int)Math.Round(Math.Sqrt(preferredVideoArea*ratio));
            int maxWidth=Math.Max(1,work.Width-chrome.Width), maxHeight=Math.Max(1,work.Height-chrome.Height);
            int width=Math.Max(1,Math.Min(wanted,maxWidth)), barHeight=0;
            // A narrower window may wrap the toolbar, using extra vertical space.
            for (int i=0;i<3;i++)
            {
                barHeight=MeasureToolbarHeight(width);
                int limit=(int)Math.Min(maxWidth,Math.Max(1,maxHeight-barHeight)*ratio);
                int floor=(int)Math.Ceiling(Math.Max(0,MinimumSize.Height-chrome.Height-barHeight)*ratio);
                int nextWidth=Math.Min(maxWidth,Math.Max(MinimumSize.Width-chrome.Width,Math.Min(limit,Math.Max(floor,wanted))));
                if (nextWidth==width) break;
                width=Math.Max(1,nextWidth);
            }
            barHeight=MeasureToolbarHeight(width);
            int imageHeight=(int)Math.Min(maxHeight,Math.Round(width/ratio));
            var size=new Size(width+chrome.Width,Math.Min(work.Height,imageHeight+barHeight+chrome.Height));
            var target=PreviewPlacement.Centered(work,MinimumSize,size,current.Left+current.Width/2.0,current.Top+current.Height/2.0);
            arrangingWindow=true;
            try { LimitMinimum(work); if (Bounds!=target) Bounds=target; }
            finally { arrangingWindow=false; }
            RecordPlacement();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            FitIfPending();
            RecordPlacement();
        }

        protected override void OnMove(EventArgs e)
        {
            base.OnMove(e); RecordPlacement();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            // Windows can still be restoring its own saved rectangle inside WM_SIZE.
            if (pendingFit && !fitQueued && geometryReady && IsHandleCreated && !Disposing) {
                fitQueued=true;
                BeginInvoke(new Action(delegate { fitQueued=false; FitIfPending(); }));
            }
            RecordPlacement();
        }

        protected override void OnResizeBegin(EventArgs e)
        {
            manualResize=true; dragStartSize=ClientSize; base.OnResizeBegin(e);
        }

        protected override void OnResizeEnd(EventArgs e)
        {
            base.OnResizeEnd(e); manualResize=false;
            if (ClientSize!=dragStartSize && WindowState==FormWindowState.Normal && !IsFullscreen) {
                preferredVideoArea=0; pendingFit=followAspect && !sourceSize.IsEmpty;
            }
            FitIfPending(); RecordPlacement();
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg!=DisplayChange || !geometryReady || !IsHandleCreated || IsDisposed || Disposing) return;
            BeginInvoke(new Action(delegate {
                if (IsDisposed || Disposing || controllerClosing) return;
                var display=Screen.FromControl(this);
                var work=display.WorkingArea;
                arrangingWindow=true;
                try {
                    LimitMinimum(work);
                    if (IsFullscreen) {
                        Bounds=display.Bounds;
                        previousBounds=PreviewPlacement.Centered(work,MinimumSize,previousBounds.Size,
                            previousBounds.Left+previousBounds.Width/2.0,previousBounds.Top+previousBounds.Height/2.0);
                    } else if (WindowState==FormWindowState.Normal && !work.Contains(Bounds)) {
                        Bounds=PreviewPlacement.Centered(work,MinimumSize,Size,Left+Width/2.0,Top+Height/2.0);
                    }
                } finally { arrangingWindow=false; }
                pendingFit=followAspect && !sourceSize.IsEmpty;
                FitIfPending(); RecordPlacement();
            }));
        }
    }
}
