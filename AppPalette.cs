using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MirrorDesk
{
    internal sealed class PalettePicker : ComboBox
    {
        AppPalette palette=new AppPalette(0,false);
        string emptyText="";
        bool nativeDarkRequested;

        public PalettePicker()
        {
            DrawMode=DrawMode.OwnerDrawFixed;
            FlatStyle=FlatStyle.Standard;
        }

        [DllImport("uxtheme.dll",CharSet=CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hwnd,string subAppName,string subIdList);

        internal string EmptyText
        {
            get { return emptyText; }
            set {
                if (emptyText==value) return;
                emptyText=value;
                if (Items.Count==0) Invalidate();
            }
        }

        internal void ApplyPalette(AppPalette value)
        {
            palette=value;
            BackColor=value.Field; ForeColor=value.Ink;
            ApplyNativeTheme();
            Invalidate();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            nativeDarkRequested=false;
            ApplyNativeTheme();
        }

        protected override void OnSystemColorsChanged(EventArgs e)
        {
            base.OnSystemColorsChanged(e);
            ApplyNativeTheme();
        }

        void ApplyNativeTheme()
        {
            if (!IsHandleCreated) return;
            bool dark=palette.Dark && !SystemInformation.HighContrast;
            if (dark==nativeDarkRequested) return;
            nativeDarkRequested=dark;
            SetWindowTheme(Handle,dark ? "DarkMode_CFD" : null,null);
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            ItemHeight=Font.Height+7;
            Invalidate();
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            bool edit=(e.State&DrawItemState.ComboBoxEdit)!=0;
            bool selected=!edit && (e.State&DrawItemState.Selected)!=0;
            Color background=selected ? palette.Action : BackColor;
            Color ink=!Enabled ? palette.Muted : selected ? Color.White : ForeColor;
            if (SystemInformation.HighContrast) {
                background=selected ? SystemColors.Highlight : SystemColors.Window;
                ink=!Enabled ? SystemColors.GrayText : selected ? SystemColors.HighlightText : SystemColors.WindowText;
            }
            using (var brush=new SolidBrush(background)) e.Graphics.FillRectangle(brush,e.Bounds);
            int index=edit && e.Index<0 ? SelectedIndex : e.Index;
            string caption=index>=0 && index<Items.Count ? GetItemText(Items[index]) : Items.Count==0 ? emptyText : "";
            if (caption.Length>0) {
                var textBounds=Rectangle.Inflate(e.Bounds,-Math.Max(4,Font.Height/4),0);
                if (textBounds.Width>0)
                    TextRenderer.DrawText(e.Graphics,caption,Font,textBounds,ink,
                        TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
            }
            e.DrawFocusRectangle();
        }
    }

    internal sealed class AppPalette
    {
        public readonly Color Canvas, Surface, Ink, Muted, Header, Accent, AccentInk, Selection, Border, Hover, Alert;
        public readonly Color Atmosphere, Caustic, Action, Field;
        public readonly bool Dark;

        public AppPalette(int theme,bool dark) : this(theme,dark,CustomThemeColors.Default) {}

        internal AppPalette(int theme,bool dark,CustomThemeColors custom)
        {
            Dark=dark;
            if (theme==ThemeOptions.CustomIndex) {
                Canvas=dark ? LimitLuminance(custom.DarkBackground,.055,false) :
                    LimitLuminance(custom.LightBackground,.70,true);
                Accent=LimitLuminance(custom.Accent,dark ? .56 : .105,dark);
                Action=LimitLuminance(custom.Accent,.115,false);
                Header=Blend(Action,Color.Black,.24);
                Atmosphere=LimitLuminance(custom.Accent,dark ? .21 : .32,!dark);
                Caustic=Blend(Atmosphere,Canvas,.30);
            } else {
                var preset=ThemeOptions.Presets[ThemeOptions.IsPreset(theme) ? theme : 0];
                Canvas=Rgb(dark ? preset.DarkCanvas : preset.LightCanvas);
                Accent=Rgb(dark ? preset.DarkAccent : preset.LightAccent);
                Header=Rgb(preset.Header);
                Atmosphere=Rgb(preset.Atmosphere);
                Caustic=Rgb(preset.Caustic);
                Action=Rgb(preset.Action);
            }
            Surface=dark ? Blend(Canvas,Color.White,.065) : Color.White;
            Field=Blend(Surface,dark ? Atmosphere : Canvas,dark ? .11 : .54);
            Ink=Rgb(dark ? 0xF1F2F8 : 0x20283A);
            Muted=Rgb(dark ? 0xBEC4D3 : 0x4D5D70);
            AccentInk=dark ? Rgb(0x101620) : Color.White;
            Selection=Blend(Surface,Accent,dark ? .18 : .12);
            Border=Blend(Surface,Ink,dark ? .28 : .19);
            Hover=Blend(Surface,Accent,dark ? .24 : .16);
            Alert=Rgb(dark ? 0xFFB89D : 0x9A3426);
        }

        // Pick the closest tone of the chosen color that keeps the text readable.
        static Color LimitLuminance(Color color,double limit,bool lighten)
        {
            if (FitsLuminance(color,limit,lighten)) return color;
            Color target=lighten ? Color.White : Color.Black;
            double low=0, high=1;
            for (int i=0;i<9;i++) {
                double middle=(low+high)/2;
                if (FitsLuminance(Blend(color,target,middle),limit,lighten)) high=middle;
                else low=middle;
            }
            return Blend(color,target,high);
        }

        static bool FitsLuminance(Color color,double limit,bool lighten)
        {
            double luminance=.2126*Linear(color.R)+.7152*Linear(color.G)+.0722*Linear(color.B);
            return lighten ? luminance>=limit : luminance<=limit;
        }

        static double Linear(byte channel)
        {
            double value=channel/255.0;
            return value<=.04045 ? value/12.92 : Math.Pow((value+.055)/1.055,2.4);
        }

        static Color Rgb(int value) { return Color.FromArgb(255,(value>>16)&255,(value>>8)&255,value&255); }
        internal static Color Blend(Color from,Color to,double weight)
        {
            return Color.FromArgb(
                (int)(from.R+(to.R-from.R)*weight),
                (int)(from.G+(to.G-from.G)*weight),
                (int)(from.B+(to.B-from.B)*weight));
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);

        public void ApplyCaption(Form form,bool matchGlass=false)
        {
            if (!form.IsHandleCreated) return;
            int dark=Dark ? 1 : 0;
            if (DwmSetWindowAttribute(form.Handle,20,ref dark,4)!=0)
                DwmSetWindowAttribute(form.Handle,19,ref dark,4);
            if (matchGlass) {
                const int CaptionColor=35, TextColor=36;
                Color tint=Dark ? Canvas : Blend(Canvas,Color.White,.76);
                int caption=SystemInformation.HighContrast ? -1 : ColorTranslator.ToWin32(tint);
                int text=SystemInformation.HighContrast ? -1 : ColorTranslator.ToWin32(Ink);
                DwmSetWindowAttribute(form.Handle,CaptionColor,ref caption,4);
                DwmSetWindowAttribute(form.Handle,TextColor,ref text,4);
            }
        }

        public void StyleButton(Button button,bool primary=false,bool header=false)
        {
            if (button is GlassButton glass) { glass.ApplyPalette(this,primary); return; }
            button.BackColor=header ? Blend(Header,Color.White,.12) : primary ? Accent : Surface;
            button.ForeColor=header ? Color.White : primary ? AccentInk : Ink;
            button.FlatAppearance.BorderColor=header ? Blend(Header,Color.White,.28) : primary ? Accent : Border;
            button.FlatAppearance.MouseOverBackColor=header ? Blend(Header,Color.White,.22) :
                primary ? Blend(Accent,AccentInk,.13) : Hover;
            button.FlatAppearance.MouseDownBackColor=header ? Blend(Header,Color.White,.29) :
                primary ? Blend(Accent,AccentInk,.22) : Selection;
        }

        // Surface roles are defined by the container, so switching palette never
        // relies on the colors assigned by a previous theme.
        public void Apply(Control control,Control primary=null)
        {
            if (control is Form || control.Name=="Canvas") control.BackColor=Canvas;
            if (control is Form form) ApplyCaption(form,form is IGlassHost);
            else if (control is ConsoleSurface pane) pane.ApplyPalette(this);
            else if (control.Name=="Card") control.BackColor=Surface;
            else if (control.Name=="Header") control.BackColor=Header;
            else if (control is TextBoxBase || control is ComboBox) control.BackColor=Field;

            bool header=control.Parent?.Name=="Header";
            if (control is TransportPicker) ((TransportPicker)control).ApplyPalette(this);
            else if (control is PalettePicker picker) picker.ApplyPalette(this);
            else if (control is GlassToggle toggle) toggle.ApplyPalette(this);
            else if (control is Button && !(control.Parent?.Parent is TransportPicker))
                StyleButton((Button)control,control==primary,header);
            else if (control is LinkLabel) {
                var link=(LinkLabel)control;
                link.LinkColor=Accent; link.ActiveLinkColor=Ink; link.VisitedLinkColor=Accent;
            } else if (control is Label || control is CheckBox || control is ComboBox || control is TextBoxBase)
                control.ForeColor=header ? Color.White : (control.Name=="Muted" ? Muted : Ink);

            foreach (Control child in control.Controls) Apply(child,primary);
        }
    }
}
