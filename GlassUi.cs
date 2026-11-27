using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MirrorDesk
{
    internal interface IGlassHost
    {
        AppPalette GlassPalette { get; }
    }

    internal static class GlassUi
    {
        static readonly AppPalette DefaultPalette=new AppPalette(0,false);

        internal static GlassGrid Grid(int columns,int rows)
        {
            return new GlassGrid { ColumnCount=columns, RowCount=rows, Dock=DockStyle.Fill,
                Margin=Padding.Empty, Padding=Padding.Empty, GrowStyle=TableLayoutPanelGrowStyle.FixedSize };
        }

        internal static Label Label(string text,float size,Color ink,bool bold=false)
        {
            return new GlassLabel { Text=text, Font=Theme.Font(size,bold), ForeColor=ink,
                Dock=DockStyle.Fill, Margin=Padding.Empty, TextAlign=ContentAlignment.MiddleLeft, AutoEllipsis=true };
        }

        // Paint the correct slice of the scene into each child HWND. The entire
        // slice starts opaque, so native and buffered repaints see the same pixels.
        internal static void ControlBackground(Graphics g,Control target)
        {
            AppPalette palette=(target.FindForm() as IGlassHost)?.GlassPalette ?? DefaultPalette;
            ConsoleSurface pane=null;
            Control track=null;
            var offset=Point.Empty;
            var paneOffset=Point.Empty;
            var trackOffset=Point.Empty;
            for (var control=target; control.Parent!=null; control=control.Parent) {
                offset.Offset(control.Left,control.Top);
                if (control.Parent is TransportPicker || (control.Parent is GlassGrid grid && grid.Segmented)) {
                    track=control.Parent;
                    trackOffset=offset;
                }
                if (control.Parent is ConsoleSurface surface) {
                    pane=surface;
                    paneOffset=offset;
                    break;
                }
            }
            var state=g.Save();
            g.SetClip(target.ClientRectangle,CombineMode.Intersect);
            if (pane==null) Backdrop(g,target,palette);
            else {
                var layer=g.Save();
                g.TranslateTransform(-paneOffset.X,-paneOffset.Y);
                Pane(g,pane,palette);
                g.Restore(layer);
            }
            if (track!=null) {
                g.TranslateTransform(-trackOffset.X,-trackOffset.Y);
                Tray(g,track,palette);
            }
            g.Restore(state);
        }

        internal static void Tray(Graphics g,Control track,AppPalette palette)
        {
            var box=Rectangle.Inflate(track.ClientRectangle,-1,-1);
            if (box.Width<4 || box.Height<4) return;
            var state=g.Save();
            g.SmoothingMode=SmoothingMode.AntiAlias;
            using (var shape=Round(box,Math.Max(10,track.Font.Height)))
            using (var fill=new SolidBrush(SystemInformation.HighContrast ? SystemColors.Window :
                Color.FromArgb(palette.Dark ? 91 : 30,palette.Action)))
            using (var outline=new Pen(SystemInformation.HighContrast ? SystemColors.WindowText :
                Color.FromArgb(palette.Dark ? 56 : 126,Color.White))) {
                g.FillPath(fill,shape);
                g.DrawPath(outline,shape);
            }
            g.Restore(state);
        }

        internal static GraphicsPath Round(Rectangle box,int radius)
        {
            var path=new GraphicsPath();
            int diameter=Math.Min(radius*2,Math.Min(box.Width,box.Height));
            if (diameter<2) { path.AddRectangle(box); return path; }
            path.AddArc(box.Left,box.Top,diameter,diameter,180,90);
            path.AddArc(box.Right-diameter,box.Top,diameter,diameter,270,90);
            path.AddArc(box.Right-diameter,box.Bottom-diameter,diameter,diameter,0,90);
            path.AddArc(box.Left,box.Bottom-diameter,diameter,diameter,90,90);
            path.CloseFigure();
            return path;
        }

        // Use one coordinate space for the window and each pane. The offset inside
        // a pane makes the bands bend at its edge instead of repeating per control.
        internal static void Backdrop(Graphics g,Control surface,AppPalette palette,int refraction=0)
        {
            Form window=surface.FindForm();
            var bounds=window?.ClientRectangle ?? surface.ClientRectangle;
            if (bounds.Width<2 || bounds.Height<2) return;
            var origin=Point.Empty;
            for (Control c=surface; c!=null && c!=window; c=c.Parent)
                origin.Offset(c.Left,c.Top);
            var state=g.Save();
            g.SetClip(surface.ClientRectangle,CombineMode.Intersect);
            g.TranslateTransform(-origin.X+refraction,-origin.Y-refraction/2);
            var fill=Rectangle.Inflate(bounds,32,32);
            if (SystemInformation.HighContrast) {
                using (var solid=new SolidBrush(SystemColors.Window)) g.FillRectangle(solid,fill);
                g.Restore(state);
                return;
            }
            Color light=palette.Dark ? AppPalette.Blend(palette.Canvas,palette.Atmosphere,.11) :
                AppPalette.Blend(palette.Canvas,Color.White,.76);
            Color deep=AppPalette.Blend(palette.Canvas,palette.Atmosphere,palette.Dark ? .34 : .38);
            using (var wash=new LinearGradientBrush(fill,light,deep,51f)) g.FillRectangle(wash,fill);
            float w=bounds.Width, h=bounds.Height;
            Light(g,new RectangleF(w*.54f,-h*.25f,w*.90f,h*1.32f),palette.Atmosphere,palette.Dark ? 112 : 156);
            Light(g,new RectangleF(-w*.34f,h*.49f,w*1.32f,h*.86f),palette.Caustic,palette.Dark ? 94 : 128);
            Light(g,new RectangleF(w*.27f,h*.48f,w*1.06f,h*.95f),Color.White,palette.Dark ? 17 : 107);
            using (var arc=new GraphicsPath())
            {
                arc.AddBezier(-w*.08f,h*1.04f,w*.40f,h*.91f,w*.79f,h*.89f,w*1.14f,-h*.11f);
                g.SmoothingMode=SmoothingMode.AntiAlias;
                using (var glow=new Pen(Color.FromArgb(palette.Dark ? 11 : 33,Color.White),Math.Max(9,h*.060f)))
                using (var edge=new Pen(Color.FromArgb(palette.Dark ? 27 : 66,Color.White),Math.Max(1,h*.004f))) {
                    g.DrawPath(glow,arc);
                    g.DrawPath(edge,arc);
                }
            }
            g.Restore(state);
        }

        static void Light(Graphics g,RectangleF ellipse,Color hue,int strength)
        {
            using (var edge=new GraphicsPath()) {
                edge.AddEllipse(ellipse);
                using (var light=new PathGradientBrush(edge)) {
                    light.CenterColor=Color.FromArgb(strength,hue);
                    light.SurroundColors=new[] { Color.FromArgb(0,hue) };
                    g.FillPath(light,edge);
                }
            }
        }

        internal static void Pane(Graphics g,Control surface,AppPalette palette)
        {
            Backdrop(g,surface,palette);
            var box=new Rectangle(3,2,surface.Width-7,surface.Height-8);
            if (box.Width<4 || box.Height<4 || SystemInformation.HighContrast) return;
            int radius=Math.Max(14,surface.Font.Height+5);
            var state=g.Save();
            g.SmoothingMode=SmoothingMode.AntiAlias;
            var shade=box;
            shade.Offset(0,3);
            using (var shadow=Round(shade,radius))
            using (var fill=new SolidBrush(Color.FromArgb(palette.Dark ? 55 : 20,Color.FromArgb(26,53,99))))
                g.FillPath(fill,shadow);
            using (var shape=Round(box,radius)) {
                var clip=g.Save();
                g.SetClip(shape,CombineMode.Intersect);
                Backdrop(g,surface,palette,Math.Max(4,surface.Font.Height/3));
                g.Restore(clip);
                Color top=Color.FromArgb(palette.Dark ? 155 : 183,palette.Dark ? palette.Surface : Color.White);
                Color bottom=Color.FromArgb(palette.Dark ? 98 : 91,palette.Dark ? palette.Surface : Color.White);
                using (var coat=new LinearGradientBrush(box,top,bottom,72f))
                using (var rim=new LinearGradientBrush(box,Color.FromArgb(palette.Dark ? 132 : 244,Color.White),
                    Color.FromArgb(palette.Dark ? 39 : 124,Color.White),104f))
                using (var boundary=new Pen(rim,1.4f)) {
                    g.FillPath(coat,shape);
                    g.DrawPath(boundary,shape);
                }
                box.Inflate(-2,-2);
                if (box.Width>2 && box.Height>2) {
                    using (var highlight=Round(box,radius-2))
                    using (var rim=new LinearGradientBrush(box,Color.FromArgb(palette.Dark ? 51 : 176,Color.White),
                        Color.FromArgb(0,Color.White),96f))
                    using (var pen=new Pen(rim)) g.DrawPath(pen,highlight);
                }
            }
            g.Restore(state);
        }

        internal static void Lens(Graphics g,Rectangle box,int radius,AppPalette palette,bool accent,bool hot,bool pressed=false)
        {
            if (box.Width<3 || box.Height<3) return;
            var state=g.Save();
            g.SmoothingMode=SmoothingMode.AntiAlias;
            using (var shape=Round(box,radius)) {
                Color top, bottom, lip;
                if (SystemInformation.HighContrast) {
                    top=bottom=accent ? SystemColors.Highlight : SystemColors.Window;
                    lip=SystemColors.WindowText;
                } else if (accent) {
                    var action=palette.Action;
                    top=AppPalette.Blend(action,palette.Atmosphere,pressed ? .06 : hot ? .32 : .22);
                    bottom=AppPalette.Blend(action,Color.FromArgb(15,24,59),.24);
                    lip=Color.FromArgb(196,AppPalette.Blend(palette.Atmosphere,Color.White,.62));
                } else {
                    top=Color.FromArgb(hot ? 222 : 153,palette.Dark ? palette.Selection : Color.White);
                    bottom=Color.FromArgb(hot ? 129 : 64,palette.Dark ? palette.Canvas : Color.White);
                    lip=Color.FromArgb(palette.Dark ? 94 : 224,Color.White);
                }
                using (var fill=new LinearGradientBrush(box,top,bottom,88f))
                using (var edge=new LinearGradientBrush(box,lip,Color.FromArgb(palette.Dark ? 67 : 116,palette.Atmosphere),100f))
                using (var pen=new Pen(edge,1.15f)) {
                    g.FillPath(fill,shape);
                    g.DrawPath(pen,shape);
                }
            }
            g.Restore(state);
        }

        internal static Color ActionInk(bool accent,AppPalette palette)
        {
            if (SystemInformation.HighContrast) return accent ? SystemColors.HighlightText : SystemColors.WindowText;
            return accent ? Color.White : palette.Ink;
        }

        internal static void Segment(Graphics g,Rectangle box,int radius,AppPalette palette,bool selected,bool hot)
        {
            if ((!selected && !hot) || box.Width<4 || box.Height<4) return;
            GlassUi.Lens(g,box,radius,palette,SystemInformation.HighContrast && selected,selected || hot);
            if (!selected || SystemInformation.HighContrast) return;
            var state=g.Save();
            g.SmoothingMode=SmoothingMode.AntiAlias;
            using (var shape=Round(box,radius))
            using (var edge=new Pen(Color.FromArgb(palette.Dark ? 95 : 50,palette.Atmosphere)))
                g.DrawPath(edge,shape);
            g.Restore(state);
        }

        internal static Color SegmentInk(AppPalette palette,bool selected,bool enabled)
        {
            if (SystemInformation.HighContrast) return ActionInk(selected,palette);
            if (!enabled || !selected) return palette.Muted;
            return palette.Dark ? palette.Ink : palette.Action;
        }
    }

    internal sealed class GlassGrid : TableLayoutPanel
    {
        internal bool Segmented { get; set; }
        internal bool RowRules { get; set; }

        public GlassGrid() { DoubleBuffered=true; SetStyle(ControlStyles.ResizeRedraw,true); }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            GlassUi.ControlBackground(e.Graphics,this);
            var palette=(FindForm() as IGlassHost)?.GlassPalette;
            if (palette==null) return;
            if (Segmented) GlassUi.Tray(e.Graphics,this,palette);
            if (!RowRules || SystemInformation.HighContrast) return;

            var rows=GetRowHeights();
            int y=Padding.Top;
            using (var rule=new Pen(Color.FromArgb(palette.Dark ? 29 : 28,palette.Ink))) {
                for (int row=0;row<rows.Length-1;row++) {
                    y+=rows[row];
                    e.Graphics.DrawLine(rule,Padding.Left,y,Width-Padding.Right,y);
                }
            }
        }
    }

    internal sealed class GlassPanel : Panel
    {
        public GlassPanel() { DoubleBuffered=true; SetStyle(ControlStyles.ResizeRedraw,true); }
        protected override void OnPaintBackground(PaintEventArgs e) { GlassUi.ControlBackground(e.Graphics,this); }
    }

    internal class GlassLabel : Label
    {
        public GlassLabel() { DoubleBuffered=true; SetStyle(ControlStyles.ResizeRedraw,true); }
        protected override void OnPaintBackground(PaintEventArgs e) { GlassUi.ControlBackground(e.Graphics,this); }
    }

    internal sealed class GlassButton : Button
    {
        AppPalette palette=new AppPalette(0,false);
        bool accent, hot, pressed;
        internal bool SegmentChoice { get; set; }

        internal GlassButton(string label,bool prominent=false)
        {
            Text=label; accent=prominent;
            Font=Theme.Font(11,prominent);
            Dock=DockStyle.Fill;
            Margin=Padding.Empty;
            FlatStyle=FlatStyle.Flat;
            UseVisualStyleBackColor=false;
            Cursor=Cursors.Hand;
            SetStyle(ControlStyles.Opaque | ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw,true);
        }

        internal void ApplyPalette(AppPalette value,bool prominent)
        {
            palette=value; accent=prominent;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hot=true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hot=pressed=false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); pressed=e.Button==MouseButtons.Left; Invalidate(); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); pressed=false; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            GlassUi.ControlBackground(e.Graphics,this);
            var box=Rectangle.Inflate(ClientRectangle,-2,-2);
            if (box.Width<3 || box.Height<3) return;
            int radius=Math.Max(10,Font.Height*3/4);
            Color ink;
            if (SegmentChoice) {
                GlassUi.Segment(e.Graphics,box,radius,palette,accent,Enabled && hot);
                ink=GlassUi.SegmentInk(palette,accent,Enabled);
            } else {
                GlassUi.Lens(e.Graphics,box,radius,palette,Enabled && accent,Enabled && hot,pressed);
                ink=Enabled ? GlassUi.ActionInk(accent,palette) : palette.Muted;
            }
            TextRenderer.DrawText(e.Graphics,Text,Font,Rectangle.Inflate(box,-5,0),ink,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics,Rectangle.Inflate(box,-4,-4),ink,BackColor);
        }
    }

    internal sealed class GlassToggle : CheckBox
    {
        AppPalette palette=new AppPalette(0,false);

        internal GlassToggle(string label)
        {
            Text=label;
            SetStyle(ControlStyles.Opaque | ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw,true);
            Dock=DockStyle.Fill;
            Margin=Padding.Empty;
            Cursor=Cursors.Hand;
            CheckedChanged+=delegate { Invalidate(); };
        }

        internal void ApplyPalette(AppPalette value) { palette=value; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            GlassUi.ControlBackground(e.Graphics,this);
            int height=Math.Min(Height-4,Math.Max(16,Font.Height));
            int width=height*9/5;
            if (height<6 || Width<=width) return;
            var pill=new Rectangle(Width-width-2,(Height-height)/2,width,height);
            GlassUi.Lens(e.Graphics,pill,height/2,palette,Enabled && Checked,false);
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            int dot=height-6;
            using (var brush=new SolidBrush(SystemInformation.HighContrast ? SystemColors.WindowText :
                Checked || palette.Dark ? Color.White : palette.Action))
                e.Graphics.FillEllipse(brush,Checked ? pill.Right-dot-3 : pill.Left+3,pill.Y+3,dot,dot);
            var label=new Rectangle(0,0,Math.Max(0,pill.Left-9),Height);
            Color ink=Enabled ? GlassUi.ActionInk(false,palette) : palette.Muted;
            TextRenderer.DrawText(e.Graphics,Text,Font,label,ink,TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics,Rectangle.Inflate(ClientRectangle,-2,-2));
        }
    }
}
