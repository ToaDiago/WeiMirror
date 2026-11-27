using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MirrorDesk
{
    // Capture the design at 96 DPI once. Every monitor change starts from that
    // baseline, including the absolute rows of nested TableLayoutPanels.
    internal sealed class DpiLayout : IDisposable
    {
        [DllImport("user32.dll")]
        static extern uint GetDpiForWindow(IntPtr hwnd);

        readonly Form host;
        readonly List<Action<float,float,string>> scaleControls = new List<Action<float,float,string>>();
        readonly List<Font> scaledFonts = new List<Font>();
        readonly List<Font> retiredFonts = new List<Font>();
        int applied;
        float appliedLayoutScale, appliedTextScale;
        string appliedFont;

        public DpiLayout(Form form)
        {
            host=form;
            Capture(form);
            host.Disposed += delegate { Dispose(); };
        }

        public static int Read(Control window)
        {
            if (!window.IsHandleCreated) return 96;
            try {
                uint dpi=GetDpiForWindow(window.Handle);
                if (dpi!=0) return (int)dpi;
            } catch (EntryPointNotFoundException) {}
            using (var graphics=window.CreateGraphics()) return (int)Math.Round(graphics.DpiX);
        }

        internal static int Pixels(float value,float scale) { return (int)Math.Round(value*scale); }
        static Padding Scale(Padding value,float scale)
        {
            return new Padding(Pixels(value.Left,scale),Pixels(value.Top,scale),Pixels(value.Right,scale),Pixels(value.Bottom,scale));
        }

        void Capture(Control control)
        {
            var padding=control.Padding;
            var margin=control.Margin;
            var minimum=control.MinimumSize;
            var height=control.Height;
            var font=control.Font;
            string family=font.Name;
            float points=font.SizeInPoints;
            FontStyle style=font.Style;
            Font ownedFont=null;
            bool topGrid=control is TableLayoutPanel && control.Dock==DockStyle.Top && !control.AutoSize;
            var table=control as TableLayoutPanel;
            var rows=new List<Action<float>>();
            if (table!=null) {
                foreach (RowStyle row in table.RowStyles) {
                    float value=row.Height;
                    if (row.SizeType==SizeType.Absolute) rows.Add(scale=>row.Height=Pixels(value,scale));
                }
                foreach (ColumnStyle column in table.ColumnStyles) {
                    float value=column.Width;
                    if (column.SizeType==SizeType.Absolute) rows.Add(scale=>column.Width=Pixels(value,scale));
                }
            }

            scaleControls.Add((scale,textScale,fontName)=>{
                control.Padding=Scale(padding,scale);
                control.Margin=Scale(margin,scale);
                if (control!=host) control.MinimumSize=new Size(Pixels(minimum.Width,scale),Pixels(minimum.Height,scale));
                if (topGrid) control.Height=Pixels(height,scale);
                float size=points*96f/72f*textScale;
                var updated=ScaledFont(family,fontName,size,style);
                // WinForms keeps the old Font instance when the values are equal.
                // A layout-only resize must keep that instance alive.
                if (updated.Equals(control.Font)) updated.Dispose();
                else {
                    control.Font=updated;
                    if (ownedFont!=null) retiredFonts.Add(ownedFont);
                    ownedFont=updated;
                }
                if (ownedFont!=null) scaledFonts.Add(ownedFont);
                foreach (var row in rows) row(scale);
            });
            foreach (Control child in control.Controls) Capture(child);
        }

        static Font ScaledFont(string baseline,string selected,float size,FontStyle style)
        {
            if (!String.IsNullOrEmpty(selected)) {
                try { return new Font(selected,size,style,GraphicsUnit.Pixel); }
                catch (ArgumentException) { }
            }
            return new Font(baseline,size,style,GraphicsUnit.Pixel);
        }

        public bool Apply(int dpi,float layoutScale=1f,float textScale=1f,string fontName=null)
        {
            if (dpi<=0 || (applied==dpi && appliedLayoutScale==layoutScale &&
                appliedTextScale==textScale && appliedFont==fontName)) return false;
            applied=dpi;
            appliedLayoutScale=layoutScale; appliedTextScale=textScale; appliedFont=fontName;
            scaledFonts.Clear();
            Walk(host,false);
            try { foreach (var scale in scaleControls) scale(dpi/96f*layoutScale,dpi/96f*textScale,fontName); }
            finally {
                Walk(host,true);
                foreach (var font in retiredFonts) font.Dispose();
                retiredFonts.Clear();
            }
            return true;
        }

        static void Walk(Control control,bool resume)
        {
            if (!resume) control.SuspendLayout();
            foreach (Control child in control.Controls) Walk(child,resume);
            if (resume) control.ResumeLayout(true);
        }

        public void Dispose()
        {
            foreach (var font in scaledFonts) font.Dispose();
            scaledFonts.Clear();
            foreach (var font in retiredFonts) font.Dispose();
            retiredFonts.Clear();
        }
    }
}
