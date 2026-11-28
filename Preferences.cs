using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Threading;

namespace MirrorDesk
{
    internal sealed class UserPreferences
    {
        public const int DefaultTextPercent = 100, DefaultInterfaceSize = 1;
        public bool Wireless { get; set; }
        public bool PlayAudio { get; set; } = true;
        public bool AlwaysOnTop { get; set; }
        public int QualityIndex { get; set; } = WirelessProfile.DefaultIndex;
        public int ThemeIndex { get; set; }
        public CustomThemeColors CustomColors { get; set; } = CustomThemeColors.Default;
        public bool DarkMode { get; set; }
        public bool AlwaysShowToolbar { get; set; }
        public string ReceiverName { get; set; } = "WeiMirror";
        public string PreviewLayout { get; set; } = "";
        public string InterfaceFont { get; set; } = "";
        public int TextPercent { get; set; } = DefaultTextPercent;
        public int InterfaceSize { get; set; } = DefaultInterfaceSize;

        public static bool ValidTextPercent(int value) { return value>=90 && value<=120 && value%10==0; }
        public static bool ValidInterfaceSize(int value) { return value>=0 && value<=2; }
    }

    internal sealed class PreferencesStore
    {
        readonly string filePath;
        static readonly Encoding Utf8 = new UTF8Encoding(false, true);
        static readonly object WriteLock = new object();

