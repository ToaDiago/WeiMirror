using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MirrorDesk
{
    internal sealed class AppPalette
    {
        internal static readonly string[] Names = { "经典", "太空", "冰川", "日落" };
        static readonly int[] LightAccent = { 0x166B68, 0x5D429C, 0x185F9F, 0x9B4A29 };
        static readonly int[] DarkAccent = { 0x69DCCE, 0xBDA4FF, 0x82C2FF, 0xFFAE8B };
        static readonly int[] Headers = { 0x19343A, 0x251F45, 0x142D49, 0x492C34 };
        static readonly int[] DarkCanvases = { 0x101B1E, 0x121225, 0x111D2B, 0x201619 };
        static readonly int[] LightCanvases = { 0xF0F5F4, 0xF4F1FB, 0xEFF5FC, 0xFBF3EF };
        public readonly Color Canvas, Surface, Ink, Muted, Header, Accent, AccentInk, Selection, Border, Hover, Alert;
        public readonly bool Dark;

        public AppPalette(int theme,bool dark)
        {
            if (theme<0 || theme>=Names.Length) theme=0;
            Dark=dark;
            Canvas=Rgb(dark ? DarkCanvases[theme] : LightCanvases[theme]);
            Surface=dark ? Blend(Canvas,Color.White,.065) : Color.White;
            Ink=Rgb(dark ? 0xF1F2F8 : 0x20283A);
            Muted=Rgb(dark ? 0xBEC4D3 : 0x4D5D70);
            Header=Rgb(Headers[theme]);
            Accent=Rgb(dark ? DarkAccent[theme] : LightAccent[theme]);
            AccentInk=dark ? Rgb(0x101620) : Color.White;
            Selection=Blend(Surface,Accent,dark ? .18 : .12);
            Border=Blend(Surface,Ink,dark ? .28 : .19);
            Hover=Blend(Surface,Accent,dark ? .24 : .16);
            Alert=Rgb(dark ? 0xFFB89D : 0x9A3426);
        }

        static Color Rgb(int value) { return Color.FromArgb(255,(value>>16)&255,(value>>8)&255,value&255); }
        static Color Blend(Color from,Color to,double weight)
        {
            return Color.FromArgb(
                (int)(from.R+(to.R-from.R)*weight),
                (int)(from.G+(to.G-from.G)*weight),
                (int)(from.B+(to.B-from.B)*weight));
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);

        public void ApplyCaption(Form form)
        {
            if (!form.IsHandleCreated) return;
            int dark=Dark ? 1 : 0;
            if (DwmSetWindowAttribute(form.Handle,20,ref dark,4)!=0)
                DwmSetWindowAttribute(form.Handle,19,ref dark,4);
        }

        public void StyleButton(Button button,bool primary=false,bool header=false)
        {
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
            if (control is Form) ApplyCaption((Form)control);
            else if (control.Name=="Card") control.BackColor=Surface;
            else if (control.Name=="Header") control.BackColor=Header;
            else if (control is TextBoxBase || control is ComboBox) control.BackColor=Surface;

            bool header=control.Parent?.Name=="Header";
            if (control is TransportPicker) ((TransportPicker)control).ApplyPalette(this);
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
