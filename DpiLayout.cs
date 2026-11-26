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
        readonly List<Action<float>> scaleControls = new List<Action<float>>();
        readonly List<Font> scaledFonts = new List<Font>();
        int applied;

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
            bool ownFont=control==host || !ReferenceEquals(font,control.Parent.Font);
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

            scaleControls.Add(scale=>{
                control.Padding=Scale(padding,scale);
                control.Margin=Scale(margin,scale);
                if (control!=host) control.MinimumSize=new Size(Pixels(minimum.Width,scale),Pixels(minimum.Height,scale));
                if (topGrid) control.Height=Pixels(height,scale);
                if (ownFont) {
                    var updated=new Font(font.FontFamily,font.SizeInPoints*96f/72f*scale,font.Style,GraphicsUnit.Pixel);
                    scaledFonts.Add(updated);
                    control.Font=updated;
                }
                foreach (var row in rows) row(scale);
            });
            foreach (Control child in control.Controls) Capture(child);
        }

        public bool Apply(int dpi)
        {
            if (dpi<=0 || applied==dpi) return false;
            applied=dpi;
            var old=scaledFonts.ToArray();
            scaledFonts.Clear();
            Walk(host,false);
            try { foreach (var scale in scaleControls) scale(dpi/96f); }
            finally {
                Walk(host,true);
                foreach (var font in old) font.Dispose();
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
        }
    }
}
