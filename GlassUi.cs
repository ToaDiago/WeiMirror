using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace MirrorDesk
{
    internal enum InterfaceGlyph { None, Settings, Casting, Appearance, Components, Tools }

    internal interface IGlassHost
    {
        AppPalette GlassPalette { get; }
    }

    internal static class GlassUi
    {
        const int CachePixelLimit=4096*2160;
        static readonly AppPalette DefaultPalette=new AppPalette(0,false);
        static readonly ConditionalWeakTable<Form,BackdropBuffer> Backdrops=new ConditionalWeakTable<Form,BackdropBuffer>();
        static readonly ConditionalWeakTable<Control,PaneBuffer> Panes=new ConditionalWeakTable<Control,PaneBuffer>();

        sealed class BackdropBuffer
        {
            Bitmap image;
            AppPalette palette;

            public BackdropBuffer(Form owner)
            {
                owner.Disposed+=delegate { image?.Dispose(); image=null; };
            }

            public Bitmap Get(Size size,AppPalette next)
            {
                if (image!=null && image.Size==size && palette==next) return image;
                image?.Dispose();
                image=null;
                image=new Bitmap(size.Width,size.Height,PixelFormat.Format32bppRgb);
                using (var graphics=Graphics.FromImage(image))
                    PaintBackdrop(graphics,new Rectangle(Point.Empty,size),next);
                palette=next;
                return image;
            }
        }

        sealed class PaneBuffer
        {
            Bitmap image;
            AppPalette palette;
            Rectangle bounds;
            Size windowSize;
            int radius;

            public PaneBuffer(Control owner)
            {
                owner.Disposed+=delegate { image?.Dispose(); image=null; };
            }

            public Bitmap Get(Control pane,Form window,int corner,AppPalette next)
            {
                var area=new Rectangle(OriginInWindow(pane,window),pane.Size);
                if (image!=null && bounds==area && windowSize==window.ClientSize && radius==corner && palette==next)
                    return image;
                image?.Dispose();
                image=null;
                image=new Bitmap(area.Width,area.Height,PixelFormat.Format32bppRgb);
                using (var graphics=Graphics.FromImage(image)) PaintPane(graphics,pane,corner,next);
                bounds=area;
                windowSize=window.ClientSize;
                radius=corner;
                palette=next;
                return image;
            }
        }

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
            bool highContrast=SystemInformation.HighContrast;
            var top=highContrast ? SystemColors.Window : Color.FromArgb(palette.Dark ? 104 : 27,palette.Dark ? Color.Black : palette.Action);
            var bottom=highContrast ? top : Color.FromArgb(palette.Dark ? 46 : 15,palette.Dark ? Color.Black : palette.Action);
            var rim=highContrast ? SystemColors.WindowText : Color.FromArgb(palette.Dark ? 26 : 36,palette.Ink);
            using (var shape=Round(box,Math.Max(10,track.Font.Height)))
            using (var fill=new LinearGradientBrush(box,top,bottom,90f))
            using (var outline=new Pen(rim)) {
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

        // Sample the same window coordinates in every opaque child control.
        internal static void Backdrop(Graphics g,Control surface,AppPalette palette)
        {
            Form window=surface.FindForm();
            var bounds=window?.ClientRectangle ?? surface.ClientRectangle;
            if (bounds.Width<2 || bounds.Height<2) return;
            var origin=OriginInWindow(surface,window);
            var state=g.Save();
            g.SetClip(surface.ClientRectangle,CombineMode.Intersect);
            if (SystemInformation.HighContrast) {
                using (var solid=new SolidBrush(SystemColors.Window)) g.FillRectangle(solid,surface.ClientRectangle);
            } else if (window!=null && (long)bounds.Width*bounds.Height<=CachePixelLimit) {
                var scene=Backdrops.GetValue(window,owner=>new BackdropBuffer(owner)).Get(bounds.Size,palette);
                g.DrawImageUnscaled(scene,-origin.X,-origin.Y);
            } else {
                g.TranslateTransform(-origin.X,-origin.Y);
                PaintBackdrop(g,bounds,palette);
            }
            g.Restore(state);
        }

        static Point OriginInWindow(Control surface,Form window)
        {
            var origin=Point.Empty;
            for (Control c=surface; c!=null && c!=window; c=c.Parent)
                origin.Offset(c.Left,c.Top);
            return origin;
        }

        static void PaintBackdrop(Graphics g,Rectangle bounds,AppPalette palette)
        {
            var fill=Rectangle.Inflate(bounds,32,32);
            Color light=AppPalette.Blend(palette.Canvas,Color.White,palette.Dark ? .025 : .63);
            Color deep=AppPalette.Blend(palette.Canvas,palette.Atmosphere,palette.Dark ? .073 : .15);
            using (var wash=new LinearGradientBrush(fill,light,deep,105f)) g.FillRectangle(wash,fill);
            float w=bounds.Width, h=bounds.Height;
            Light(g,new RectangleF(w*.44f,-h*.47f,w*1.02f,h*1.42f),palette.Atmosphere,palette.Dark ? 57 : 97);
            Light(g,new RectangleF(-w*.49f,h*.28f,w*1.43f,h*1.23f),palette.Caustic,palette.Dark ? 27 : 66);
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
            if (surface.Width<7 || surface.Height<8 || SystemInformation.HighContrast) {
                Backdrop(g,surface,palette);
                return;
            }
            int radius=Math.Max(11,surface.Font.Height*4/5);
            var window=surface.FindForm();
            if (window==null || (long)surface.Width*surface.Height>CachePixelLimit) {
                PaintPane(g,surface,radius,palette);
                return;
            }
            var pixels=Panes.GetValue(surface,owner=>new PaneBuffer(owner)).Get(surface,window,radius,palette);
            g.DrawImageUnscaled(pixels,0,0);
        }

        static void PaintPane(Graphics g,Control surface,int radius,AppPalette palette)
        {
            Backdrop(g,surface,palette);
            var box=new Rectangle(1,1,surface.Width-3,surface.Height-4);
            var state=g.Save();
            g.SmoothingMode=SmoothingMode.AntiAlias;
            var shade=box;
            shade.Offset(0,2);
            using (var shadow=Round(shade,radius))
            using (var fill=new SolidBrush(Color.FromArgb(palette.Dark ? 44 : 19,Color.FromArgb(24,37,64))))
                g.FillPath(fill,shadow);
            using (var shape=Round(box,radius)) {
                Color top=Color.FromArgb(palette.Dark ? 88 : 175,palette.Dark ? palette.Surface : Color.White);
                Color bottom=Color.FromArgb(palette.Dark ? 194 : 116,palette.Dark ? palette.Surface : Color.White);
                using (var coat=new LinearGradientBrush(box,top,bottom,96f))
                using (var rim=new LinearGradientBrush(box,Color.FromArgb(palette.Dark ? 45 : 206,Color.White),
                    Color.FromArgb(palette.Dark ? 24 : 33,palette.Ink),90f))
                using (var boundary=new Pen(rim)) {
                    g.FillPath(coat,shape);
                    g.DrawPath(boundary,shape);
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
                    top=AppPalette.Blend(action,palette.Atmosphere,pressed ? 0 : hot ? .19 : .10);
                    bottom=AppPalette.Blend(action,Color.FromArgb(15,24,59),.14);
                    lip=Color.FromArgb(107,AppPalette.Blend(palette.Atmosphere,Color.White,.56));
                } else {
                    top=Color.FromArgb(pressed ? 13 : hot ? 25 : 11,palette.Ink);
                    bottom=Color.FromArgb(pressed ? 21 : hot ? 18 : 7,palette.Ink);
                    lip=Color.FromArgb(palette.Dark ? 30 : 36,palette.Ink);
                }
                using (var fill=new LinearGradientBrush(box,top,bottom,88f))
                using (var pen=new Pen(lip)) {
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

        internal static void Segment(Graphics g,Control control,Rectangle box,int radius,AppPalette palette,bool selected,bool hot,bool pressed=false)
        {
            if ((!selected && !hot) || box.Width<4 || box.Height<4) return;
            if (!selected || SystemInformation.HighContrast) {
                Lens(g,box,radius,palette,SystemInformation.HighContrast && selected,hot,pressed);
                return;
            }
            var state=g.Save();
            g.SmoothingMode=SmoothingMode.AntiAlias;
            using (var shape=Round(box,radius)) {
                var sample=GlassRefraction.Lens(control,box,radius,palette);
                var clip=g.Save();
                g.SetClip(shape,CombineMode.Intersect);
                g.DrawImageUnscaled(sample,box.Location);
                g.Restore(clip);

                var top=Color.FromArgb((palette.Dark ? 64 : 201)+(pressed ? -14 : hot ? 18 : 0),Color.White);
                var bottom=Color.FromArgb((palette.Dark ? 19 : 81)+(pressed ? 9 : 0),Color.White);
                using (var surface=new LinearGradientBrush(box,top,bottom,109f))
                using (var rim=new LinearGradientBrush(box,
                    Color.FromArgb(palette.Dark ? 127 : 237,Color.White),
                    Color.FromArgb(palette.Dark ? 76 : 121,palette.Atmosphere),113f))
                using (var edge=new Pen(rim,Math.Max(1,box.Height/49f))) {
                    g.FillPath(surface,shape);
                    g.DrawPath(edge,shape);
                }
            }
            g.Restore(state);
        }

        internal static Color SegmentInk(AppPalette palette,bool selected,bool enabled)
        {
            if (SystemInformation.HighContrast) return ActionInk(selected,palette);
            if (!enabled || !selected) return palette.Muted;
            return palette.Dark ? palette.Ink : palette.Action;
        }

        internal static void NavigationItem(Graphics g,Rectangle box,AppPalette palette,bool selected,bool hot,int radius)
        {
            if (!selected && !hot) return;
            if (SystemInformation.HighContrast) {
                Lens(g,box,radius,palette,selected,hot);
                return;
            }
            var state=g.Save();
            g.SmoothingMode=SmoothingMode.AntiAlias;
            using (var shape=Round(box,radius))
            using (var fill=new SolidBrush(Color.FromArgb(selected ? 27 : 13,palette.Ink)))
                g.FillPath(fill,shape);
            if (selected) {
                var line=new Rectangle(box.Left+2,box.Top+box.Height/4,Math.Max(2,box.Height/17),box.Height/2);
                using (var marker=Round(line,line.Width))
                using (var brush=new SolidBrush(palette.Accent)) g.FillPath(brush,marker);
            }
            g.Restore(state);
        }

        internal static void Glyph(Graphics g,InterfaceGlyph glyph,Rectangle bounds,Color ink)
        {
            var state=g.Save();
            g.SmoothingMode=SmoothingMode.AntiAlias;
            g.TranslateTransform(bounds.X,bounds.Y);
            g.ScaleTransform(bounds.Width/20f,bounds.Height/20f);
            using (var pen=new Pen(ink,1.5f) { StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round }) {
                switch (glyph) {
                    case InterfaceGlyph.Settings:
                        g.DrawEllipse(pen,4,4,12,12);
                        g.DrawEllipse(pen,7.5f,7.5f,5,5);
                        g.DrawLine(pen,10,2,10,4); g.DrawLine(pen,10,16,10,18);
                        g.DrawLine(pen,2,10,4,10); g.DrawLine(pen,16,10,18,10);
                        break;
                    case InterfaceGlyph.Casting:
                        g.DrawLines(pen,new[] { new Point(6,14),new Point(2,14),new Point(2,4),new Point(18,4),new Point(18,14),new Point(14,14) });
                        g.DrawPolygon(pen,new[] {new Point(6,17),new Point(10,11),new Point(14,17)});
                        break;
                    case InterfaceGlyph.Appearance:
                        g.DrawEllipse(pen,3,3,14,14);
                        g.DrawArc(pen,7,3,10,14,80,200);
                        break;
                    case InterfaceGlyph.Components:
                        g.DrawPolygon(pen,new[] {new Point(2,7),new Point(10,3),new Point(18,7),new Point(10,11)});
                        g.DrawLines(pen,new[] {new Point(2,11),new Point(10,15),new Point(18,11)});
                        g.DrawLines(pen,new[] {new Point(2,15),new Point(10,19),new Point(18,15)});
                        break;
                    case InterfaceGlyph.Tools:
                        g.DrawEllipse(pen,3,3,14,14);
                        g.DrawLine(pen,10,9,10,13);
                        g.DrawLine(pen,10,6.5f,10,6.6f);
                        break;
                }
            }
            g.Restore(state);
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
                    if (rows[row]==0) continue;
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

    internal sealed class GlassRule : Control
    {
        internal GlassRule()
        {
            Dock=DockStyle.Fill;
            Margin=Padding.Empty;
            TabStop=false;
            DoubleBuffered=true;
        }

        protected override void OnPaintBackground(PaintEventArgs e) { GlassUi.ControlBackground(e.Graphics,this); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var palette=(FindForm() as IGlassHost)?.GlassPalette;
            var color=SystemInformation.HighContrast || palette==null
                ? SystemColors.WindowText : Color.FromArgb(palette.Dark ? 34 : 31,palette.Ink);
            using (var pen=new Pen(color)) e.Graphics.DrawLine(pen,0,Height/2,Width,Height/2);
        }
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
        internal bool ClearGlass { get; set; }
        internal InterfaceGlyph Symbol { get; set; }
        internal Color SwatchColor { get; set; }

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
            int radius=Math.Max(6,Font.Height/2);
            bool left=TextAlign==ContentAlignment.MiddleLeft;
            Color ink;
            if (ClearGlass) {
                GlassUi.Segment(e.Graphics,this,box,Math.Max(10,Font.Height*3/4),palette,true,Enabled && hot,pressed);
                ink=Enabled ? GlassUi.ActionInk(SystemInformation.HighContrast,palette) : palette.Muted;
            } else if (SegmentChoice) {
                if (left) GlassUi.NavigationItem(e.Graphics,box,palette,accent,Enabled && hot,radius);
                else GlassUi.Segment(e.Graphics,this,box,Math.Max(9,Font.Height*3/4),palette,accent,Enabled && hot,pressed);
                ink=GlassUi.SegmentInk(palette,accent,Enabled);
            } else {
                GlassUi.Lens(e.Graphics,box,radius,palette,Enabled && accent,Enabled && hot,pressed);
                ink=Enabled ? GlassUi.ActionInk(accent,palette) : palette.Muted;
            }
            var textBox=Rectangle.Inflate(box,-Math.Max(5,left && SegmentChoice ? Font.Height/2 : Font.Height/3),0);
            int icon=Font.Height;
            int gap=Math.Max(4,icon/3);
            if (Symbol!=InterfaceGlyph.None || !SwatchColor.IsEmpty) {
                int textWidth=TextRenderer.MeasureText(e.Graphics,Text,Font,Size.Empty,TextFormatFlags.NoPadding).Width;
                if (textBox.Width>=icon+gap+textWidth) {
                    int x=left ? textBox.Left : Math.Max(textBox.Left,box.X+(box.Width-icon-gap-textWidth)/2);
                    var glyph=new Rectangle(x,(Height-icon)/2,icon,icon);
                    if (SwatchColor.IsEmpty) GlassUi.Glyph(e.Graphics,Symbol,glyph,ink);
                    else {
                        glyph.Inflate(-2,-2);
                        using (var shape=GlassUi.Round(glyph,Math.Max(2,icon/5)))
                        using (var fill=new SolidBrush(SwatchColor))
                        using (var edge=new Pen(palette.Border)) {
                            e.Graphics.FillPath(fill,shape);
                            e.Graphics.DrawPath(edge,shape);
                        }
                    }
                    textBox=Rectangle.FromLTRB(x+icon+gap,textBox.Top,textBox.Right,textBox.Bottom);
                    left=true;
                }
            }
            TextRenderer.DrawText(e.Graphics,Text,Font,textBox,ink,
                (left ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter) | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics,Rectangle.Inflate(box,-4,-4),ink,BackColor);
        }
    }

    internal sealed class GlassToggle : CheckBox
    {
        AppPalette palette=new AppPalette(0,false);
        readonly Timer transition=new Timer { Interval=15 };
        readonly Stopwatch clock=new Stopwatch();
        double thumb, start, target;

        internal GlassToggle(string label)
        {
            Text=label;
            SetStyle(ControlStyles.Opaque | ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw,true);
            Dock=DockStyle.Fill;
            Margin=Padding.Empty;
            Cursor=Cursors.Hand;
            transition.Tick+=delegate {
                double progress=Math.Min(1,clock.Elapsed.TotalMilliseconds/145);
                thumb=start+(target-start)*(1-Math.Pow(1-progress,3));
                if (progress>=1) transition.Stop();
                Invalidate();
            };
            CheckedChanged+=delegate {
                start=thumb;
                target=Checked ? 1 : 0;
                if (IsHandleCreated && Visible && !SystemInformation.HighContrast && SystemInformation.IsMenuAnimationEnabled) {
                    clock.Restart();
                    transition.Start();
                } else {
                    transition.Stop();
                    thumb=target;
                    Invalidate();
                }
            };
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
            int x=pill.Left+3+(int)Math.Round((pill.Width-dot-6)*thumb);
            var knob=new Rectangle(x,pill.Y+3,dot,dot);
            if (SystemInformation.HighContrast) {
                using (var solid=new SolidBrush(SystemColors.WindowText)) e.Graphics.FillEllipse(solid,knob);
            } else {
                using (var lacquer=new LinearGradientBrush(knob,Color.White,AppPalette.Blend(palette.Field,Color.White,.45),LinearGradientMode.Vertical))
                using (var edge=new Pen(Color.FromArgb(165,palette.Atmosphere))) {
                    e.Graphics.FillEllipse(lacquer,knob);
                    e.Graphics.DrawEllipse(edge,knob);
                }
            }
            var label=new Rectangle(0,0,Math.Max(0,pill.Left-9),Height);
            Color ink=Enabled ? GlassUi.ActionInk(false,palette) : palette.Muted;
            TextRenderer.DrawText(e.Graphics,Text,Font,label,ink,TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics,Rectangle.Inflate(ClientRectangle,-2,-2));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) transition.Dispose();
            base.Dispose(disposing);
        }
    }
}
