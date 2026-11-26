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
            Font = Theme.Font(14);
            Dock = DockStyle.Fill;
            Margin = Padding.Empty;
            TabStop = false;
            var row = Theme.Grid(2, 1);
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            Theme.Rows(row,-1);
            usb = MakeChoice("USB 数据线", "UsbMode", true);
            air = MakeChoice("无线 AirPlay", "AirPlayMode", false);
            usb.Margin = new Padding(0,0,4,0);
            air.Margin = new Padding(4,0,0,0);
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
                Font = Font, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
                UseVisualStyleBackColor = false, Cursor = Cursors.Hand
            };
            return radio;
        }

        internal void ApplyPalette(AppPalette palette)
        {
            usb.Palette=air.Palette=palette;
            usb.Invalidate(); air.Invalidate();
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
            public ModeChoice(bool wired)
            {
                this.wired = wired;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
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
            protected override void OnPaint(PaintEventArgs e)
            {
                var g=e.Graphics;
                Color ink = Checked ? Palette.Ink : Palette.Muted;
                Color fill = Checked ? Palette.Selection : Palette.Surface;
                Color line = Checked ? Palette.Accent : Palette.Border;
                using (var brush = new SolidBrush(fill)) g.FillRectangle(brush, ClientRectangle);
                using (var pen = new Pen(line, Checked ? 2 : 1))
                    g.DrawRectangle(pen, 1, 1, Math.Max(0,Width-3), Math.Max(0,Height-3));
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
                    ControlPaint.DrawFocusRectangle(g,Rectangle.Inflate(ClientRectangle,-4,-4),ink,fill);
            }
        }
    }

    internal sealed partial class MainForm
    {
        sealed class BrandMark : Control
        {
            protected override void OnPaint(PaintEventArgs e)
            {
                var g=e.Graphics;
                float side=Math.Min(ClientSize.Width,ClientSize.Height);
                g.SmoothingMode=SmoothingMode.AntiAlias;
                g.TranslateTransform((ClientSize.Width-side)/2,(ClientSize.Height-side)/2);
                g.ScaleTransform(side/44f,side/44f);
                using (var bg=new SolidBrush(Color.FromArgb(105,91,164)))
                using (var monitor=new Pen(Color.White,2.2f))
                using (var tablet=new Pen(Color.FromArgb(173,241,208),2.2f))
                {
                    g.FillEllipse(bg,0,0,44,44);
                    monitor.LineJoin=tablet.LineJoin=LineJoin.Round;
                    g.DrawRectangle(monitor,8,11,25,18);
                    g.DrawLine(monitor,18,29,18,33);
                    g.DrawLine(monitor,13,33,22,33);
                    g.FillRectangle(bg,23,18,12,17);
                    g.DrawRectangle(tablet,23,18,12,17);
                    g.DrawLine(tablet,27,32,31,32);
                }
            }
        }

        readonly Label sessionBadge = Theme.Label("",14, ConsoleColors.Accent, true);
        TableLayoutPanel dashboard, connection, utility;
        Panel viewport;
        bool compact, arranging, arrangePending;
        DpiLayout consoleDpi;
        int consoleDpiValue=96;
        readonly ComboBox appearance = Picker();
        readonly CheckBox darkMode = new CheckBox { Text="深色模式", Dock=DockStyle.Fill, Margin=Padding.Empty };
        readonly CheckBox showToolbar = new CheckBox { Text="始终显示投屏工具栏", Dock=DockStyle.Fill, Margin=Padding.Empty };
        AppPalette palette = new AppPalette(0,false);

        void ChangeAppearance()
        {
            palette=new AppPalette(appearance.SelectedIndex,darkMode.Checked);
            palette.Apply(this,primary);
            UpdateDashboardStatus();
            preview?.ApplyPalette(palette);
        }

        internal void ScaleConsole(int dpi)
        {
            consoleDpiValue=dpi;
            if (!consoleDpi.Apply(dpi)) return;
            var area=Screen.FromControl(this).WorkingArea;
            MinimumSize=new Size(Math.Min(UiPixels(500),area.Width),Math.Min(UiPixels(310),area.Height));
            ArrangeConsole(true);
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            if (consoleDpi!=null) ScaleConsole(e.DeviceDpiNew);
        }

        void UpdateDashboardStatus()
        {
            string state = closing ? "正在退出" : WaitingForUsb ? "等待重连" : playing ? (hadVideo ? "投屏中" : "等待画面") : listening ? "等待设备" :
                core == null ? "待配置" : "就绪";
            sessionBadge.Text=state;
            sessionBadge.BackColor=playing || listening ? palette.Selection : palette.Canvas;
            sessionBadge.ForeColor=playing || listening ? palette.Accent : palette.Muted;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            palette.ApplyCaption(this);
            ScaleConsole(DpiLayout.Read(this));
            ClientSize=new Size(UiPixels(990),UiPixels(632));
            var area = Screen.FromControl(this).WorkingArea;
            MinimumSize = new Size(Math.Min(MinimumSize.Width, area.Width),Math.Min(MinimumSize.Height, area.Height));
            int width=Math.Min(Width,area.Width), height=Math.Min(Height,area.Height);
            Bounds = new Rectangle(Math.Max(area.Left,Math.Min(Left,area.Right-width)),
                Math.Max(area.Top,Math.Min(Top,area.Bottom-height)),width,height);
            ArrangeConsole(true);
        }

        void BuildControlPanel()
        {
            SuspendLayout();
            Font=Theme.Font(14); ForeColor=ConsoleColors.Ink; BackColor=ConsoleColors.Canvas;
            AutoScaleMode=AutoScaleMode.None;
            StartPosition=FormStartPosition.CenterScreen;
            MinimumSize=new Size(580,360); ClientSize = new Size(990,632);

            var page=Theme.Grid(1,2); Theme.Cols(page,-1); Theme.Rows(page,64,-1);
            var header=Theme.Grid(4,1); Theme.Cols(header,46,76,-1,122); Theme.Rows(header,-1);
            header.Name="Header";
            header.Padding=new Padding(24,0,24,0);
            var logo=new BrandMark { Dock=DockStyle.Fill, Margin=new Padding(0,10,8,10), TabStop=false };
            header.Controls.Add(logo,0,0);
            header.Controls.Add(Theme.Label("微映",25,Color.White,true),1,0);
            header.Controls.Add(Theme.Label("投屏控制台",15,Color.FromArgb(187,217,210)),2,0);
            about.Font=Theme.Font(13); about.Margin=new Padding(0,10,0,10);
            header.Controls.Add(about,3,0); page.Controls.Add(header,0,0);

            viewport=new Panel { Dock=DockStyle.Fill, Margin=Padding.Empty, AutoScroll=true, Name="Canvas" };
            dashboard=Theme.Grid(2,2); dashboard.Dock=DockStyle.Top;
            dashboard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,62));
            dashboard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,38));
            Theme.Rows(dashboard,-1,0);
            connection=BuildConnectionCard(); utility=BuildUtilityColumn();
            dashboard.Controls.Add(connection,0,0); dashboard.Controls.Add(utility,1,0);
            viewport.Controls.Add(dashboard); page.Controls.Add(viewport,0,1);
            TrackConsoleFocus(dashboard);
            Controls.Add(page);
            consoleDpi=new DpiLayout(this);
            ResumeLayout(true);
            viewport.ClientSizeChanged+=delegate {
                // Changing the rows inside a scrollbar layout can leave its old scroll range in effect.
                if (arrangePending || !IsHandleCreated || IsDisposed) return;
                arrangePending=true;
                BeginInvoke(new Action(delegate {
                    arrangePending=false;
                    if (!IsDisposed && !Disposing) ArrangeConsole(false);
                }));
            };
            ArrangeConsole(true);
        }

        void TrackConsoleFocus(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                if (child.TabStop) child.Enter+=delegate {
                    if (!IsHandleCreated || !viewport.VerticalScroll.Visible) return;
                    BeginInvoke(new Action(KeepFieldInView));
                };
                TrackConsoleFocus(child);
            }
        }

        void KeepFieldInView()
        {
            if (IsDisposed || !viewport.VerticalScroll.Visible || !viewport.ContainsFocus) return;
            Control target=ActiveControl;
            while (target is ContainerControl && ((ContainerControl)target).ActiveControl!=null)
                target=((ContainerControl)target).ActiveControl;
            if (target==null || !target.Focused) return;
            Rectangle box=viewport.RectangleToClient(target.RectangleToScreen(target.ClientRectangle));
            int shift=box.Top<0 ? box.Top : Math.Max(0,box.Bottom-viewport.ClientSize.Height);
            if (shift!=0) viewport.AutoScrollPosition=new Point(0,Math.Max(0,-viewport.AutoScrollPosition.Y+shift));
        }

        int UiPixels(int value)
        {
            return DpiLayout.Pixels(value,consoleDpiValue/96f);
        }

        void ArrangeConsole(bool force)
        {
            if (arranging || dashboard==null || viewport==null || viewport.ClientSize.Width<=0) return;
            bool stack=viewport.ClientSize.Width < UiPixels(830);
            if (!force && stack==compact) return;
            arranging=true;
            dashboard.SuspendLayout();
            try {
                compact=stack;
                dashboard.Padding=new Padding(UiPixels(stack?14:22),UiPixels(18),UiPixels(stack?14:22),UiPixels(16));
                dashboard.ColumnStyles[0].Width=stack ? 100 : 62;
                dashboard.ColumnStyles[1].Width=stack ? 0 : 38;
                // The two cards remain in the same visual/tab order across breakpoints.
                dashboard.SetCellPosition(utility,stack ? new TableLayoutPanelCellPosition(0,1) : new TableLayoutPanelCellPosition(1,0));
                dashboard.RowStyles[0].SizeType=SizeType.Absolute;
                dashboard.RowStyles[0].Height=UiPixels(stack ? 490 : 506);
                dashboard.RowStyles[1].SizeType=SizeType.Absolute;
                dashboard.RowStyles[1].Height=stack ? UiPixels(460) : 0;
                connection.Margin=new Padding(0,0,stack ? 0 : UiPixels(14),0);
                utility.Margin=new Padding(0,stack ? UiPixels(12) : 0,0,0);
                dashboard.Height=dashboard.Padding.Vertical + (int)(dashboard.RowStyles[0].Height+dashboard.RowStyles[1].Height);
                viewport.AutoScrollMinSize=new Size(0,dashboard.Height);
            } finally { dashboard.ResumeLayout(true); arranging=false; }
            if (IsHandleCreated) BeginInvoke(new Action(KeepFieldInView));
        }

        TableLayoutPanel Card()
        {
            var card=Theme.Grid(1,1);
            card.Name="Card"; card.Padding=new Padding(18,10,18,16);
            return card;
        }

        TableLayoutPanel BuildConnectionCard()
        {
            var card=Card(); card.RowCount=10; Theme.Cols(card,-1);
            Theme.Rows(card,38,48,33,42,32,43,40,44,-1,46);
            card.Controls.Add(Theme.Label("连接设备",19,ConsoleColors.Ink,true),0,0);
            card.Controls.Add(mode,0,1);

            inputLabel.Font=presetLabel.Font=Theme.Font(13,true);
            inputLabel.ForeColor=presetLabel.ForeColor=ConsoleColors.Secondary;
            card.Controls.Add(inputLabel,0,2); card.Controls.Add(input,0,3);
            quality.Items.AddRange(WirelessProfile.Choices); quality.SelectedIndex=WirelessProfile.DefaultIndex;
            foreach (var picker in new[]{devices,quality}) {
                picker.Font=Theme.Font(14); picker.ForeColor=ConsoleColors.Ink; picker.Dock=DockStyle.Top; picker.Margin=Padding.Empty;
            }
            name.Font=Theme.Font(14); input.Controls.Add(name);
            var usbPick=Theme.Grid(2,1); usbPick.Name="UsbPick"; Theme.Cols(usbPick,-1,88); Theme.Rows(usbPick,-1);
            usbPick.Controls.Add(devices,0,0); refresh.Margin=new Padding(8,0,0,0); usbPick.Controls.Add(refresh,1,0);
            input.Controls.Add(usbPick);
            card.Controls.Add(presetLabel,0,4);
            var presets=new Panel{Dock=DockStyle.Fill,Margin=Padding.Empty};
            var usbMode=Theme.Label("硬件解码优先 · 60 fps 上限",14,ConsoleColors.Ink);
            usbMode.Name="UsbPreset"; presets.Controls.Add(usbMode); presets.Controls.Add(quality);
            card.Controls.Add(presets,0,5); card.Controls.Add(audio,0,6);
            audio.Font=Theme.Font(14); hint.Font=Theme.Font(13); hint.ForeColor=ConsoleColors.Secondary;
            appearance.Items.AddRange(AppPalette.Names); appearance.SelectedIndex=0;
            appearance.Name="AppearanceTheme"; appearance.AccessibleName="界面主题"; appearance.FlatStyle=FlatStyle.Flat; appearance.Dock=DockStyle.Top;
            appearance.Font=darkMode.Font=showToolbar.Font=Theme.Font(12);
            appearance.Margin=new Padding(0,4,8,0);
            var style=Theme.Grid(3,1); Theme.Cols(style,118,110,-1); Theme.Rows(style,-1);
            style.Controls.Add(appearance,0,0); style.Controls.Add(darkMode,1,0); style.Controls.Add(showToolbar,2,0);
            card.Controls.Add(style,0,7);
            hint.Name="Muted"; card.Controls.Add(hint,0,8);
            primary.Font=Theme.Font(14,true); primary.BackColor=ConsoleColors.Accent;
            primary.FlatAppearance.BorderColor=ConsoleColors.Accent;
            card.Controls.Add(primary,0,9);
            return card;
        }

        TableLayoutPanel BuildUtilityColumn()
        {
            var tools=Theme.Grid(1,2); Theme.Cols(tools,-1); Theme.Rows(tools,178,-1);
            var session=Card(); session.RowCount=3; Theme.Cols(session,-1); Theme.Rows(session,39,-1,43);
            var heading=Theme.Grid(2,1); Theme.Cols(heading,-1,88); Theme.Rows(heading,-1);
            heading.Controls.Add(Theme.Label("接收状态",17,ConsoleColors.Ink,true),0,0);
            sessionBadge.TextAlign=ContentAlignment.MiddleCenter;
            sessionBadge.Margin=new Padding(0,6,0,6);
            heading.Controls.Add(sessionBadge,1,0); session.Controls.Add(heading,0,0);
            footer.Font=Theme.Font(13); footer.ForeColor=ConsoleColors.Secondary;
            footer.Name="Muted"; footer.UseMnemonic=false; session.Controls.Add(footer,0,1);
            session.Controls.Add(showPreview,0,2);
            session.Margin=new Padding(0,0,0,12);
            tools.Controls.Add(session,0,0);

            var files=Card(); files.RowCount=4; Theme.Cols(files,-1); Theme.Rows(files,44,-1,41,44);
            pathCaption.Font=Theme.Font(16,true); pathCaption.ForeColor=ConsoleColors.Ink;
            files.Controls.Add(pathCaption,0,0);
            componentPath.AccessibleName="投屏组件目录";
            componentPath.ReadOnly=true; componentPath.Multiline=componentPath.WordWrap=true;
            componentPath.ScrollBars=ScrollBars.Vertical; componentPath.Font=Theme.Font(13);
            componentPath.Dock=DockStyle.Fill; componentPath.Margin=new Padding(0,4,0,10);
            componentPath.BackColor=ConsoleColors.Canvas; componentPath.ForeColor=ConsoleColors.Ink;
            componentPath.BorderStyle=BorderStyle.FixedSingle;
            files.Controls.Add(componentPath,0,1); files.Controls.Add(openPath,0,2);
            driver.Margin=new Padding(0,6,0,0); files.Controls.Add(driver,0,3);
            tools.Controls.Add(files,0,1);
            return tools;
        }
    }
}
