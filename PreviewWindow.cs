using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MirrorDesk
{
    internal sealed partial class PreviewWindow : Form
    {
        static readonly Color BarColor = Color.FromArgb(21, 34, 43);
        static readonly Color ButtonColor = Color.FromArgb(41, 59, 69);
        static readonly Color ButtonEdge = Color.FromArgb(73, 97, 108);
        static readonly Color ActiveColor = Color.FromArgb(15, 92, 87);
        static readonly Color LightText = Color.FromArgb(235, 245, 246);
        static readonly Color ActiveText = Color.FromArgb(198, 252, 232);
        readonly Panel toolbar = new Panel { Name="PreviewToolbar", BackColor=BarColor, Visible=false };
        readonly Panel stage = new Panel { Dock = DockStyle.None, BackColor = Color.Black };
        readonly Panel surface = new Panel { Name = "VideoSurface", Dock = DockStyle.Fill, BackColor = Color.Black };
        readonly Label overlay = Theme.Label("", 16, Color.FromArgb(222, 235, 236));
        readonly Label stats = Theme.Label("", 13, LightText);
        readonly Label feedback = Theme.Label("", 13, LightText);
        readonly Timer toolbarTimer = new Timer { Interval = 100 };
        readonly Timer feedbackTimer = new Timer { Interval = 4000 };
        readonly Button pin = new Button { Name = "PinPreview", Text = "置顶" };
        readonly Button snapshot = new Button { Name = "SaveSnapshot", Text = "截图" };
        readonly Button full = new Button { Name = "FullPreview", Text = "全屏" };
        readonly Button aspect = new Button { Name = "FollowAspect", Text = "跟随比例", AccessibleName = "窗口随画面比例调整" };
        readonly ToolTip tips = new ToolTip();
        DpiLayout previewDpi;
        AppPalette palette;
        bool showToolbar;
        bool closePending, controllerClosing, snapshotAvailable, snapshotBusy, layingOut;
        string currentStats = "", snapshotMessage = "";
        Rectangle previousBounds;
        FormWindowState previousState;

        public event EventHandler ToolbarPreferenceChanged;
        public bool AlwaysShowToolbar
        {
            get { return showToolbar; }
            set {
                if (showToolbar==value) return;
                showToolbar=value;
                LayoutTools();
                UpdateToolbar();
                if (geometryReady) {
                    preferredVideoArea=0;
                    pendingFit=followAspect && !sourceSize.IsEmpty;
                    FitIfPending();
                }
                ToolbarPreferenceChanged?.Invoke(this,EventArgs.Empty);
            }
        }
        public event EventHandler StopRequested;
        public event EventHandler PinChanged;
        public event EventHandler SnapshotRequested;
        public Panel Surface { get { return surface; } }
        public bool IsFullscreen { get; private set; }
        public bool Pinned
        {
            get { return TopMost; }
            set
            {
                if (TopMost == value) return;
                TopMost = value;
                pin.Text = value ? "已置顶" : "置顶";
                StyleButton(pin,value);
                tips.SetToolTip(pin,value ? "取消窗口置顶" : "让画面显示在其他窗口之上");
                PinChanged?.Invoke(this,EventArgs.Empty);
            }
        }

        public PreviewWindow()
        {
            Text = "微映投屏";
            Font = Theme.Font(13);
            AutoScaleMode = AutoScaleMode.None;
            KeyPreview = true;
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(960, 600);
            MinimumSize = new Size(360, 240);
            BackColor = Color.Black;

            stats.Name = "PreviewStats";
            stats.Dock = DockStyle.None;
            stats.UseMnemonic = false;
            foreach (var button in new[] { pin, snapshot, aspect, full }) {
                button.Font = Font;
                button.FlatStyle = FlatStyle.Flat;
                button.UseVisualStyleBackColor = false;
                button.Cursor = Cursors.Hand;
                button.TextImageRelation = TextImageRelation.ImageBeforeText;
                button.TextAlign = ContentAlignment.MiddleCenter;
                StyleButton(button,false);
            }
            pin.AccessibleDescription = "切换画面窗口置顶";
            snapshot.AccessibleDescription = "将当前画面保存为 PNG，快捷键 Ctrl+S";
            full.AccessibleDescription = "切换全屏，快捷键 F11";
            tips.SetToolTip(pin,"让画面显示在其他窗口之上");
            tips.SetToolTip(full,"全屏 F11，退出全屏 Esc");
            tips.SetToolTip(snapshot,"保存当前画面为 PNG  ·  Ctrl+S");
            pin.Click += delegate { Pinned = !Pinned; };
            snapshot.Click += delegate { RequestSnapshot(); };
            full.Click += delegate { ToggleFullscreen(); };
            aspect.Click += delegate { ChangeAspectMode(); };
            UpdateAspectButton();
            toolbar.Controls.AddRange(new Control[] { stats, pin, snapshot, aspect, full });
            ClientSizeChanged += delegate { LayoutTools(); };
            snapshot.Enabled = false;

            overlay.Name = "PreviewOverlay";
            overlay.BackColor = Theme.Stage;
            overlay.TextAlign = ContentAlignment.MiddleCenter;
            overlay.Padding = new Padding(20);
            overlay.Visible = false;
            stage.Controls.Add(surface);
            stage.Controls.Add(overlay);
            feedback.Name = "SnapshotFeedback";
            feedback.Dock = DockStyle.None;
            feedback.UseMnemonic = false;
            feedback.Visible = false;
            feedback.BackColor = BarColor;
            stage.Controls.Add(feedback);
            overlay.BringToFront();
            stage.SizeChanged += delegate { LayoutFeedback(); };
            feedbackTimer.Tick += delegate {
                feedbackTimer.Stop();
                snapshotMessage = "";
                DisplayFeedback();
            };
            Controls.Add(stage);
            Controls.Add(toolbar);
            toolbar.BringToFront();
            toolbarTimer.Tick+=delegate { UpdateToolbar(); };
            Activated+=delegate { UpdateToolbar(); };
            Deactivate+=delegate { if (!showToolbar) SetToolbarVisible(false); };
            previewDpi=new DpiLayout(this);

            surface.DoubleClick += delegate { ToggleFullscreen(); };
            overlay.DoubleClick += delegate { ToggleFullscreen(); };
        }

        protected override void OnLoad(EventArgs e)
        {
            ScalePreview(DpiLayout.Read(this));
            LoadPlacement();
            palette?.ApplyCaption(this);
            toolbarTimer.Start();
            LayoutTools();
            base.OnLoad(e);
        }

        void ScalePreview(int dpi)
        {
            if (!previewDpi.Apply(dpi)) return;
            designMinimum=new Size(DpiLayout.Pixels(360,dpi/96f),DpiLayout.Pixels(240,dpi/96f));
            LimitMinimum(Screen.FromControl(this).WorkingArea);
            CreateButtonIcons();
            LayoutTools();
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            if (previewDpi!=null) ScalePreview(e.DeviceDpiNew);
        }

        public void ApplyPalette(AppPalette next)
        {
            palette=next;
            palette.ApplyCaption(this);
            toolbar.BackColor=feedback.BackColor=next.Header;
            foreach (var button in new[]{pin,snapshot,aspect,full})
                StyleButton(button,button==pin ? Pinned : button==aspect && followAspect);
        }

        void StyleButton(Button button, bool active)
        {
            if (palette!=null) {
                palette.StyleButton(button,active,!active);
                return;
            }
            button.ForeColor = active ? ActiveText : LightText;
            button.BackColor = active ? ActiveColor : ButtonColor;
            button.FlatAppearance.BorderColor = active ? Color.FromArgb(86,179,157) : ButtonEdge;
            button.FlatAppearance.MouseOverBackColor = active ? Color.FromArgb(18,108,100) : Color.FromArgb(53,77,87);
            button.FlatAppearance.MouseDownBackColor = active ? Color.FromArgb(20,121,111) : Color.FromArgb(61,88,100);
        }

        void CreateButtonIcons()
        {
            int size = Math.Max(16, Font.Height-3);
            foreach (var button in new[] { pin, snapshot, aspect, full })
            {
                var bitmap = new Bitmap(size,size);
                using (var g = Graphics.FromImage(bitmap))
                using (var pen = new Pen(LightText,1.6f))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.ScaleTransform(size/24f,size/24f);
                    pen.StartCap=pen.EndCap=LineCap.Round;
                    pen.LineJoin=LineJoin.Round;
                    if (button == pin) {
                        g.DrawPolygon(pen,new[] { new Point(8,4),new Point(16,4),new Point(15,7),new Point(15,11),new Point(18,14),new Point(6,14),new Point(9,11),new Point(9,7) });
                        g.DrawLine(pen,12,14,12,20);
                    } else if (button == snapshot) {
                        g.DrawPolygon(pen,new[] { new Point(3,8),new Point(7,8),new Point(9,5),new Point(15,5),new Point(17,8),new Point(21,8),new Point(21,19),new Point(3,19) });
                        g.DrawEllipse(pen,8.5f,9.5f,7,7);
                    } else if (button == aspect) {
                        g.DrawRectangle(pen,2,4,15,12);
                        g.DrawRectangle(pen,13,8,9,13);
                    } else {
                        g.DrawLines(pen,new[] { new Point(4,10),new Point(4,5),new Point(9,5) });
                        g.DrawLines(pen,new[] { new Point(15,5),new Point(20,5),new Point(20,10) });
                        g.DrawLines(pen,new[] { new Point(20,14),new Point(20,19),new Point(15,19) });
                        g.DrawLines(pen,new[] { new Point(9,19),new Point(4,19),new Point(4,14) });
                    }
                }
                var old = button.Image; button.Image = bitmap; old?.Dispose();
            }
        }

        // Width breakpoints also drive the prospective video size before a device rotates.
        int ArrangeToolbar(int width,bool apply)
        {
            int gap=Math.Max(5,Font.Height/4), outer=2*gap, h=Font.Height+3*gap;
            int icon=pin.Image==null ? Font.Height : pin.Image.Width;
            int pinWidth=TextRenderer.MeasureText("已置顶",Font).Width+icon+3*gap;
            int other=TextRenderer.MeasureText("截图",Font).Width+icon+3*gap;
            int aspectWidth=TextRenderer.MeasureText(aspect.Text,Font).Width+icon+3*gap;
            int group=pinWidth+2*other+aspectWidth+3*gap, available=Math.Max(1,width-2*outer);
            bool stacked=available<group+gap+TextRenderer.MeasureText("2732 × 2048   120 fps",stats.Font).Width;
            bool doubleRow=available<group;
            int top=stacked ? stats.Font.Height+2*gap : gap;
            int height=top+h+gap+(doubleRow ? h+gap : 0);
            if (!apply) return height;
            stats.SetBounds(outer,gap,stacked ? available : available-group-gap,stacked ? stats.Font.Height : h);
            var buttons=new[]{pin,snapshot,aspect,full};
            var widths=new[]{pinWidth,other,aspectWidth,other};
            int x=stacked ? outer : width-outer-group;
            for (int i=0;i<buttons.Length;i++) {
                int w=doubleRow ? Math.Max(1,(available-gap)/2) : widths[i];
                if (doubleRow && i==2) { x=outer; top+=h+gap; }
                buttons[i].SetBounds(x,top,w,h);
                x+=w+gap;
            }
            return height;
        }

        int MeasureToolbarHeight(int width) { return showToolbar && !IsFullscreen ? ArrangeToolbar(width,false) : 0; }

        void UpdateToolbar()
        {
            if (IsDisposed || toolbar==null || !IsHandleCreated || !Visible) return;
            if (IsFullscreen || WindowState==FormWindowState.Minimized) { SetToolbarVisible(false); return; }
            Point mouse=PointToClient(Cursor.Position);
            bool overTop=Form.ActiveForm==this && ClientRectangle.Contains(mouse) &&
                mouse.Y < (toolbar.Visible ? toolbar.Height : Math.Max(7,Font.Height/2));
            SetToolbarVisible(showToolbar || overTop);
        }

        void SetToolbarVisible(bool visible)
        {
            if (toolbar.Visible==visible) return;
            toolbar.Visible=visible;
            DisplayFeedback();
        }
        void LayoutTools()
        {
            if (layingOut || toolbar==null || stage==null || ClientSize.Width<=0) return;
            layingOut=true;
            try {
                int width=ClientSize.Width;
                toolbar.SetBounds(0,0,width,ArrangeToolbar(width,true));
                int offset=MeasureToolbarHeight(width);
                stage.SetBounds(0,offset,width,Math.Max(0,ClientSize.Height-offset));
            }
            finally { layingOut=false; }
        }

        public void SetSnapshotAvailable(bool available)
        {
            snapshotAvailable=available;
            snapshot.Enabled=available && !snapshotBusy && !closePending && !controllerClosing;
        }

        public void SetSnapshotBusy(bool busy)
        {
            snapshotBusy=busy;
            SetSnapshotAvailable(snapshotAvailable);
        }

        void RequestSnapshot()
        {
            if (snapshot.Enabled && !IsDisposed && !closePending && !controllerClosing)
                SnapshotRequested?.Invoke(this,EventArgs.Empty);
        }

        public string ChooseSnapshotPath()
        {
            if (IsDisposed || closePending || controllerClosing) return null;
            using (var dialog = new SaveFileDialog {
                Title="保存投屏截图", Filter="PNG 图片 (*.png)|*.png", DefaultExt="png",
                AddExtension=true, OverwritePrompt=true, RestoreDirectory=true,
                FileName="微映-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+".png"
            }) {
                return dialog.ShowDialog(this)==DialogResult.OK ? dialog.FileName : null;
            }
        }

        public void SetSource(string name)
        {
            Text = String.IsNullOrWhiteSpace(name) ? "微映投屏" : name + " · 微映投屏";
        }

        public void SetStats(string text)
        {
            currentStats = text ?? "";
            if (snapshotMessage.Length==0) stats.Text=currentStats;
        }

        public void ShowSnapshotResult(string message)
        {
            if (IsDisposed || controllerClosing || closePending) return;
            snapshotMessage = message ?? "";
            feedbackTimer.Stop();
            DisplayFeedback();
            if (snapshotMessage.Length!=0) feedbackTimer.Start();
        }

        void DisplayFeedback()
        {
            stats.Text = snapshotMessage.Length==0 ? currentStats : snapshotMessage;
            tips.SetToolTip(stats,snapshotMessage);
            feedback.Text = snapshotMessage;
            feedback.Visible = !toolbar.Visible && snapshotMessage.Length!=0;
            if (feedback.Visible) { LayoutFeedback(); feedback.BringToFront(); }
        }

        void LayoutFeedback()
        {
            if (snapshotMessage.Length==0) return;
            int pad=Math.Max(8,feedback.Font.Height/2);
            int width=Math.Min(Math.Max(1,stage.ClientSize.Width-2*pad),
                TextRenderer.MeasureText(snapshotMessage,feedback.Font).Width+2*pad);
            feedback.Padding=new Padding(pad,0,pad,0);
            feedback.SetBounds(Math.Max(0,stage.ClientSize.Width-width-pad),pad,width,feedback.Font.Height+2*pad);
        }

        public void SetOverlay(string text)
        {
            bool show = !String.IsNullOrEmpty(text);
            overlay.Text = text ?? "";
            if (show) overlay.BringToFront();
            overlay.Visible = show;
            if (feedback.Visible) feedback.BringToFront();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData==Keys.F8) { AlwaysShowToolbar=!AlwaysShowToolbar; return true; }
            if (keyData==(Keys.Control|Keys.R)) { ChangeAspectMode(); return true; }
            if (keyData == (Keys.Control | Keys.S))
            {
                RequestSnapshot();
                return true;
            }
            if (keyData == Keys.F11 || (keyData == Keys.Escape && IsFullscreen))
            {
                ToggleFullscreen();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        public void ToggleFullscreen()
        {
            if (controllerClosing || closePending || IsDisposed) return;
            arrangingWindow=true; SuspendLayout();
            try
            {
                if (IsFullscreen)
                {
                    FormBorderStyle = FormBorderStyle.Sizable;
                    Bounds = previousBounds;
                    WindowState = previousState;
                    IsFullscreen = false;
                }
                else
                {
                    var screen = Screen.FromControl(this);
                    previousState = WindowState == FormWindowState.Maximized ? FormWindowState.Maximized : FormWindowState.Normal;
                    previousBounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
                    WindowState = FormWindowState.Normal;
                    toolbar.Visible = false;
                    FormBorderStyle = FormBorderStyle.None;
                    Bounds = screen.Bounds;
                    IsFullscreen = true;
                }
            }
            finally { ResumeLayout(true); arrangingWindow=false; LayoutTools(); UpdateToolbar(); DisplayFeedback(); }
            FitIfPending(); RecordPlacement();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (controllerClosing || StopRequested == null) return;
            // Keep the target HWND valid while the controller detaches native capture.
            e.Cancel = true;
            if (closePending) return;
            closePending = true;
            BeginInvoke(new Action(delegate {
                if (!IsDisposed && !controllerClosing) StopRequested?.Invoke(this, EventArgs.Empty);
            }));
        }

        public void CloseFromController()
        {
            if (IsDisposed) return;
            controllerClosing = true;
            Close();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) {
                toolbarTimer.Dispose();
                feedbackTimer.Dispose();
                tips.Dispose();
                foreach (var button in new[] { pin, snapshot, aspect, full }) {
                    var img=button.Image;
                    if (img!=null) { button.Image=null; img.Dispose(); }
                }
            }
            base.Dispose(disposing);
        }
    }
}
