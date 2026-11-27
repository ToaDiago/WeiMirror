using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MirrorDesk
{
    internal static class ConsoleColors
    {
        public static readonly Color Header = Color.FromArgb(25, 52, 58);
        public static readonly Color Accent = Color.FromArgb(8, 124, 116);
        public static readonly Color Ink = Color.FromArgb(24, 47, 50);
        public static readonly Color Secondary = Color.FromArgb(71, 98, 96);
        public static readonly Color Canvas = Color.FromArgb(239, 245, 243);
        public static readonly Color Mint = Color.FromArgb(220, 239, 233);
        public static readonly Color Line = Color.FromArgb(208, 222, 218);
    }

    internal sealed class TransportPicker : UserControl
    {
        readonly ModeChoice usb, air;
        AppPalette palette=new AppPalette(0,false);
        int index;
        public event EventHandler SelectedIndexChanged;
        public int SelectedIndex
        {
            get { return index; }
            set
            {
                if (value != 0 && value != 1) throw new ArgumentOutOfRangeException("value");
                if (index == value) return;
                index=value;
                usb.SetSelected(value==0); air.SetSelected(value==1);
                SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public TransportPicker()
        {
            AccessibleName = "连接方式";
            AccessibleRole = AccessibleRole.Grouping;
            Font = Theme.Font(11,true);
            Dock = DockStyle.Fill;
            Margin = Padding.Empty;
            Padding=new Padding(4);
            TabStop = false;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw,true);
            var row = GlassUi.Grid(2, 1);
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            Theme.Rows(row,-1);
            usb = MakeChoice("USB 数据线", "UsbMode", true);
            air = MakeChoice("无线 AirPlay", "AirPlayMode", false);
            row.Controls.Add(usb, 0, 0);
            row.Controls.Add(air, 1, 0);
            Controls.Add(row);
            usb.Click += delegate { SelectedIndex=0; };
            air.Click += delegate { SelectedIndex=1; };
            usb.SetSelected(true); air.SetSelected(false);
        }

        ModeChoice MakeChoice(string text, string name, bool wired)
        {
            var radio = new ModeChoice(wired) {
                Text = text, Name = name, AccessibleName = text,
                AccessibleRole=AccessibleRole.RadioButton, FlatStyle = FlatStyle.Flat,
                Dock = DockStyle.Fill, Margin=Padding.Empty, TextAlign = ContentAlignment.MiddleCenter,
                UseVisualStyleBackColor = false, Cursor = Cursors.Hand
            };
            return radio;
        }

        internal void ApplyPalette(AppPalette palette)
        {
            this.palette=usb.Palette=air.Palette=palette;
            Invalidate();
            usb.Invalidate(); air.Invalidate();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            GlassUi.ControlBackground(e.Graphics,this);
            GlassUi.Tray(e.Graphics,this,palette);
        }

        protected override bool ProcessCmdKey(ref Message message, Keys key)
        {
            if (ContainsFocus && (key==Keys.Right || key==Keys.Left || key==Keys.Up || key==Keys.Down))
            {
                int next=1-index;
                (next==0 ? usb : air).Focus();
                SelectedIndex=next;
                return true;
            }
            return base.ProcessCmdKey(ref message,key);
        }

        sealed class ModeChoice : Button
        {
            readonly bool wired;
            public AppPalette Palette = new AppPalette(0,false);
            public bool Checked { get; private set; }
            bool hot;
            public ModeChoice(bool wired)
            {
                this.wired = wired;
                SetStyle(ControlStyles.Opaque | ControlStyles.UserPaint |
                    ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer,true);
            }
            public void SetSelected(bool selected)
            {
                Checked=TabStop=selected;
                Invalidate();
                if (IsHandleCreated) AccessibilityNotifyClients(AccessibleEvents.StateChange,-1);
            }
            sealed class ChoiceAccessibility : ControlAccessibleObject
            {
                readonly ModeChoice choice;
                public ChoiceAccessibility(ModeChoice choice) : base(choice) { this.choice=choice; }
                public override AccessibleRole Role { get { return AccessibleRole.RadioButton; } }
                public override AccessibleStates State {
                    get { return base.State | (choice.Checked ? AccessibleStates.Checked : AccessibleStates.None); }
                }
                public override string DefaultAction { get { return "选择"; } }
                public override void DoDefaultAction() { if (choice.Enabled) choice.PerformClick(); }
            }
            protected override AccessibleObject CreateAccessibilityInstance() { return new ChoiceAccessibility(this); }
            protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hot=true; Invalidate(); }
            protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hot=false; Invalidate(); }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g=e.Graphics;
                GlassUi.ControlBackground(g,this);
                Color ink=GlassUi.SegmentInk(Palette,Checked,Enabled);
                if (Width<4 || Height<4) return;
                GlassUi.Segment(g,this,Rectangle.Inflate(ClientRectangle,-2,-2),Math.Max(9,Font.Height*3/4),
                    Palette,Checked,hot && Enabled);
                int icon = Math.Max(18, Font.Height * 4 / 5);
                int gap = Math.Max(5, icon / 3);
                int tw = TextRenderer.MeasureText(g,Text,Font,Size.Empty,TextFormatFlags.NoPadding).Width;
                bool drawIcon = tw + icon + gap + 12 < Width;
                int start = Math.Max(4,(Width - tw - (drawIcon ? icon+gap : 0))/2);
                if (drawIcon)
                {
                    var saved = g.Save();
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TranslateTransform(start, (Height-icon)/2f);
                    g.ScaleTransform(icon/24f, icon/24f);
                    using (var pen = new Pen(ink,1.8f)) {
                        pen.StartCap=pen.EndCap=LineCap.Round; pen.LineJoin=LineJoin.Round;
                        if (wired) {
                            g.DrawRectangle(pen, 6, 2, 12, 7);
                            g.DrawLine(pen,9,4,9,6); g.DrawLine(pen,15,4,15,6);
                            g.DrawLines(pen,new[]{new Point(4,10),new Point(4,15),new Point(8,18),new Point(16,18),new Point(20,15),new Point(20,10),new Point(4,10)});
                            g.DrawLine(pen,12,18,12,22);
                        } else {
                            g.DrawLines(pen,new[]{new Point(6,17),new Point(2,17),new Point(2,4),new Point(22,4),new Point(22,17),new Point(18,17)});
                            g.DrawPolygon(pen, new[]{new Point(6,21),new Point(12,13),new Point(18,21)});
                        }
                    }
                    g.Restore(saved);
                    start += icon + gap;
                }
                TextRenderer.DrawText(g, Text, Font, new Rectangle(start,0,Math.Max(0,Width-start-4),Height),ink,
                    TextFormatFlags.VerticalCenter|TextFormatFlags.Left|TextFormatFlags.NoPadding|TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine);
                if (Focused && ShowFocusCues)
                    ControlPaint.DrawFocusRectangle(g,Rectangle.Inflate(ClientRectangle,-4,-4),ink,BackColor);
            }
        }
    }

    internal sealed class ConsoleSurface : TableLayoutPanel
    {
        AppPalette palette = new AppPalette(0,false);

        public ConsoleSurface(int columns,int rows)
        {
            ColumnCount=columns; RowCount=rows;
            Dock=DockStyle.Fill; Margin=Padding.Empty;
            GrowStyle=TableLayoutPanelGrowStyle.FixedSize;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint,true);
        }

        public void ApplyPalette(AppPalette next)
        {
            palette=next;
            Invalidate();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            GlassUi.Pane(e.Graphics,this,palette);
        }
    }

    internal sealed partial class MainForm
    {
        sealed class BrandMark : Control
        {
            public AppPalette Palette=new AppPalette(0,false);

            public BrandMark()
            {
                DoubleBuffered=true;
                SetStyle(ControlStyles.ResizeRedraw,true);
            }

            protected override void OnPaintBackground(PaintEventArgs e) { GlassUi.ControlBackground(e.Graphics,this); }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g=e.Graphics;
                float side=Math.Min(ClientSize.Width,ClientSize.Height);
                if (side<1) return;
                g.SmoothingMode=SmoothingMode.AntiAlias;
                g.TranslateTransform((ClientSize.Width-side)/2,(ClientSize.Height-side)/2);
                g.ScaleTransform(side/40f,side/40f);
                GlassUi.Lens(g,new Rectangle(1,1,38,38),13,Palette,true,false);
                using (var bg=new SolidBrush(Palette.Action))
                using (var monitor=new Pen(Color.FromArgb(241,248,255),1.55f)) {
                    monitor.LineJoin=LineJoin.Round;
                    using (var desktop=GlassUi.Round(new Rectangle(8,9,22,15),3)) g.DrawPath(monitor,desktop);
                    g.DrawLine(monitor,16,24,16,29);
                    g.DrawLine(monitor,12,29,20,29);
                    using (var phone=GlassUi.Round(new Rectangle(22,16,10,15),3)) {
                        g.FillPath(bg,phone);
                        g.DrawPath(monitor,phone);
                    }
                }
            }
        }

        sealed class ReceptionBadge : GlassLabel
        {
            public Color Signal;

            protected override void OnPaint(PaintEventArgs e)
            {
                int size=Math.Max(5,Font.Height/3);
                int textWidth=TextRenderer.MeasureText(e.Graphics,Text,Font,Size.Empty,TextFormatFlags.NoPadding).Width;
                int inset=Math.Max(5,Font.Height/3);
                int x=Math.Max(inset,Width-textWidth-size-3*inset);
                int height=Math.Min(Height-2,Font.Height+inset);
                e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
                var box=new Rectangle(x-inset,(Height-height)/2,Width-x+inset-2,height);
                if (box.Width>3 && box.Height>3 && !SystemInformation.HighContrast)
                    using (var pill=GlassUi.Round(box,height/2))
                    using (var tint=new SolidBrush(Color.FromArgb(34,Signal))) e.Graphics.FillPath(tint,pill);
                using (var color=new SolidBrush(Signal)) e.Graphics.FillEllipse(color,x,(Height-size)/2,size,size);
                TextRenderer.DrawText(e.Graphics,Text,Font,new Rectangle(x+size+6,0,
                    Math.Max(0,Width-x-size-6),Height),ForeColor,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            }
        }

        readonly ReceptionBadge sessionBadge = new ReceptionBadge { Dock=DockStyle.Fill, Margin=Padding.Empty };
        readonly Label airReceiver = GlassUi.Label("",11,Theme.Ink);
        readonly Button settingsButton = new GlassButton("设置") { Symbol=InterfaceGlyph.Settings,ClearGlass=true };
        readonly BrandMark logo = new BrandMark();
        TableLayoutPanel dashboard, connectionActions;
        Panel viewport;
        DpiLayout consoleDpi;
        int consoleDpiValue=96;
        AppPalette palette = new AppPalette(0,false);
        AppPalette IGlassHost.GlassPalette { get { return palette; } }

        void ChangeAppearance()
        {
            palette=new AppPalette(appearance.SelectedIndex,darkMode.Checked);
            palette.Apply(this,primary);
            logo.Palette=palette;
            logo.Invalidate();
            settingsDialog.ApplyPalette(palette);
            UpdateDashboardStatus();
            preview?.ApplyPalette(palette);
            Invalidate(true);
        }

        internal void ScaleConsole(int dpi)
        {
            consoleDpiValue=dpi;
            if (!consoleDpi.Apply(dpi,InterfaceScale,TextScale,InterfaceFont)) return;
            var area=Screen.FromControl(this).WorkingArea;
            MinimumSize=new Size(Math.Min(UiPixels(398),area.Width),Math.Min(UiPixels(300),area.Height));
            ArrangeConnectionActions();
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            if (consoleDpi!=null) ScaleConsole(e.DeviceDpiNew);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ArrangeConnectionActions();
        }

        void ArrangeConnectionActions()
        {
            if (consoleDpi==null) return;
            bool stacked=ClientSize.Width<UiPixels(463);
            if (connectionActions.GetRow(primary)!=(stacked ? 1 : 0)) {
                connectionActions.SuspendLayout();
                if (stacked) {
                    connectionActions.SetRow(primary,1);
                    connectionActions.SetColumnSpan(hint,2);
                } else {
                    connectionActions.SetColumnSpan(hint,1);
                    connectionActions.SetRow(primary,0);
                }
                connectionActions.ResumeLayout();
            }
            connectionActions.RowStyles[1].Height=stacked ? UiPixels(50) : 0;
            dashboard.RowStyles[0].Height=UiPixels(stacked ? 266 : 219);
            dashboard.Height=UiPixels(stacked ? 384 : 337);
            viewport.AutoScrollMinSize=new Size(0,dashboard.Height);
        }

        void UpdateDashboardStatus()
        {
            string state = closing ? "正在退出" : WaitingForUsb ? "等待重连" : playing ? (hadVideo ? "投屏中" : "等待画面") : listening ? "等待设备" :
                core == null ? "待配置" : "就绪";
            sessionBadge.Text=state;
            sessionBadge.Signal=playing || listening ? palette.Accent : palette.Action;
            sessionBadge.ForeColor=palette.Ink;
            sessionBadge.Invalidate();
        }

        protected override void OnPaintBackground(PaintEventArgs e) { GlassUi.Backdrop(e.Graphics,this,palette); }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            palette.ApplyCaption(this,true);
            ScaleConsole(DpiLayout.Read(this));
            FitConsole();
        }

        void FitConsole()
        {
            var area=Screen.FromControl(this).WorkingArea;
            ClientSize=new Size(UiPixels(588),UiPixels(406));
            int width=Math.Min(Width,area.Width), height=Math.Min(Height,area.Height);
            Bounds=new Rectangle(Math.Max(area.Left,Math.Min(Left,area.Right-width)),
                Math.Max(area.Top,Math.Min(Top,area.Bottom-height)),width,height);
        }

        void BuildControlPanel()
        {
            SuspendLayout();
            Font=Theme.Font(10.5f); ForeColor=ConsoleColors.Ink; BackColor=ConsoleColors.Canvas;
            AutoScaleMode=AutoScaleMode.None;
            DoubleBuffered=true;
            SetStyle(ControlStyles.ResizeRedraw,true);
            StartPosition=FormStartPosition.CenterScreen;
            MinimumSize=new Size(398,300); ClientSize=new Size(588,406);

            var page=GlassUi.Grid(1,2); Theme.Cols(page,-1); Theme.Rows(page,67,-1);
            var header=GlassUi.Grid(4,1); Theme.Cols(header,48,86,-1,81); Theme.Rows(header,-1);
            header.Padding=new Padding(27,0,27,0);
            logo.Dock=DockStyle.Fill; logo.Margin=new Padding(0,12,9,12); logo.TabStop=false;
            header.Controls.Add(logo,0,0);
            var brandTitle=GlassUi.Label("微映",17,Theme.Ink,true);
            brandTitle.Name="BrandTitle";
            header.Controls.Add(brandTitle,1,0);
            var subtitle=GlassUi.Label("iPhone / iPad",10,Theme.Muted);
            subtitle.Name="Muted";
            header.Controls.Add(subtitle,2,0);
            settingsButton.Name="OpenSettings";
            settingsButton.Font=Theme.Font(10.5f,true); settingsButton.Margin=new Padding(0,15,0,14);
            settingsButton.Click+=delegate { ShowSettings(); };
            header.Controls.Add(settingsButton,3,0); page.Controls.Add(header,0,0);

            viewport=new GlassPanel { Dock=DockStyle.Fill, Margin=Padding.Empty, AutoScroll=true, Name="Canvas" };
            dashboard=GlassUi.Grid(1,3); dashboard.Dock=DockStyle.Top; dashboard.Height=337;
            dashboard.Padding=new Padding(21,3,21,11);
            Theme.Cols(dashboard,-1); Theme.Rows(dashboard,219,11,93);
            dashboard.Controls.Add(BuildConnectionCard(),0,0);
            dashboard.Controls.Add(BuildSessionCard(),0,2);
            viewport.Controls.Add(dashboard); page.Controls.Add(viewport,0,1);
            Controls.Add(page);
            consoleDpi=new DpiLayout(this);
            BuildSettings();
            ResumeLayout(true);
        }

        int UiPixels(int value)
        {
            return DpiLayout.Pixels(value,consoleDpiValue/96f*InterfaceScale);
        }

        ConsoleSurface BuildConnectionCard()
        {
            var card=new ConsoleSurface(1,6) { Padding=new Padding(20,13,20,17) };
            Theme.Cols(card,-1); Theme.Rows(card,29,45,9,39,9,-1);
            var title=GlassUi.Label("连接设备",13,Theme.Ink,true);
            card.Controls.Add(title,0,0);
            card.Controls.Add(mode,0,1);

            devices.Font=Theme.Font(11);
            devices.AccessibleName="USB 设备";
            devices.Dock=DockStyle.None;
            devices.Anchor=AnchorStyles.Left | AnchorStyles.Right;
            var usbPick=GlassUi.Grid(2,1); usbPick.Name="UsbPick"; Theme.Cols(usbPick,-1,81); Theme.Rows(usbPick,-1);
            refresh.Font=Theme.Font(10.5f); refresh.Margin=new Padding(8,0,0,0);
            usbPick.Controls.Add(devices,0,0); usbPick.Controls.Add(refresh,1,0);
            input.Controls.Add(airReceiver); input.Controls.Add(usbPick);
            card.Controls.Add(input,0,3);

            hint.Font=Theme.Font(10); hint.Name="Muted";
            hint.TextChanged+=delegate { detail.SetToolTip(hint,hint.Text); };
            connectionActions=GlassUi.Grid(2,2);
            Theme.Cols(connectionActions,-1,187); Theme.Rows(connectionActions,-1,0);
            hint.Margin=new Padding(0,0,12,0);
            connectionActions.Controls.Add(hint,0,0);
            primary.Font=Theme.Font(11,true);
            primary.Margin=new Padding(0,7,0,7);
            connectionActions.Controls.Add(primary,1,0);
            card.Controls.Add(connectionActions,0,5);
            return card;
        }

        ConsoleSurface BuildSessionCard()
        {
            var card=new ConsoleSurface(2,2) { Padding=new Padding(20,9,20,14) };
            Theme.Cols(card,-1,152); Theme.Rows(card,28,-1);
            var title=GlassUi.Label("接收状态",11.5f,Theme.Ink,true);
            card.Controls.Add(title,0,0);
            sessionBadge.Font=Theme.Font(10.5f,true);
            card.Controls.Add(sessionBadge,1,0);
            footer.Font=Theme.Font(10.5f);
            footer.Name="Muted"; footer.UseMnemonic=false;
            footer.Margin=new Padding(0,0,9,0);
            card.Controls.Add(footer,0,1);
            showPreview.Font=Theme.Font(10);
            showPreview.Margin=new Padding(0,3,0,2);
            card.Controls.Add(showPreview,1,1);
            return card;
        }
    }
}