        const uint MoveFileReplaceExisting = 1;
        const uint MoveFileWriteThrough = 8;
        [DllImport("kernel32.dll", ExactSpelling=true, CharSet=CharSet.Unicode, SetLastError=true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool MoveFileExW(string existingName, string newName, uint flags);

        public PreferencesStore(string filePath)
        {
            this.filePath = Path.GetFullPath(filePath);
        }

        public static PreferencesStore ForCurrentUser()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return new PreferencesStore(Path.Combine(local, "WeiMirror", "preferences.ini"));
        }

        public UserPreferences Load()
        {
            var prefs = new UserPreferences();
            try
            {
                using (var file = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
                {
                    if (file.Length > 32768) return prefs;
                    using (var reader = new StreamReader(file, Utf8, true))
                    {
                        string line;
                        bool legacy=true;
                        while ((line = reader.ReadLine()) != null)
                        {
                            int separator = line.IndexOf('=');
                            if (separator <= 0) continue;
                            string value = line.Substring(separator + 1).Trim();
                            bool flag;
                            int index;
                            switch (line.Substring(0, separator).Trim())
                            {
                                case "format":
                                    legacy=value!="2";
                                    break;
                                case "theme":
                                    if (value=="custom") prefs.ThemeIndex=ThemeOptions.CustomIndex;
                                    else if (Int32.TryParse(value,NumberStyles.None,CultureInfo.InvariantCulture,out index) &&
                                        ThemeOptions.IsPreset(index))
                                        prefs.ThemeIndex=index;
                                    break;
                                case "customColors":
                                    if (CustomThemeColors.TryDecode(value,out var colors)) prefs.CustomColors=colors;
                                    break;
                                case "dark":
                                    if (Boolean.TryParse(value,out flag)) prefs.DarkMode=flag;
                                    break;
                                case "toolbar":
                                    if (Boolean.TryParse(value,out flag)) prefs.AlwaysShowToolbar=flag;
                                    break;
                                case "transport":
                                    if (value == "airplay" || value == "usb") prefs.Wireless = value == "airplay";
                                    break;
                                case "quality":
                                    if (Int32.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out index)) {
                                        int quality=WirelessProfile.IndexFromSavedId(index);
                                        if (quality>=0) prefs.QualityIndex=quality;
                                    }
                                    break;
                                case "interfaceFont":
                                    prefs.InterfaceFont=SafeFont(value);
                                    break;
                                case "textPercent":
                                    if (Int32.TryParse(value,NumberStyles.None,CultureInfo.InvariantCulture,out index) && UserPreferences.ValidTextPercent(index))
                                        prefs.TextPercent=index;
                                    break;
                                case "interfaceSize":
                                    if (Int32.TryParse(value,NumberStyles.None,CultureInfo.InvariantCulture,out index) && UserPreferences.ValidInterfaceSize(index))
                                        prefs.InterfaceSize=index;
                                    break;
                                case "audio":
                                    if (Boolean.TryParse(value, out flag)) prefs.PlayAudio = flag;
                                    break;
                                case "topmost":
                                    if (Boolean.TryParse(value, out flag)) prefs.AlwaysOnTop = flag;
                                    break;
                                case "name":
                                    prefs.ReceiverName = SafeName(value);
                                    break;
                                case "previewLayout":
                                    prefs.PreviewLayout = SafeLayout(value);
                                    break;
                            }
                        }
                        if (legacy && prefs.QualityIndex==0) prefs.QualityIndex=WirelessProfile.DefaultIndex;
                    }
                }
                return prefs;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException ||
                                       ex is SecurityException || ex is DecoderFallbackException)
            {
                return new UserPreferences();
            }
        }

        static bool ValidQuality(int index) { return index >= 0 && index < WirelessProfile.Choices.Length; }

        static string SafeFont(string value)
        {
            value=(value ?? "").Trim();
            if (value.Length>128) return "";
            foreach (char c in value) if (Char.IsControl(c)) return "";
            return value;
        }

        static string SafeLayout(string value)
        {
            if (String.IsNullOrEmpty(value) || value.Length > 512) return "";
            foreach (char c in value) if (Char.IsControl(c)) return "";
            return value;
        }

        static string SafeName(string name)
        {
            try { return MirrorCore.ValidateReceiverName(name); }
            catch (ArgumentException) { return "WeiMirror"; }
        }

        public bool TrySave(UserPreferences prefs)
        {
            if (prefs == null) return false;
            lock (WriteLock) return SaveSnapshot(prefs);
        }

        bool SaveSnapshot(UserPreferences prefs)
        {
            string temp = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                File.WriteAllLines(temp, new[] {
                    "format=2",
                    "theme=" + (prefs.ThemeIndex==ThemeOptions.CustomIndex ? "custom" :
                        (ThemeOptions.IsPreset(prefs.ThemeIndex) ? prefs.ThemeIndex : 0).ToString(CultureInfo.InvariantCulture)),
                    "customColors=" + prefs.CustomColors.Encode(),
                    "dark=" + prefs.DarkMode,
                    "toolbar=" + prefs.AlwaysShowToolbar,
                    "transport=" + (prefs.Wireless ? "airplay" : "usb"),
                    "quality=" + WirelessProfile.Choices[ValidQuality(prefs.QualityIndex) ? prefs.QualityIndex : WirelessProfile.DefaultIndex].Id.ToString(CultureInfo.InvariantCulture),
                    "interfaceFont=" + SafeFont(prefs.InterfaceFont),
                    "textPercent=" + (UserPreferences.ValidTextPercent(prefs.TextPercent) ? prefs.TextPercent : UserPreferences.DefaultTextPercent).ToString(CultureInfo.InvariantCulture),
                    "interfaceSize=" + (UserPreferences.ValidInterfaceSize(prefs.InterfaceSize) ? prefs.InterfaceSize : UserPreferences.DefaultInterfaceSize).ToString(CultureInfo.InvariantCulture),
                    "audio=" + prefs.PlayAudio,
                    "topmost=" + prefs.AlwaysOnTop,
                    "name=" + SafeName(prefs.ReceiverName),
                    "previewLayout=" + SafeLayout(prefs.PreviewLayout)
                }, Utf8);

                // A same-directory rename on Windows covers both first save and overwrite.
                for (int attempt = 0; ; attempt++)
                {
                    if (MoveFileExW(temp, filePath, MoveFileReplaceExisting | MoveFileWriteThrough)) return true;
                    int error = Marshal.GetLastWin32Error();
                    // Readers, antivirus scanners and another instance can briefly lock a rename.
                    if (attempt == 4 || (error != 5 && error != 32 && error != 33)) return false;
                    Thread.Sleep(8 << attempt);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException ||
                                       ex is SecurityException || ex is EncoderFallbackException)
            {
                return false;
            }
            finally
            {
                try { File.Delete(temp); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                catch (SecurityException) { }
            }
        }
    }
}
