using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MirrorDesk
{
    internal sealed class PageTransition : Control, IMessageFilter
    {
        const int DurationMs=170;
        const float Handoff=.32f;
        const float FadeDepth=.72f;
        const uint GetClientAreaAnimation=0x1042;
        readonly Stopwatch clock=new Stopwatch();
        readonly Timer timer=new Timer { Interval=15 };
        readonly ColorMatrix opacity=new ColorMatrix();
        readonly ImageAttributes blend=new ImageAttributes();
        Bitmap previous, incoming;
        float progress;
        int offset;

        internal Func<bool> AnimationsEnabled=ReadAnimationsEnabled;
        internal bool IsActive { get { return previous!=null; } }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool SystemParametersInfo(uint action,uint parameter,out int value,uint flags);

        static bool ReadAnimationsEnabled()
        {
            return SystemParametersInfo(GetClientAreaAnimation,0,out int enabled,0)
                ? enabled!=0 : SystemInformation.IsMenuAnimationEnabled;
        }

        internal PageTransition()
        {
            Name="ConsoleTransition";
            Dock=DockStyle.Fill;
            Margin=Padding.Empty;
            TabStop=false;
            Visible=false;
            SetStyle(ControlStyles.Selectable,false);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque,true);
            timer.Tick+=delegate {
                progress=(float)(clock.Elapsed.TotalMilliseconds/DurationMs);
                if (progress>=1) Finish();
                else Invalidate();
            };
        }

        internal void Begin(Control source,int enterOffset)
        {
            Finish();
            if (!source.Visible || !source.IsHandleCreated || SystemInformation.HighContrast ||
                source.FindForm()?.WindowState==FormWindowState.Minimized || !AnimationsEnabled()) return;
            Bounds=source.Bounds;
            previous=Capture(source);
            if (previous==null) return;
            offset=enterOffset;
            progress=0;
            BringToFront();
            Visible=true;
        }

        internal void Play(Control destination)
        {
            if (!IsActive) return;
            if (destination.Size==Size) incoming=Capture(destination);
            if (incoming==null) { Finish(); return; }
            Application.AddMessageFilter(this);
            clock.Restart();
            timer.Start();
            Invalidate();
        }

        static Bitmap Capture(Control page)
        {
            if (page.Width<1 || page.Height<1) return null;
            Bitmap frame=null;
            try {
                frame=new Bitmap(page.Width,page.Height,PixelFormat.Format32bppRgb);
                page.DrawToBitmap(frame,new Rectangle(Point.Empty,page.Size));
                return frame;
            } catch (Exception error) when (error is ArgumentException || error is ExternalException ||
                error is OutOfMemoryException) {
                frame?.Dispose();
                return null;
            }
        }

        internal void Finish()
        {
            if (!IsActive) return;
            timer.Stop();
            clock.Stop();
            if (incoming!=null) Application.RemoveMessageFilter(this);
            previous.Dispose(); previous=null;
            incoming?.Dispose(); incoming=null;
            Visible=false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (previous==null) { GlassUi.ControlBackground(e.Graphics,this); return; }
            if (incoming==null || progress<=0) { e.Graphics.DrawImageUnscaled(previous,0,0); return; }

            GlassUi.ControlBackground(e.Graphics,this);
            bool entering=progress>=Handoff;
            float phase=entering ? (progress-Handoff)/(1f-Handoff) : progress/Handoff;
            float remaining=1f-phase;
            float settle=remaining*remaining*remaining;
            opacity.Matrix33=1f-FadeDepth*(entering ? settle : phase*phase);
            blend.SetColorMatrix(opacity);
            // Keep just one page of text visible during the handoff.
            var frame=entering ? incoming : previous;
            int y=entering ? (int)Math.Round(offset*settle) : 0;
            e.Graphics.DrawImage(frame,new Rectangle(0,y,frame.Width,frame.Height),
                0,0,frame.Width,frame.Height,GraphicsUnit.Pixel,blend);
        }

        // Pointer input reaches the controls underneath the painted frames.
        protected override void WndProc(ref Message message)
        {
            const int NonClientHitTest=0x0084;
            if (message.Msg==NonClientHitTest) { message.Result=new IntPtr(-1); return; }
            base.WndProc(ref message);
        }

        bool IMessageFilter.PreFilterMessage(ref Message message)
        {
            switch (message.Msg) {
                case 0x0100: // WM_KEYDOWN
                case 0x0104: // WM_SYSKEYDOWN
                case 0x0201: // WM_LBUTTONDOWN
                case 0x0204: // WM_RBUTTONDOWN
                case 0x0207: // WM_MBUTTONDOWN
                case 0x020A: // WM_MOUSEWHEEL
                case 0x020E: // WM_MOUSEHWHEEL
                    if (Control.FromChildHandle(message.HWnd)?.FindForm()==FindForm()) Finish();
                    break;
            }
            return false;
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            Finish();
            base.OnSizeChanged(e);
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            if (!Visible) Finish();
            base.OnVisibleChanged(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) {
                Finish();
                timer.Dispose();
                blend.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
