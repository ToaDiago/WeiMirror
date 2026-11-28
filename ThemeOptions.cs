using System;
using System.Drawing;
using System.Globalization;
using System.Linq;

namespace MirrorDesk
{
    internal static class ThemeOptions
    {
        internal readonly struct Preset
        {
            internal readonly string Name;
            internal readonly int LightCanvas, DarkCanvas, LightAccent, DarkAccent, Header, Atmosphere, Caustic, Action;

            internal Preset(string name,int lightCanvas,int darkCanvas,int lightAccent,int darkAccent,
                int header,int atmosphere,int caustic,int action)
            {
                Name=name; LightCanvas=lightCanvas; DarkCanvas=darkCanvas;
                LightAccent=lightAccent; DarkAccent=darkAccent; Header=header;
                Atmosphere=atmosphere; Caustic=caustic; Action=action;
            }
        }

        // Keep these positions stable for preferences saved by earlier versions.
        internal static readonly Preset[] Presets={
            new Preset("经典",0xE7EEF8,0x132036,0x166B68,0x69DCCE,
                header:0x19343A,atmosphere:0x879DDA,caustic:0x64BBCD,action:0x254A7D),
            new Preset("太空",0xECE7F8,0x19152F,0x5D429C,0xBDA4FF,
                header:0x251F45,atmosphere:0xA086DB,caustic:0x7BAEDB,action:0x54388E),
            new Preset("冰川",0xE5F1F9,0x122438,0x185F9F,0x82C2FF,
                header:0x142D49,atmosphere:0x76B8E2,caustic:0x79CBBF,action:0x155C92),
            new Preset("日落",0xF9E9E4,0x2B1C29,0x9B4A29,0xFFAE8B,
                header:0x492C34,atmosphere:0xE99C85,caustic:0xC094DC,action:0x973F3A),
            new Preset("薄荷",0xE5F4ED,0x142821,0x17624D,0x88D7B8,
                header:0x19382C,atmosphere:0x7EBAA5,caustic:0xB1D9B4,action:0x205E4F),
            new Preset("琥珀",0xFBF1DF,0x2C241B,0x74470B,0xF3C880,
                header:0x413021,atmosphere:0xDDB477,caustic:0xF2CA86,action:0x805018),
            new Preset("玫瑰",0xF7EAF1,0x2C1C2C,0x8A355A,0xF0ACC8,
                header:0x3C2638,atmosphere:0xD79CB5,caustic:0xC7A6D6,action:0x853D60),
            new Preset("石墨",0xECEEF1,0x222528,0x455369,0xB9C8DF,
                header:0x272C35,atmosphere:0xA1A8BA,caustic:0xABB9C9,action:0x3C4B62)
        };

        internal static readonly string[] Names=Presets.Select(preset=>preset.Name).Concat(new[]{"自定义"}).ToArray();
        internal static int CustomIndex { get { return Presets.Length; } }
        internal static bool IsPreset(int index) { return index>=0 && index<Presets.Length; }
    }

    internal struct CustomThemeColors
    {
        internal Color Accent, LightBackground, DarkBackground;

        internal CustomThemeColors(Color accent,Color lightBackground,Color darkBackground)
        {
            Accent=accent; LightBackground=lightBackground; DarkBackground=darkBackground;
        }

        internal static CustomThemeColors Default {
            get {
                return new CustomThemeColors(Color.FromArgb(57,106,176),
                    Color.FromArgb(231,238,248),Color.FromArgb(19,32,54));
            }
        }

        internal static string Hex(Color color)
        {
            return "#"+(color.ToArgb()&0xFFFFFF).ToString("X6",CultureInfo.InvariantCulture);
        }

        internal string Encode()
        {
            return String.Join(",",Hex(Accent),Hex(LightBackground),Hex(DarkBackground));
        }

        internal static bool TryDecode(string value,out CustomThemeColors colors)
        {
            colors=Default;
            var values=value.Split(',');
            if (values.Length!=3 || !TryReadColor(values[0],out var accent) ||
                !TryReadColor(values[1],out var light) || !TryReadColor(values[2],out var dark)) return false;
            colors=new CustomThemeColors(accent,light,dark);
            return true;
        }

        static bool TryReadColor(string value,out Color color)
        {
            color=Color.Empty;
            if (value.Length!=7 || value[0]!='#' ||
                !Int32.TryParse(value.Substring(1),NumberStyles.AllowHexSpecifier,CultureInfo.InvariantCulture,out int rgb))
                return false;
            color=Color.FromArgb(unchecked((int)0xFF000000)|rgb);
            return true;
        }
    }
}
