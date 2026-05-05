// ============================================
// LoginPilot v3.2 - Proxy, VPN & Arbeitsplatz Manager
//
// Portabel: Config liegt neben der EXE
// Erststart: Oeffnet automatisch Einstellungen wenn keine Config
// Feature-Toggles: RDP und VPN einzeln aktivierbar
//
// Kompilieren:
// C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /win32icon:loginpilot_icon.ico /out:LoginPilot.exe LoginPilot.cs
//
// Konfiguration oeffnen:
//   LoginPilot.exe /k
//   oder Taste '#' im Hauptfenster druecken
// ============================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Xml.Serialization;
using Microsoft.Win32;

namespace LoginPilot
{
    // ========== DATENMODELL ==========
    public class ProxyProfile
    {
        public string Name { get; set; }
        public string Server { get; set; }
        public int Port { get; set; }
        public string Exceptions { get; set; }
        public string SubnetPrefix { get; set; }
        public string RdpFile { get; set; }
        public bool IsHomeOffice { get; set; }
        public ProxyProfile() { Name = ""; Server = ""; Port = 3128; Exceptions = ""; SubnetPrefix = ""; RdpFile = ""; IsHomeOffice = false; }
        public ProxyProfile(string n, string s, int p, string ex, string sub, string rdp, bool ho = false)
        { Name = n; Server = s; Port = p; Exceptions = ex; SubnetPrefix = sub; RdpFile = rdp; IsHomeOffice = ho; }
        public string ProxyAddress { get { return Server + ":" + Port; } }
    }

    public class AppConfig
    {
        public List<ProxyProfile> Profiles { get; set; }
        public bool AutoDetectOnStart { get; set; }
        public bool ConfigLocked { get; set; }
        public string LogoFile { get; set; }
        public string LastMode { get; set; }
        public string LastProfileName { get; set; }
        public bool AutostartAllUsers { get; set; }
        public bool StartMinimized { get; set; }
        public string RdpFileProxyOff { get; set; }
        public string VpnCommand { get; set; }
        public string VpnArgs { get; set; }
        public bool DarkTheme { get; set; }
        public bool EnableRdp { get; set; }
        public bool EnableVpn { get; set; }

        public AppConfig()
        {
            Profiles = new List<ProxyProfile>();
            AutoDetectOnStart = true;
            ConfigLocked = false;
            LogoFile = "";
            LastMode = "auto";
            LastProfileName = "";
            AutostartAllUsers = false;
            StartMinimized = false;
            RdpFileProxyOff = "";
            VpnCommand = "";
            VpnArgs = "";
            DarkTheme = true;
            EnableRdp = false;
            EnableVpn = false;
        }

        // PORTABEL: Config liegt neben der EXE
        public static string ConfigPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "loginpilot_config.xml"); }
        }

        public void Save()
        {
            try
            {
                var xs = new XmlSerializer(typeof(AppConfig));
                using (var sw = new StreamWriter(ConfigPath)) { xs.Serialize(sw, this); }
            }
            catch { }
        }

        public static AppConfig Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var xs = new XmlSerializer(typeof(AppConfig));
                    using (var sr = new StreamReader(ConfigPath)) { return (AppConfig)xs.Deserialize(sr); }
                }
            }
            catch { }
            // Keine Config gefunden -> leere Defaults (Erststart)
            var cfg = new AppConfig();
            return cfg;
        }

        public bool IsFirstStart { get { return Profiles.Count == 0; } }
    }

    // ========== PROXY REGISTRY ==========
    public static class ProxyManager
    {
        [DllImport("wininet.dll")]
        private static extern bool InternetSetOption(IntPtr h, int o, IntPtr b, int l);

        public static void Enable(ProxyProfile p)
        {
            using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings", true))
            {
                k.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
                k.SetValue("ProxyServer", p.ProxyAddress, RegistryValueKind.String);
                k.SetValue("ProxyOverride", p.Exceptions, RegistryValueKind.String);
            }
            Refresh();
        }

        public static void Disable()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings", true))
            { k.SetValue("ProxyEnable", 0, RegistryValueKind.DWord); }
            Refresh();
        }

        public static bool IsEnabled()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings", false))
            { var v = k.GetValue("ProxyEnable"); return v != null && (int)v == 1; }
        }

        public static string GetServer()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings", false))
            { var v = k.GetValue("ProxyServer"); return v != null ? v.ToString() : ""; }
        }

        public static string GetExceptions()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings", false))
            { var v = k.GetValue("ProxyOverride"); return v != null ? v.ToString() : ""; }
        }

        static void Refresh()
        {
            InternetSetOption(IntPtr.Zero, 39, IntPtr.Zero, 0);
            InternetSetOption(IntPtr.Zero, 37, IntPtr.Zero, 0);
        }
    }

    // ========== NETZWERK-ERKENNUNG ==========
    public static class NetDetect
    {
        public static List<string> GetIPs()
        {
            var list = new List<string>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var ip in ni.GetIPProperties().UnicastAddresses)
                        if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                            list.Add(ip.Address.ToString());
                }
            }
            catch { }
            return list;
        }

        public static ProxyProfile Detect(List<ProxyProfile> profiles)
        {
            var ips = GetIPs();
            foreach (var ip in ips)
                foreach (var p in profiles)
                    if (!string.IsNullOrEmpty(p.SubnetPrefix) && ip.StartsWith(p.SubnetPrefix))
                        return p;
            return null;
        }

        public static bool IsVpnConnected()
        {
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    string desc = ni.Description.ToLower();
                    string name = ni.Name.ToLower();
                    if (desc.Contains("tap-") || desc.Contains("tap ") || desc.Contains("tun ")
                        || desc.Contains("openvpn") || desc.Contains("wireguard")
                        || desc.Contains("vpn") || name.Contains("vpn")
                        || desc.Contains("fortinet") || desc.Contains("cisco"))
                    {
                        foreach (var ip in ni.GetIPProperties().UnicastAddresses)
                            if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                                return true;
                    }
                }
            }
            catch { }
            return false;
        }

        // RDP-Host aus .rdp-Datei extrahieren (Zeile "full address:s:hostname")
        public static string ExtractRdpHost(string rdpFilePath)
        {
            try
            {
                if (string.IsNullOrEmpty(rdpFilePath) || !File.Exists(rdpFilePath)) return null;
                foreach (var line in File.ReadAllLines(rdpFilePath))
                {
                    string l = line.Trim().ToLower();
                    if (l.StartsWith("full address:s:"))
                    {
                        string addr = line.Substring(line.IndexOf(":s:") + 3).Trim();
                        // Port entfernen falls vorhanden (host:port)
                        if (addr.Contains(":")) addr = addr.Substring(0, addr.IndexOf(':'));
                        return addr;
                    }
                }
            }
            catch { }
            return null;
        }

        // TCP-Verbindungstest zu einem Host/Port mit Timeout
        public static bool IsTcpReachable(string host, int port, int timeoutMs)
        {
            try
            {
                using (var client = new System.Net.Sockets.TcpClient())
                {
                    var result = client.BeginConnect(host, port, null, null);
                    bool connected = result.AsyncWaitHandle.WaitOne(timeoutMs);
                    if (connected && client.Connected)
                    {
                        client.EndConnect(result);
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }
    }

    // ========== RDP LAUNCHER ==========
    public static class RdpLauncher
    {
        public static string FindRdp(string filename)
        {
            if (string.IsNullOrEmpty(filename)) return null;

            if (Path.IsPathRooted(filename) && File.Exists(filename))
                return filename;

            // Neben der EXE suchen
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string path = Path.Combine(exeDir, filename);
            if (File.Exists(path)) return path;

            // Auf User-Desktop suchen
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            path = Path.Combine(desktop, filename);
            if (File.Exists(path)) return path;

            // Public Desktop
            string pubDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
            path = Path.Combine(pubDesktop, filename);
            if (File.Exists(path)) return path;

            // C:\ZEUS\ als Fallback
            string zeusPath = Path.Combine(@"C:\ZEUS", filename);
            if (File.Exists(zeusPath)) return zeusPath;

            return null;
        }

        public static bool Launch(string filename)
        {
            string path = FindRdp(filename);
            if (path == null) return false;
            try
            {
                System.Diagnostics.Process.Start(path);
                return true;
            }
            catch { return false; }
        }
    }

    // ========== AUTO-UPDATE ==========
    public static class AutoUpdater
    {
        const string CurrentVersion    = "3.2";
        const string VersionUrl        = "https://raw.githubusercontent.com/eyeCatcher77/LoginPilot/main/version.txt";
        const string ExeUrl            = "https://raw.githubusercontent.com/eyeCatcher77/LoginPilot/main/LoginPilot.exe";
        const int    TimeoutMs         = 5000; // Kein langer Haenger beim Start

        // Wird im Hintergrund-Thread aufgerufen – blockiert den Start nicht
        public static void CheckAndUpdateAsync()
        {
            var t = new System.Threading.Thread(() =>
            {
                try { CheckAndUpdate(); }
                catch { /* Fehler still ignorieren – Update ist optional */ }
            });
            t.IsBackground = true;
            t.Start();
        }

        static void CheckAndUpdate()
        {
            // TLS 1.2 erzwingen (GitHub braucht das, .NET 4.0 default ist aelter)
            ServicePointManager.SecurityProtocol =
                (SecurityProtocolType)3072; // SecurityProtocolType.Tls12

            string remoteVersion;
            using (var wc = new WebClient())
            {
                wc.Headers.Add("User-Agent", "LoginPilot/" + CurrentVersion);
                // Timeout ueber eigenen WebRequest setzen
                var req = (HttpWebRequest)WebRequest.Create(VersionUrl);
                req.Timeout = TimeoutMs;
                req.UserAgent = "LoginPilot/" + CurrentVersion;
                using (var resp = req.GetResponse())
                using (var sr = new StreamReader(resp.GetResponseStream()))
                    remoteVersion = sr.ReadToEnd().Trim();
            }

            if (string.IsNullOrEmpty(remoteVersion)) return;

            // Versionsvergleich als float (3.1 < 3.2 etc.)
            float remote, current;
            if (!float.TryParse(remoteVersion, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out remote)) return;
            if (!float.TryParse(CurrentVersion, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out current)) return;

            if (remote <= current) return; // Kein Update noetig

            // Neue EXE herunterladen
            string exePath  = Application.ExecutablePath;
            string bakPath  = exePath + ".bak";
            string tmpPath  = exePath + ".tmp";

            var req2 = (HttpWebRequest)WebRequest.Create(ExeUrl);
            req2.Timeout = 60000; // 60s fuer den Download
            req2.UserAgent = "LoginPilot/" + CurrentVersion;
            using (var resp = req2.GetResponse())
            using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write))
            {
                var buf = new byte[81920];
                var stream = resp.GetResponseStream();
                int read;
                while ((read = stream.Read(buf, 0, buf.Length)) > 0)
                    fs.Write(buf, 0, read);
            }

            // Sanity-Check: heruntergeladene Datei muss groesser als 100 KB sein
            if (new FileInfo(tmpPath).Length < 100 * 1024)
            {
                try { File.Delete(tmpPath); } catch { }
                return;
            }

            // Altes .bak aufraemen, aktuelle EXE als .bak sichern, neue einsetzen
            try { if (File.Exists(bakPath)) File.Delete(bakPath); } catch { }
            File.Move(exePath, bakPath);
            File.Move(tmpPath, exePath);

            // Neustart – neuer Prozess startet, dieser beendet sich
            Process.Start(exePath);
            Application.Exit();
        }
    }

    // ========== AUTOSTART ==========
    public static class AutostartManager
    {
        const string TaskName = "LoginPilot_Autostart";

        // Startup-Ordner-Pfade (nur noch fuer Altbestand-Bereinigung)
        static string StartupFolder { get { return Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup); } }
        static string LnkPath { get { return Path.Combine(StartupFolder, "LoginPilot.lnk"); } }
        static string BatPath { get { return Path.Combine(StartupFolder, "LoginPilot.bat"); } }

        public static bool IsInstalled()
        {
            // Scheduled Task pruefen (neue Methode)
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("schtasks.exe",
                    "/Query /TN \"" + TaskName + "\" /FO LIST")
                { CreateNoWindow = true, UseShellExecute = false,
                  RedirectStandardOutput = true, RedirectStandardError = true };
                var p = System.Diagnostics.Process.Start(psi);
                p.WaitForExit(3000);
                if (p.ExitCode == 0) return true;
            }
            catch { }
            // Fallback: alter Startup-Ordner-Eintrag
            return File.Exists(LnkPath) || File.Exists(BatPath);
        }

        public static bool InstallSimple()
        {
            try
            {
                // Alte Startup-Ordner-Eintraege aufraemen falls vorhanden
                try { if (File.Exists(BatPath)) File.Delete(BatPath); } catch { }
                try { if (File.Exists(LnkPath)) File.Delete(LnkPath); } catch { }

                // Scheduled Task anlegen:
                // - AtLogon: startet fuer jeden User beim Login
                // - HIGHEST: hoehere Startprioritat als normale Startup-Programme
                // - DELAY PT10S: 10 Sekunden nach Login (gibt Windows-Shell Zeit zum Laden,
                //   aber LoginPilot startet trotzdem deutlich vor manuellen Startup-Eintraegen)
                string exePath = Application.ExecutablePath;
                string xml = "<?xml version=\"1.0\" encoding=\"UTF-16\"?>"
                    + "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">"
                    + "<Triggers><LogonTrigger><Enabled>true</Enabled>"
                    + "<Delay>PT10S</Delay>"
                    + "</LogonTrigger></Triggers>"
                    + "<Principals><Principal id=\"Author\">"
                    + "<GroupId>S-1-5-32-545</GroupId>"  // Gruppe "Benutzer" - alle lokalen User
                    + "<RunLevel>LeastPrivilege</RunLevel>"
                    + "</Principal></Principals>"
                    + "<Settings>"
                    + "<MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>"
                    + "<DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>"
                    + "<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>"
                    + "<ExecutionTimeLimit>PT0S</ExecutionTimeLimit>"
                    + "<Priority>4</Priority>"  // Windows-Prioritaet 4 = AboveNormal
                    + "</Settings>"
                    + "<Actions><Exec>"
                    + "<Command>" + exePath + "</Command>"
                    + "</Exec></Actions>"
                    + "</Task>";

                // XML in Temp-Datei schreiben, dann per schtasks importieren
                string tmpXml = Path.Combine(Path.GetTempPath(), "loginpilot_task.xml");
                File.WriteAllText(tmpXml, xml, System.Text.Encoding.Unicode);

                var psi = new System.Diagnostics.ProcessStartInfo("schtasks.exe",
                    "/Create /TN \"" + TaskName + "\" /XML \"" + tmpXml + "\" /F")
                { CreateNoWindow = true, UseShellExecute = false,
                  RedirectStandardOutput = true, RedirectStandardError = true };
                var proc = System.Diagnostics.Process.Start(psi);
                proc.WaitForExit(10000);

                try { File.Delete(tmpXml); } catch { }

                return proc.ExitCode == 0;
            }
            catch { return false; }
        }

        public static bool Uninstall()
        {
            bool ok = true;
            // Scheduled Task entfernen
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("schtasks.exe",
                    "/Delete /TN \"" + TaskName + "\" /F")
                { CreateNoWindow = true, UseShellExecute = false,
                  RedirectStandardOutput = true, RedirectStandardError = true };
                var proc = System.Diagnostics.Process.Start(psi);
                proc.WaitForExit(5000);
                if (proc.ExitCode != 0) ok = false;
            }
            catch { ok = false; }
            // Alte Startup-Ordner-Eintraege ebenfalls entfernen (Altbestand)
            try { if (File.Exists(LnkPath)) File.Delete(LnkPath); } catch { }
            try { if (File.Exists(BatPath)) File.Delete(BatPath); } catch { }
            return ok;
        }
    }

    // ========== FARBEN (Theme) ==========
    public static class Clr
    {
        public static bool IsDark = true;

        public static Color Bg, Card, CardHi, Brd, T1, T2, Inp;
        public static Color Grn = Color.FromArgb(46, 204, 113);
        public static Color GrnD = Color.FromArgb(30, 140, 78);
        public static Color GrnHi = Color.FromArgb(60, 220, 130);
        public static Color Red = Color.FromArgb(231, 76, 60);
        public static Color RedD = Color.FromArgb(160, 50, 38);
        public static Color RedHi = Color.FromArgb(245, 100, 85);
        public static Color Blu = Color.FromArgb(52, 152, 219);
        public static Color BluD = Color.FromArgb(35, 120, 180);
        public static Color BluHi = Color.FromArgb(75, 175, 235);
        public static Color Org = Color.FromArgb(243, 156, 18);
        public static Color OrgD = Color.FromArgb(180, 115, 10);
        public static Color OrgHi = Color.FromArgb(255, 175, 40);

        static Clr() { SetDark(); }

        public static void SetDark()
        {
            IsDark = true;
            Bg = Color.FromArgb(25, 27, 33);
            Card = Color.FromArgb(38, 40, 48);
            CardHi = Color.FromArgb(50, 53, 65);
            Brd = Color.FromArgb(58, 60, 70);
            T1 = Color.FromArgb(238, 238, 243);
            T2 = Color.FromArgb(155, 158, 168);
            Inp = Color.FromArgb(30, 32, 39);

            Grn = Color.FromArgb(46, 204, 113);
            GrnD = Color.FromArgb(30, 140, 78);
            GrnHi = Color.FromArgb(60, 220, 130);
            Red = Color.FromArgb(231, 76, 60);
            RedD = Color.FromArgb(160, 50, 38);
            RedHi = Color.FromArgb(245, 100, 85);
            Blu = Color.FromArgb(52, 152, 219);
            BluD = Color.FromArgb(35, 120, 180);
            BluHi = Color.FromArgb(75, 175, 235);
            Org = Color.FromArgb(243, 156, 18);
            OrgD = Color.FromArgb(180, 115, 10);
            OrgHi = Color.FromArgb(255, 175, 40);
        }

        public static void SetLight()
        {
            IsDark = false;
            Bg = Color.FromArgb(243, 244, 246);
            Card = Color.FromArgb(255, 255, 255);
            CardHi = Color.FromArgb(238, 240, 244);
            Brd = Color.FromArgb(210, 214, 220);
            T1 = Color.FromArgb(40, 44, 52);
            T2 = Color.FromArgb(108, 114, 128);
            Inp = Color.FromArgb(249, 250, 251);

            // Gedaempfte Farben fuer Light-Theme
            Grn = Color.FromArgb(34, 139, 94);
            GrnD = Color.FromArgb(30, 120, 82);
            GrnHi = Color.FromArgb(40, 155, 105);
            Red = Color.FromArgb(180, 62, 52);
            RedD = Color.FromArgb(158, 55, 45);
            RedHi = Color.FromArgb(195, 72, 62);
            Blu = Color.FromArgb(45, 112, 180);
            BluD = Color.FromArgb(38, 98, 160);
            BluHi = Color.FromArgb(55, 128, 198);
            Org = Color.FromArgb(182, 120, 20);
            OrgD = Color.FromArgb(160, 105, 15);
            OrgHi = Color.FromArgb(198, 135, 30);
        }
    }

    // ========== STYLED BUTTON ==========
    public class SButton : Control
    {
        public Color BgNormal { get; set; }
        public Color BgHover { get; set; }
        public bool IsActive { get; set; }
        bool _hover;
        ToolTip _tt;

        public SButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            ForeColor = Clr.T1; BgNormal = Clr.Blu; BgHover = Clr.BluHi;
            IsActive = false; Cursor = Cursors.Hand; Height = 36;
            _tt = new ToolTip();
        }

        public void SetTooltip(string text) { _tt.SetToolTip(this, text); }
        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            var path = RR(r, 8);
            using (var br = new SolidBrush(_hover ? BgHover : BgNormal)) g.FillPath(br, path);
            if (IsActive)
            {
                Color ac = Clr.IsDark ? Color.White : Color.FromArgb(60, 65, 75);
                using (var pen = new Pen(ac, 2.5f)) g.DrawPath(pen, RR(new Rectangle(1, 1, Width - 3, Height - 3), 8));
            }
            else if (_hover)
                using (var pen = new Pen(Color.FromArgb(80, 255, 255, 255), 1f)) g.DrawPath(pen, RR(new Rectangle(1, 1, Width - 3, Height - 3), 8));
            TextRenderer.DrawText(g, Text, Font, r, ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        static GraphicsPath RR(Rectangle r, int rad)
        {
            var p = new GraphicsPath(); int d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure(); return p;
        }
    }

    // ========== HAUPTFENSTER ==========
    public class MainForm : Form
    {
        const int W = 440, M = 25, CW = 390, SCW = 368; // SCW = Config-Breite abzgl. Scrollbar

        AppConfig _cfg;
        bool _configAllowed;
        bool _isFirstStart;
        NotifyIcon _tray;
        ContextMenuStrip _trayMenu;
        Icon _appIcon;

        Panel _mainPage, _cfgPage, _statusIndicator;
        PictureBox _logoBox;
        ComboBox _cmbProfile;
        SButton _btnAn, _btnAus, _btnAuto, _btnWts, _btnSettings;
        Label _lblStatus, _lblStatusDetail, _lblProxy, _lblExceptions, _lblNetwork, _lblStandort, _lblWtsInfo;

        ListBox _lstProfiles;
        SButton _btnAdd, _btnRemove, _btnSave, _btnBack, _btnBrowseLogo;
        CheckBox _chkAutoDetect, _chkLockConfig, _chkAutostart, _chkStartMin, _chkEnableRdp, _chkEnableVpn, _chkHomeOffice;
        TextBox _txtName, _txtServer, _txtPort, _txtExceptions, _txtSubnet, _txtRdp, _txtLogo, _txtRdpOff, _txtVpnCmd, _txtVpnArgs;
        Panel _pnlRdpSettings, _pnlVpnSettings;

        public MainForm(bool configMode)
        {
            _cfg = AppConfig.Load();
            _isFirstStart = _cfg.IsFirstStart;
            _configAllowed = configMode || !_cfg.ConfigLocked;
            if (_cfg.DarkTheme) Clr.SetDark(); else Clr.SetLight();
            try { string p = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "loginpilot_icon.ico"); if (File.Exists(p)) _appIcon = new Icon(p); } catch { }

            InitForm(); InitTray(); BuildMainPage(); BuildConfigPage(); LoadLogo(); UpdateMainLayout();
            InitNetworkWatcher();

            if (_isFirstStart)
            {
                // Erststart -> direkt Einstellungen zeigen
                ShowConfig();
            }
            else
            {
                ApplyLastMode();
                if (_cfg.StartMinimized) { WindowState = FormWindowState.Minimized; ShowInTaskbar = false; Visible = false; }
            }
        }

        Label AL(string text, Font font, Color color, int x, int y)
        { return new Label { Text = text, Font = font, ForeColor = color, BackColor = Color.Transparent, Location = new Point(x, y), AutoSize = true }; }

        TextBox TB(int x, int y, int w)
        { return new TextBox { Location = new Point(x, y), Size = new Size(w, 26), BorderStyle = BorderStyle.FixedSingle, BackColor = Clr.Inp, ForeColor = Clr.T1, Font = new Font("Segoe UI", 10f) }; }

        CheckBox CB(string text, int x, int y, bool chk)
        { return new CheckBox { Text = "  " + text, Location = new Point(x, y), Size = new Size(CW, 22), ForeColor = Clr.T1, Font = new Font("Segoe UI", 9f), BackColor = Clr.Bg, Checked = chk }; }

        void InitForm()
        {
            Text = "LoginPilot";
            Size = new Size(W, 870); MinimumSize = new Size(W, 500); MaximumSize = new Size(W, 920);
            FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen; BackColor = Clr.Bg;
            KeyPreview = true;
            KeyPress += (s, e) =>
            {
                if (e.KeyChar == '#')
                {
                    _configAllowed = !_configAllowed;
                    _btnSettings.Visible = _configAllowed;
                    e.Handled = true;
                }
            };
            if (_appIcon != null) Icon = _appIcon;
        }

        void ApplyLastMode()
        {
            // Gespeichertes Profil im Dropdown waehlen
            if (!string.IsNullOrEmpty(_cfg.LastProfileName))
                for (int i = 0; i < _cfg.Profiles.Count; i++)
                    if (_cfg.Profiles[i].Name == _cfg.LastProfileName) { _cmbProfile.SelectedIndex = i; break; }

            string mode = (_cfg.LastMode ?? "").ToLower();

            // Pruefen ob gewaehltes Profil ein HomeOffice-Profil ist
            ProxyProfile selectedProfile = null;
            if (_cmbProfile.SelectedIndex >= 0 && _cmbProfile.SelectedIndex < _cfg.Profiles.Count)
                selectedProfile = _cfg.Profiles[_cmbProfile.SelectedIndex];

            if (mode == "on" && selectedProfile != null && !selectedProfile.IsHomeOffice)
            {
                ProxyManager.Enable(selectedProfile); SetActive(_btnAn);
            }
            else if (mode == "off" || (selectedProfile != null && selectedProfile.IsHomeOffice))
            {
                ProxyManager.Disable(); SetActive(_btnAus);
            }
            else
            {
                SetActive(_btnAuto); AutoDetect(); return; // AutoDetect ruft selbst RefreshStatus etc. auf
            }
            RefreshStatus(); UpdateTrayIcon(); UpdateWtsInfo();
        }

        void SaveLast(string mode, string name) { _cfg.LastMode = mode; _cfg.LastProfileName = name; _cfg.Save(); }

        // Dynamische Fensterhöhe je nach Features
        void UpdateMainLayout()
        {
            bool showRdp = _cfg.EnableRdp;
            _btnWts.Visible = showRdp;
            _lblWtsInfo.Visible = showRdp;

            int baseY = 458; // nach Standort-Label
            int y = baseY + 20;

            if (showRdp)
            {
                _btnWts.Top = y; y += 50;
                _lblWtsInfo.Top = y; y += 28;
            }

            _btnSettings.Top = y; y += 42;

            // Theme-Button folgt
            foreach (Control c in _mainPage.Controls)
                if (c is SButton && c != _btnAn && c != _btnAus && c != _btnAuto && c != _btnWts && c != _btnSettings && c.Tag != null && c.Tag.ToString() == "theme")
                    c.Top = y;
            y += 40;

            // Version Label
            foreach (Control c in _mainPage.Controls)
                if (c.Tag != null && c.Tag.ToString() == "version")
                    c.Top = y;

            int formH = y + 60;
            if (formH < 500) formH = 500;
            Size = new Size(W, formH);
            MinimumSize = new Size(W, formH);
            MaximumSize = new Size(W, formH + 50);
            UpdateWtsInfo();
        }

        // ========== TRAY ==========
        void InitTray()
        {
            _trayMenu = new ContextMenuStrip { BackColor = Clr.Card, ForeColor = Clr.T1 };
            _trayMenu.Renderer = new DarkRenderer();
            RebuildTrayMenu();
            _tray = new NotifyIcon { Text = "LoginPilot", Icon = MkTrayIcon(Clr.T2, ""), ContextMenuStrip = _trayMenu, Visible = true };
            _tray.DoubleClick += (s, e) => { Show(); ShowInTaskbar = true; WindowState = FormWindowState.Normal; BringToFront(); };
        }

        void RebuildTrayMenu()
        {
            _trayMenu.Items.Clear();
            foreach (var pr in _cfg.Profiles)
            {
                var p = pr;
                if (p.IsHomeOffice)
                    _trayMenu.Items.Add("\u2302  " + p.Name, null, (s, e) => { ProxyManager.Disable(); SaveLast("off", p.Name); RefreshStatus(); UpdateTrayIcon(); UpdateWtsInfo(); });
                else
                    _trayMenu.Items.Add("\u2713  " + p.Name, null, (s, e) => { ProxyManager.Enable(p); SaveLast("on", p.Name); RefreshStatus(); UpdateTrayIcon(); UpdateWtsInfo(); });
            }
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add("\u2298  Proxy AUS", null, (s, e) => { ProxyManager.Disable(); SaveLast("off", ""); RefreshStatus(); UpdateTrayIcon(); UpdateWtsInfo(); });
            _trayMenu.Items.Add("\u26a1  Automatik", null, (s, e) => { SaveLast("auto", ""); AutoDetect(); });
            if (_cfg.EnableRdp)
            {
                _trayMenu.Items.Add(new ToolStripSeparator());
                _trayMenu.Items.Add("\u25b6  Arbeitsplatz starten", null, (s, e) => LaunchWts());
            }
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add("\u00d6ffnen", null, (s, e) => { Show(); ShowInTaskbar = true; WindowState = FormWindowState.Normal; BringToFront(); });
            _trayMenu.Items.Add("Beenden", null, (s, e) => { _tray.Visible = false; Application.Exit(); });
        }

        // ========== HAUPTSEITE ==========
        void BuildMainPage()
        {
            _mainPage = new Panel { Dock = DockStyle.Fill, BackColor = Clr.Bg };
            Controls.Add(_mainPage);

            _logoBox = new PictureBox { Location = new Point(145, 8), Size = new Size(150, 50), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent };
            _mainPage.Controls.Add(_logoBox);

            var t = AL("LoginPilot", new Font("Segoe UI", 18f, FontStyle.Bold), Clr.T1, 0, 15);
            t.AutoSize = false; t.Size = new Size(W, 35); t.TextAlign = ContentAlignment.MiddleCenter;
            _mainPage.Controls.Add(t);

            var s = AL("Proxy, VPN & Arbeitsplatz Manager", new Font("Segoe UI", 9.5f), Clr.T2, 0, 52);
            s.AutoSize = false; s.Size = new Size(W, 22); s.TextAlign = ContentAlignment.MiddleCenter;
            _mainPage.Controls.Add(s);

            // Status Card
            var card = new Panel { Location = new Point(M, 88), Size = new Size(CW, 150), BackColor = Clr.Card };
            _mainPage.Controls.Add(card);
            _statusIndicator = new Panel { Location = new Point(0, 0), Size = new Size(6, 150) };
            card.Controls.Add(_statusIndicator);
            card.Controls.Add(AL("STATUS", new Font("Segoe UI", 8f, FontStyle.Bold), Clr.T2, 20, 12));
            _lblStatus = AL("Proxy AUS", new Font("Segoe UI", 15f, FontStyle.Bold), Clr.Red, 20, 36);
            card.Controls.Add(_lblStatus);
            _lblStatusDetail = AL("", new Font("Segoe UI", 9.5f), Clr.T2, 20, 70);
            card.Controls.Add(_lblStatusDetail);
            _lblProxy = AL("", new Font("Segoe UI", 9.5f), Clr.T2, 20, 93);
            card.Controls.Add(_lblProxy);
            _lblExceptions = AL("", new Font("Segoe UI", 9.5f), Clr.T2, 20, 116);
            card.Controls.Add(_lblExceptions);

            // Netzwerk Card
            var net = new Panel { Location = new Point(M, 252), Size = new Size(CW, 50), BackColor = Clr.Card };
            _mainPage.Controls.Add(net);
            net.Controls.Add(AL("NETZWERK", new Font("Segoe UI", 8f, FontStyle.Bold), Clr.T2, 15, 6));
            _lblNetwork = AL("", new Font("Segoe UI", 9.5f), Clr.T1, 15, 28);
            net.Controls.Add(_lblNetwork);

            // Standort
            _mainPage.Controls.Add(AL("STANDORT W\u00c4HLEN", new Font("Segoe UI", 8f, FontStyle.Bold), Clr.T2, M, 320));

            _cmbProfile = new ComboBox
            {
                Location = new Point(M, 342), Size = new Size(CW, 36),
                DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Clr.Inp, ForeColor = Clr.T1,
                FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 11f),
                DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 28
            };
            _cmbProfile.DrawItem += (snd, ev) =>
            {
                if (ev.Index < 0) return;
                ev.Graphics.FillRectangle((ev.State & DrawItemState.Selected) != 0 ? new SolidBrush(Clr.CardHi) : new SolidBrush(Clr.Inp), ev.Bounds);
                TextRenderer.DrawText(ev.Graphics, _cmbProfile.Items[ev.Index].ToString(), ev.Font, ev.Bounds, Clr.T1, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            };
            PopulateCombo();
            _mainPage.Controls.Add(_cmbProfile);

            // Proxy Buttons
            int by = 400, bw = 120, bh = 44;
            _btnAn = new SButton { Text = "\u25cf PROXY EIN", Location = new Point(M, by), Size = new Size(bw, bh) };
            _btnAn.SetTooltip("Proxy mit gew\u00e4hltem Standort einschalten");
            _btnAn.Click += BtnAn_Click;
            _mainPage.Controls.Add(_btnAn);

            _btnAus = new SButton { Text = "\u25cb PROXY AUS", Location = new Point(M + bw + 12, by), Size = new Size(bw, bh) };
            _btnAus.SetTooltip("Proxy ausschalten (Homeoffice)");
            _btnAus.Click += BtnAus_Click;
            _mainPage.Controls.Add(_btnAus);

            _btnAuto = new SButton { Text = "\u26a1 AUTO", Location = new Point(M + 2 * (bw + 12), by), Size = new Size(bw + 2, bh) };
            _btnAuto.SetTooltip("Standort automatisch erkennen");
            _btnAuto.Click += (sn, ev) => { SetActive(_btnAuto); SaveLast("auto", ""); AutoDetect(); };
            _mainPage.Controls.Add(_btnAuto);
            ApplyProxyButtonTheme();

            _lblStandort = AL("", new Font("Segoe UI", 9.5f), Clr.T2, M, 458);
            _lblStandort.AutoSize = false; _lblStandort.Size = new Size(CW, 22); _lblStandort.TextAlign = ContentAlignment.MiddleCenter;
            _mainPage.Controls.Add(_lblStandort);

            // WTS Button (Position wird dynamisch durch UpdateMainLayout gesetzt)
            _btnWts = new SButton
            {
                Text = "\u25b6  ARBEITSPLATZ STARTEN",
                Location = new Point(M, 498),
                Size = new Size(CW, 44),
                BgNormal = Clr.OrgD,
                BgHover = Clr.OrgHi,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold)
            };
            _btnWts.SetTooltip("Passende RDP-Verbindung starten");
            _wtsClickHandler = (sn, ev) => LaunchWts();
            _btnWts.Click += _wtsClickHandler;
            _mainPage.Controls.Add(_btnWts);

            _lblWtsInfo = AL("", new Font("Segoe UI", 8.5f), Clr.T2, M, 548);
            _lblWtsInfo.AutoSize = false; _lblWtsInfo.Size = new Size(CW, 20); _lblWtsInfo.TextAlign = ContentAlignment.MiddleCenter;
            _mainPage.Controls.Add(_lblWtsInfo);

            // Einstellungen
            _btnSettings = new SButton { Text = "\u2699  Einstellungen", Location = new Point(M, 578), Size = new Size(CW, 40), BgNormal = Clr.Card, BgHover = Clr.CardHi, Font = new Font("Segoe UI", 9.5f) };
            _btnSettings.Visible = _configAllowed;
            _btnSettings.Click += (sn, ev) => ShowConfig();
            _mainPage.Controls.Add(_btnSettings);

            // Theme Toggle
            var btnTheme = new SButton
            {
                Text = Clr.IsDark ? "\u2600  Helles Design" : "\u263d  Dunkles Design",
                Location = new Point(M, 620),
                Size = new Size(CW, 36),
                BgNormal = Clr.Card,
                BgHover = Clr.CardHi,
                Font = new Font("Segoe UI", 9f),
                Tag = "theme"
            };
            btnTheme.Click += (sn, ev) =>
            {
                _cfg.DarkTheme = !_cfg.DarkTheme;
                _cfg.Save();
                MessageBox.Show("Das Design wird beim n\u00e4chsten Start von LoginPilot angewendet.", "Design", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            _mainPage.Controls.Add(btnTheme);

            var ver = AL("v3.1", new Font("Segoe UI", 7.5f), Clr.Brd, CW, 800);
            ver.Tag = "version";
            _mainPage.Controls.Add(ver);
        }

        // ========== CONFIG ==========
        void BuildConfigPage()
        {
            _cfgPage = new Panel { Dock = DockStyle.Fill, BackColor = Clr.Bg, Visible = false, AutoScroll = true };
            _cfgPage.HorizontalScroll.Maximum = 0;
            _cfgPage.AutoScrollMargin = new Size(0, 20);
            Controls.Add(_cfgPage);

            int y = 15;
            var fl = new Font("Segoe UI", 9f, FontStyle.Bold);
            int sp = 48;

            _cfgPage.Controls.Add(AL("\u2699  Einstellungen", new Font("Segoe UI", 16f, FontStyle.Bold), Clr.T1, M, y)); y += 40;

            if (_isFirstStart)
            {
                var welcome = AL("Willkommen! Bitte konfiguriere mindestens einen Standort.", new Font("Segoe UI", 9.5f), Clr.Org, M, y);
                welcome.AutoSize = false; welcome.Size = new Size(CW, 22);
                _cfgPage.Controls.Add(welcome); y += 28;
            }

            _cfgPage.Controls.Add(AL("STANDORT-PROFILE", new Font("Segoe UI", 8f, FontStyle.Bold), Clr.T2, M, y)); y += 21;

            _lstProfiles = new ListBox { Location = new Point(M, y), Size = new Size(170, 85), BackColor = Clr.Inp, ForeColor = Clr.T1, Font = new Font("Segoe UI", 10f), BorderStyle = BorderStyle.FixedSingle };
            _lstProfiles.SelectedIndexChanged += LstProf_Changed;
            _cfgPage.Controls.Add(_lstProfiles);

            _btnAdd = new SButton { Text = "+", Location = new Point(205, y + 4), Size = new Size(40, 30), BgNormal = Clr.GrnD, BgHover = Clr.GrnHi };
            _btnAdd.Click += BtnAdd_Click; _cfgPage.Controls.Add(_btnAdd);

            _btnRemove = new SButton { Text = "\u2212", Location = new Point(205, y + 42), Size = new Size(40, 30), BgNormal = Clr.RedD, BgHover = Clr.RedHi };
            _btnRemove.Click += BtnRem_Click; _cfgPage.Controls.Add(_btnRemove);

            y += 95;

            // Breiten fuer Config-Seite (SCW = schmaler wegen Scrollbar)
            int tbw = SCW - 60; // TextBox-Breite bei Browse-Button
            int bbx = M + tbw + 5; // Browse-Button X-Position
            int portX = M + SCW - 85; // Port-Feld X

            _cfgPage.Controls.Add(AL("Name", fl, Clr.T2, M, y));
            _txtName = TB(M, y + 19, SCW); _cfgPage.Controls.Add(_txtName); y += sp;

            _chkHomeOffice = new CheckBox { Text = "  HomeOffice / Au\u00dfendienst (kein Proxy)", Location = new Point(M, y), Size = new Size(SCW, 22), ForeColor = Clr.Blu, Font = new Font("Segoe UI", 9f, FontStyle.Bold), BackColor = Clr.Bg, Checked = false };
            _chkHomeOffice.CheckedChanged += (sn, ev) => ToggleProxyFields(!_chkHomeOffice.Checked);
            _cfgPage.Controls.Add(_chkHomeOffice); y += 28;

            _cfgPage.Controls.Add(AL("Proxy-Server", fl, Clr.T2, M, y));
            _cfgPage.Controls.Add(AL("Port", fl, Clr.T2, portX, y));
            _txtServer = TB(M, y + 19, portX - M - 8); _cfgPage.Controls.Add(_txtServer);
            _txtPort = TB(portX, y + 19, SCW - (portX - M)); _cfgPage.Controls.Add(_txtPort); y += sp;

            _cfgPage.Controls.Add(AL("Ausnahmen (Semikolon-getrennt)", fl, Clr.T2, M, y));
            _txtExceptions = TB(M, y + 19, SCW); _cfgPage.Controls.Add(_txtExceptions); y += sp;

            _cfgPage.Controls.Add(AL("Subnetz-Prefix (z.B. 192.168.151.)", fl, Clr.T2, M, y));
            _txtSubnet = TB(M, y + 19, SCW); _cfgPage.Controls.Add(_txtSubnet); y += sp;

            _cfgPage.Controls.Add(AL("RDP-Datei f\u00fcr diesen Standort", fl, Clr.Org, M, y));
            _txtRdp = TB(M, y + 19, tbw); _cfgPage.Controls.Add(_txtRdp);
            var btnBrowseRdp = new SButton { Text = "...", Location = new Point(bbx, y + 18), Size = new Size(50, 28), BgNormal = Clr.Card, BgHover = Clr.CardHi };
            btnBrowseRdp.Click += (sn, ev) => { using (var d = new OpenFileDialog()) { d.Filter = "RDP-Dateien|*.rdp|Alle|*.*"; d.InitialDirectory = @"C:\ZEUS"; if (d.ShowDialog() == DialogResult.OK) _txtRdp.Text = d.FileName; } };
            _cfgPage.Controls.Add(btnBrowseRdp); y += sp;

            // --- Feature-Toggles ---
            var sep1 = new Panel { Location = new Point(M, y), Size = new Size(SCW, 1), BackColor = Clr.Brd };
            _cfgPage.Controls.Add(sep1); y += 10;

            _cfgPage.Controls.Add(AL("FEATURES", new Font("Segoe UI", 8f, FontStyle.Bold), Clr.T2, M, y)); y += 22;

            _chkEnableRdp = new CheckBox { Text = "  Arbeitsplatz-Verbindung (RDP) aktivieren", Location = new Point(M, y), Size = new Size(SCW, 22), ForeColor = Clr.Org, Font = new Font("Segoe UI", 9f, FontStyle.Bold), BackColor = Clr.Bg, Checked = _cfg.EnableRdp };
            _chkEnableRdp.CheckedChanged += (sn, ev) => { _pnlRdpSettings.Visible = _chkEnableRdp.Checked; };
            _cfgPage.Controls.Add(_chkEnableRdp); y += 26;

            _pnlRdpSettings = new Panel { Location = new Point(0, y), Size = new Size(M + SCW + 5, 54), BackColor = Clr.Bg, Visible = _cfg.EnableRdp };
            _pnlRdpSettings.Controls.Add(AL("RDP-Datei wenn Proxy AUS (Homeoffice)", fl, Clr.Org, M, 2));
            _txtRdpOff = TB(M, 21, tbw); _txtRdpOff.Text = _cfg.RdpFileProxyOff; _pnlRdpSettings.Controls.Add(_txtRdpOff);
            var btnBrowseRdpOff = new SButton { Text = "...", Location = new Point(bbx, 20), Size = new Size(50, 28), BgNormal = Clr.Card, BgHover = Clr.CardHi };
            btnBrowseRdpOff.Click += (sn, ev) => { using (var d = new OpenFileDialog()) { d.Filter = "RDP-Dateien|*.rdp|Alle|*.*"; d.InitialDirectory = @"C:\ZEUS"; if (d.ShowDialog() == DialogResult.OK) _txtRdpOff.Text = d.FileName; } };
            _pnlRdpSettings.Controls.Add(btnBrowseRdpOff);
            _cfgPage.Controls.Add(_pnlRdpSettings); y += 58;

            _chkEnableVpn = new CheckBox { Text = "  VPN-Verbindung aktivieren (Homeoffice)", Location = new Point(M, y), Size = new Size(SCW, 22), ForeColor = Clr.Org, Font = new Font("Segoe UI", 9f, FontStyle.Bold), BackColor = Clr.Bg, Checked = _cfg.EnableVpn };
            _chkEnableVpn.CheckedChanged += (sn, ev) => { _pnlVpnSettings.Visible = _chkEnableVpn.Checked; };
            _cfgPage.Controls.Add(_chkEnableVpn); y += 26;

            _pnlVpnSettings = new Panel { Location = new Point(0, y), Size = new Size(M + SCW + 5, 102), BackColor = Clr.Bg, Visible = _cfg.EnableVpn };
            _pnlVpnSettings.Controls.Add(AL("VPN-Programm", fl, Clr.Org, M, 2));
            _txtVpnCmd = TB(M, 21, tbw); _txtVpnCmd.Text = _cfg.VpnCommand; _pnlVpnSettings.Controls.Add(_txtVpnCmd);
            var btnBrowseVpn = new SButton { Text = "...", Location = new Point(bbx, 20), Size = new Size(50, 28), BgNormal = Clr.Card, BgHover = Clr.CardHi };
            btnBrowseVpn.Click += (sn, ev) => { using (var d = new OpenFileDialog()) { d.Filter = "Programme|*.exe|Alle|*.*"; d.InitialDirectory = @"C:\Program Files"; if (d.ShowDialog() == DialogResult.OK) _txtVpnCmd.Text = d.FileName; } };
            _pnlVpnSettings.Controls.Add(btnBrowseVpn);
            _pnlVpnSettings.Controls.Add(AL("VPN-Argumente", fl, Clr.T2, M, 52));
            _txtVpnArgs = TB(M, 71, SCW); _txtVpnArgs.Text = _cfg.VpnArgs; _pnlVpnSettings.Controls.Add(_txtVpnArgs);
            _cfgPage.Controls.Add(_pnlVpnSettings); y += 106;

            // --- Allgemeine Einstellungen ---
            var sep2 = new Panel { Location = new Point(M, y), Size = new Size(SCW, 1), BackColor = Clr.Brd };
            _cfgPage.Controls.Add(sep2); y += 10;

            _cfgPage.Controls.Add(AL("ALLGEMEIN", new Font("Segoe UI", 8f, FontStyle.Bold), Clr.T2, M, y)); y += 22;

            _cfgPage.Controls.Add(AL("Logo-Datei (PNG, optional)", fl, Clr.T2, M, y));
            _txtLogo = TB(M, y + 19, tbw); _txtLogo.Text = _cfg.LogoFile; _cfgPage.Controls.Add(_txtLogo);
            _btnBrowseLogo = new SButton { Text = "...", Location = new Point(bbx, y + 18), Size = new Size(50, 28), BgNormal = Clr.Card, BgHover = Clr.CardHi };
            _btnBrowseLogo.Click += (sn, ev) => { using (var d = new OpenFileDialog()) { d.Filter = "Bilder|*.png;*.jpg;*.bmp;*.gif"; if (d.ShowDialog() == DialogResult.OK) _txtLogo.Text = d.FileName; } };
            _cfgPage.Controls.Add(_btnBrowseLogo); y += sp + 2;

            var fc = new Font("Segoe UI", 9f);
            _chkAutoDetect = new CheckBox { Text = "  Standort beim Start automatisch erkennen", Location = new Point(M, y), Size = new Size(SCW, 22), ForeColor = Clr.T1, Font = fc, BackColor = Clr.Bg, Checked = _cfg.AutoDetectOnStart };
            _cfgPage.Controls.Add(_chkAutoDetect); y += 24;

            _chkAutostart = new CheckBox { Text = "  Autostart f\u00fcr alle Benutzer", Location = new Point(M, y), Size = new Size(SCW, 22), ForeColor = Clr.T1, Font = fc, BackColor = Clr.Bg, Checked = AutostartManager.IsInstalled() };
            _cfgPage.Controls.Add(_chkAutostart); y += 24;

            _chkStartMin = new CheckBox { Text = "  Minimiert starten (nur Tray-Icon)", Location = new Point(M, y), Size = new Size(SCW, 22), ForeColor = Clr.T1, Font = fc, BackColor = Clr.Bg, Checked = _cfg.StartMinimized };
            _cfgPage.Controls.Add(_chkStartMin); y += 24;

            _chkLockConfig = new CheckBox { Text = "  Einstellungen f\u00fcr Benutzer sperren", Location = new Point(M, y), Size = new Size(SCW, 22), ForeColor = Clr.Org, Font = new Font("Segoe UI", 9f, FontStyle.Bold), BackColor = Clr.Bg, Checked = _cfg.ConfigLocked };
            _cfgPage.Controls.Add(_chkLockConfig); y += 22;

            _cfgPage.Controls.Add(AL("Tipp: Gesperrte Einstellungen mit # oder /k \u00f6ffnen", new Font("Segoe UI", 8f, FontStyle.Italic), Clr.T2, M, y));
            y += 28;

            int btnW = (SCW - 10) / 2;
            _btnSave = new SButton { Text = "\ud83d\udcbe  Speichern", Location = new Point(M, y), Size = new Size(btnW, 38), BgNormal = Clr.GrnD, BgHover = Clr.GrnHi };
            _btnSave.Click += BtnSave_Click; _cfgPage.Controls.Add(_btnSave);

            _btnBack = new SButton { Text = "\u2190 Zur\u00fcck", Location = new Point(M + btnW + 10, y), Size = new Size(btnW, 38), BgNormal = Clr.Card, BgHover = Clr.CardHi };
            _btnBack.Click += (sn, ev) => ShowMain(); _cfgPage.Controls.Add(_btnBack);

            RefreshProfileList();
        }

        // ========== LOGO ==========
        void LoadLogo()
        {
            _logoBox.Visible = false;
            string lp = _cfg.LogoFile;
            if (string.IsNullOrEmpty(lp)) lp = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logo.png");
            try
            {
                if (File.Exists(lp))
                {
                    _logoBox.Image = Image.FromFile(lp); _logoBox.Visible = true;
                    foreach (Control c in _mainPage.Controls) if (c != _logoBox) c.Top += 45;
                }
            }
            catch { }
        }

        // ========== ARBEITSPLATZ STARTEN ==========
        string GetCurrentRdpFile()
        {
            if (!_cfg.EnableRdp) return null;

            if (ProxyManager.IsEnabled())
            {
                string srv = ProxyManager.GetServer();
                foreach (var p in _cfg.Profiles)
                    if (!p.IsHomeOffice && p.ProxyAddress == srv && !string.IsNullOrEmpty(p.RdpFile))
                        return p.RdpFile;
            }
            else
            {
                // Schaue ob ein HomeOffice-Profil gewaehlt ist und eine eigene RDP hat
                string last = _cfg.LastProfileName ?? "";
                foreach (var p in _cfg.Profiles)
                    if (p.IsHomeOffice && p.Name == last && !string.IsNullOrEmpty(p.RdpFile))
                        return p.RdpFile;
            }

            // Fallback: globale HomeOffice-RDP
            if (!string.IsNullOrEmpty(_cfg.RdpFileProxyOff))
                return _cfg.RdpFileProxyOff;
            return null;
        }

        void LaunchWts()
        {
            if (!_cfg.EnableRdp) return;
            if (_rdpLaunchInProgress) return; // FIX: Doppelstart verhindern
            _rdpLaunchInProgress = true;      // FIX: Verbindungsvorgang markieren
            _wtsConnected = false; // Bei jedem Klick zuruecksetzen
            ResetWtsButton();

            string rdpFile = GetCurrentRdpFile();
            if (string.IsNullOrEmpty(rdpFile))
            {
                MessageBox.Show("Keine RDP-Datei konfiguriert f\u00fcr den aktuellen Modus.", "Arbeitsplatz", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string found = RdpLauncher.FindRdp(rdpFile);
            if (found == null)
            {
                MessageBox.Show("RDP-Datei nicht gefunden:\n" + rdpFile + "\n\nBitte in C:\\ZEUS\\ ablegen oder vollst\u00e4ndigen Pfad in den Einstellungen angeben.",
                    "Arbeitsplatz", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Im Buero (Proxy EIN) -> direkt RDP starten
            if (ProxyManager.IsEnabled())
            {
                DoLaunchRdp(rdpFile, found);
                return;
            }

            // HomeOffice: VPN pruefen
            if (_cfg.EnableVpn)
            {
                bool vpnUp = NetDetect.IsVpnConnected();

                if (vpnUp)
                {
                    // VPN steht bereits -> Tunnel-Check, dann RDP
                    _lblWtsInfo.ForeColor = Clr.Org;
                    _lblWtsInfo.Text = "\u23f3 VPN verbunden \u2013 pr\u00fcfe Tunnel...";
                    _btnWts.Enabled = false;
                    _btnWts.Text = "\u23f3  PR\u00dcFE TUNNEL...";
                    StartTunnelCheck(rdpFile, found);
                    return;
                }

                // VPN nicht verbunden -> VPN starten
                if (!string.IsNullOrEmpty(_cfg.VpnCommand) && File.Exists(_cfg.VpnCommand))
                {
                    // Pruefen ob OpenVPN-GUI bereits laeuft
                    bool guiRunning = false;
                    try
                    {
                        string exeName = Path.GetFileNameWithoutExtension(_cfg.VpnCommand);
                        var procs = System.Diagnostics.Process.GetProcessesByName(exeName);
                        guiRunning = procs.Length > 0;
                        foreach (var pr in procs) pr.Dispose();
                    }
                    catch { }

                    try
                    {
                        if (!guiRunning)
                        {
                            // GUI erst starten, dann connect-Befehl verz\u00f6gert senden
                            var psi = new System.Diagnostics.ProcessStartInfo();
                            psi.FileName = _cfg.VpnCommand;
                            psi.UseShellExecute = true;
                            System.Diagnostics.Process.Start(psi);

                            // Kurz warten bis GUI bereit ist, dann connect senden
                            var connectTimer = new Timer();
                            connectTimer.Interval = 2000;
                            connectTimer.Tick += (ct, ce) =>
                            {
                                connectTimer.Stop(); connectTimer.Dispose();
                                try
                                {
                                    var psi2 = new System.Diagnostics.ProcessStartInfo();
                                    psi2.FileName = _cfg.VpnCommand;
                                    psi2.Arguments = _cfg.VpnArgs;
                                    psi2.UseShellExecute = true;
                                    System.Diagnostics.Process.Start(psi2);
                                }
                                catch { }
                            };
                            connectTimer.Start();
                        }
                        else
                        {
                            // GUI l\u00e4uft schon -> direkt connect senden
                            var psi = new System.Diagnostics.ProcessStartInfo();
                            psi.FileName = _cfg.VpnCommand;
                            psi.Arguments = _cfg.VpnArgs;
                            psi.UseShellExecute = true;
                            System.Diagnostics.Process.Start(psi);
                        }
                    }
                    catch { }

                    // Warte auf VPN-Verbindung (2FA etc.)
                    _lblWtsInfo.ForeColor = Clr.Org;
                    _lblWtsInfo.Text = "\u23f3 Warte auf VPN-Verbindung (2FA)...";
                    _btnWts.Enabled = true;
                    _btnWts.Text = "\u2716  ABBRECHEN";
                    _btnWts.BgNormal = Clr.RedD; _btnWts.BgHover = Clr.RedHi;
                    _btnWts.Invalidate();

                    // Fokus zurueckholen nach OpenVPN-GUI Start
                    var focusTimer = new Timer();
                    focusTimer.Interval = 500;
                    focusTimer.Tick += (ft, fe) => { focusTimer.Stop(); focusTimer.Dispose(); Activate(); };
                    focusTimer.Start();

                    var vpnTimer = new Timer();
                    vpnTimer.Interval = 2000;
                    int elapsed = 0;
                    int maxWait = 120;
                    bool cancelled = false;

                    // Abbrechen-Handler: Button-Click stoppt den Timer
                    EventHandler cancelHandler = null;
                    cancelHandler = (cs, ce) =>
                    {
                        _rdpLaunchInProgress = false; // FIX: Guard zurücksetzen bei Abbruch
                        cancelled = true;
                        vpnTimer.Stop(); vpnTimer.Dispose();
                        _btnWts.Click -= cancelHandler;
                        _btnWts.Click += _wtsClickHandler;
                        ResetWtsButton();
                        _lblWtsInfo.ForeColor = Clr.T2;
                        _lblWtsInfo.Text = "VPN-Verbindung abgebrochen";
                        Activate(); Focus();
                    };

                    // Original Click-Handler tempor\u00e4r ersetzen
                    _btnWts.Click -= _wtsClickHandler;
                    _btnWts.Click += cancelHandler;

                    vpnTimer.Tick += (ts, te) =>
                    {
                        if (cancelled) { vpnTimer.Stop(); vpnTimer.Dispose(); return; }
                        elapsed += 2;
                        if (NetDetect.IsVpnConnected())
                        {
                            vpnTimer.Stop(); vpnTimer.Dispose();
                            _btnWts.Click -= cancelHandler;
                            _btnWts.Click += _wtsClickHandler;
                            _lblWtsInfo.ForeColor = Clr.Org;
                            _lblWtsInfo.Text = "\u23f3 VPN verbunden \u2013 pr\u00fcfe Tunnel-Stabilit\u00e4t...";
                            _btnWts.Enabled = false;
                            _btnWts.Text = "\u23f3  PR\u00dcFE TUNNEL...";
                            _btnWts.BgNormal = Clr.OrgD; _btnWts.BgHover = Clr.OrgHi;
                            _btnWts.Invalidate();
                            StartTunnelCheck(rdpFile, found);
                        }
                        else if (elapsed >= maxWait)
                        {
                            vpnTimer.Stop(); vpnTimer.Dispose();
                            _btnWts.Click -= cancelHandler;
                            _btnWts.Click += _wtsClickHandler;
                            ResetWtsButton();
                            _lblWtsInfo.ForeColor = Clr.Red;
                            _lblWtsInfo.Text = "\u26a0 VPN-Timeout \u2013 keine Verbindung nach " + maxWait + "s";
                        }
                        else
                        {
                            _lblWtsInfo.Text = "\u23f3 Warte auf VPN... (" + elapsed + "s) \u2013 2FA best\u00e4tigen oder abbrechen";
                        }
                    };
                    vpnTimer.Start();
                    return;
                }
            }

            // Fallback: Kein VPN konfiguriert oder VPN deaktiviert -> direkt RDP
            DoLaunchRdp(rdpFile, found);
        }

        void StartTunnelCheck(string rdpFile, string foundPath)
        {
            var stabilityTimer = new Timer();
            stabilityTimer.Interval = 2500;
            int stabilityChecks = 0;
            int successCount = 0;
            int requiredSuccesses = 2;
            int maxStabilityChecks = 12;
            stabilityTimer.Tick += (ss, se) =>
            {
                stabilityChecks++;
                string rdpHost = NetDetect.ExtractRdpHost(foundPath);
                bool reachable = false;
                if (!string.IsNullOrEmpty(rdpHost))
                    reachable = NetDetect.IsTcpReachable(rdpHost, 3389, 2000);
                else
                    reachable = stabilityChecks >= 4;

                if (reachable)
                    successCount++;
                else
                    successCount = 0;

                if (successCount >= requiredSuccesses)
                {
                    stabilityTimer.Stop(); stabilityTimer.Dispose();
                    _lblWtsInfo.ForeColor = Clr.Grn;
                    _lblWtsInfo.Text = "\u2713 VPN-Tunnel steht \u2013 starte RDP...";
                    var launchTimer = new Timer();
                    launchTimer.Interval = 2000;
                    launchTimer.Tick += (ls, le) =>
                    {
                        launchTimer.Stop(); launchTimer.Dispose();
                        DoLaunchRdp(rdpFile, foundPath);
                    };
                    launchTimer.Start();
                }
                else if (stabilityChecks >= maxStabilityChecks)
                {
                    stabilityTimer.Stop(); stabilityTimer.Dispose();
                    _rdpLaunchInProgress = false; // FIX: Guard zurücksetzen bei Tunnel-Timeout
                    _btnWts.Enabled = true;
                    _btnWts.Text = "\u25b6  ARBEITSPLATZ STARTEN";
                    _lblWtsInfo.ForeColor = Clr.Red;
                    _lblWtsInfo.Text = "\u26a0 VPN steht, aber RDP-Host nicht erreichbar";
                }
                else
                {
                    string dots = new string('.', (stabilityChecks % 3) + 1);
                    _lblWtsInfo.Text = "\u23f3 Tunnel wird stabilisiert" + dots + " (" + (stabilityChecks * 2.5).ToString("0") + "s)";
                }
            };
            stabilityTimer.Start();
        }

        void DoLaunchRdp(string rdpFile, string foundPath)
        {
            _rdpLaunchInProgress = false; // FIX: Guard zurücksetzen
            if (RdpLauncher.Launch(rdpFile))
            {
                SetWtsConnected(rdpFile);
            }
            else
            {
                _btnWts.Enabled = true;
                _btnWts.Text = "\u25b6  ARBEITSPLATZ STARTEN";
                MessageBox.Show("Fehler beim Starten von:\n" + foundPath, "Arbeitsplatz", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        bool _wtsConnected = false;
        bool _rdpLaunchInProgress = false; // FIX: Guard gegen doppelten RDP-Start
        EventHandler _wtsClickHandler;

        void ResetWtsButton()
        {
            if (_wtsConnected) return; // Nicht zuruecksetzen wenn gerade verbunden
            _btnWts.BgNormal = Clr.OrgD; _btnWts.BgHover = Clr.OrgHi;
            _btnWts.Text = "\u25b6  ARBEITSPLATZ STARTEN"; _btnWts.Enabled = true;
            _btnWts.Invalidate();
        }

        void SetWtsConnected(string rdpFile)
        {
            _wtsConnected = true;
            _btnWts.Enabled = true; // Immer klickbar!
            _btnWts.BgNormal = Clr.GrnD; _btnWts.BgHover = Clr.GrnHi;
            _btnWts.Text = "\u2713  VERBUNDEN \u2022 erneut \u25b6"; _btnWts.Invalidate();
            _lblWtsInfo.ForeColor = Clr.Grn;
            _lblWtsInfo.Text = "\u25b6 " + Path.GetFileName(rdpFile) + " gestartet";
        }

        void UpdateWtsInfo()
        {
            if (!_cfg.EnableRdp) { _lblWtsInfo.Text = ""; return; }
            if (!_wtsConnected) ResetWtsButton();
            string rdp = GetCurrentRdpFile();
            if (!string.IsNullOrEmpty(rdp))
            {
                string found = RdpLauncher.FindRdp(rdp);
                if (found == null)
                {
                    _lblWtsInfo.ForeColor = Clr.RedHi;
                    _lblWtsInfo.Text = "\u26a0 " + Path.GetFileName(rdp) + " nicht gefunden";
                }
                else if (_cfg.EnableVpn && !ProxyManager.IsEnabled() && !NetDetect.IsVpnConnected())
                {
                    _lblWtsInfo.ForeColor = Clr.Org;
                    _lblWtsInfo.Text = "\u26a0 VPN nicht verbunden \u2022 " + Path.GetFileName(rdp);
                }
                else
                {
                    _lblWtsInfo.ForeColor = Clr.T2;
                    _lblWtsInfo.Text = "RDP: " + Path.GetFileName(rdp);
                }
            }
            else
            {
                _lblWtsInfo.ForeColor = Clr.T2;
                _lblWtsInfo.Text = "Keine RDP konfiguriert";
            }
        }

        // ========== EVENTS ==========
        void BtnAn_Click(object sender, EventArgs e)
        {
            if (_cmbProfile.SelectedIndex < 0 || _cmbProfile.SelectedIndex >= _cfg.Profiles.Count)
            { MessageBox.Show("Bitte zuerst einen Standort w\u00e4hlen.", "Hinweis", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            var p = _cfg.Profiles[_cmbProfile.SelectedIndex];
            _wtsConnected = false;
            if (p.IsHomeOffice)
            {
                // HomeOffice: Proxy AUS
                ProxyManager.Disable(); SetActive(_btnAus); SaveLast("off", p.Name);
            }
            else
            {
                ProxyManager.Enable(p); SetActive(_btnAn); SaveLast("on", p.Name);
            }
            RefreshStatus(); UpdateTrayIcon(); UpdateWtsInfo();
        }

        void BtnAus_Click(object sender, EventArgs e)
        { _wtsConnected = false; ProxyManager.Disable(); SetActive(_btnAus); SaveLast("off", ""); RefreshStatus(); UpdateTrayIcon(); UpdateWtsInfo(); }

        void ApplyProxyButtonTheme()
        {
            if (Clr.IsDark)
            {
                var norm = Color.FromArgb(55, 58, 68);
                var hover = Color.FromArgb(68, 72, 84);
                _btnAn.BgNormal = norm; _btnAn.BgHover = hover;
                _btnAus.BgNormal = norm; _btnAus.BgHover = hover;
                _btnAuto.BgNormal = norm; _btnAuto.BgHover = hover;
            }
            else
            {
                var norm = Color.FromArgb(200, 204, 212);
                var hover = Color.FromArgb(180, 185, 195);
                _btnAn.BgNormal = norm; _btnAn.BgHover = hover;
                _btnAus.BgNormal = norm; _btnAus.BgHover = hover;
                _btnAuto.BgNormal = norm; _btnAuto.BgHover = hover;
            }
            _btnAn.Invalidate(); _btnAus.Invalidate(); _btnAuto.Invalidate();
        }

        void SetActive(SButton b)
        {
            _btnAn.IsActive = b == _btnAn;
            _btnAus.IsActive = b == _btnAus;
            _btnAuto.IsActive = b == _btnAuto;

            if (Clr.IsDark)
            {
                // Dark: Inaktive neutral-dunkelgrau, aktiver dezente Farbe
                var norm = Color.FromArgb(55, 58, 68);
                var hover = Color.FromArgb(68, 72, 84);
                _btnAn.BgNormal = norm; _btnAn.BgHover = hover;
                _btnAus.BgNormal = norm; _btnAus.BgHover = hover;
                _btnAuto.BgNormal = norm; _btnAuto.BgHover = hover;

                if (b == _btnAn) { _btnAn.BgNormal = Color.FromArgb(35, 95, 62); _btnAn.BgHover = Color.FromArgb(42, 112, 74); }
                else if (b == _btnAus) { _btnAus.BgNormal = Color.FromArgb(105, 45, 40); _btnAus.BgHover = Color.FromArgb(125, 55, 48); }
                else if (b == _btnAuto) { _btnAuto.BgNormal = Color.FromArgb(35, 75, 120); _btnAuto.BgHover = Color.FromArgb(42, 90, 140); }
            }
            else
            {
                // Light: Inaktive grau, aktiver dezente Farbe
                var norm = Color.FromArgb(200, 204, 212);
                var hover = Color.FromArgb(180, 185, 195);
                _btnAn.BgNormal = norm; _btnAn.BgHover = hover;
                _btnAus.BgNormal = norm; _btnAus.BgHover = hover;
                _btnAuto.BgNormal = norm; _btnAuto.BgHover = hover;

                if (b == _btnAn) { _btnAn.BgNormal = Color.FromArgb(180, 215, 195); _btnAn.BgHover = Color.FromArgb(165, 205, 182); }
                else if (b == _btnAus) { _btnAus.BgNormal = Color.FromArgb(218, 192, 190); _btnAus.BgHover = Color.FromArgb(205, 180, 178); }
                else if (b == _btnAuto) { _btnAuto.BgNormal = Color.FromArgb(185, 205, 225); _btnAuto.BgHover = Color.FromArgb(170, 192, 215); }
            }

            _btnAn.Invalidate(); _btnAus.Invalidate(); _btnAuto.Invalidate();
        }

        void AutoDetect()
        {
            var d = NetDetect.Detect(_cfg.Profiles);
            if (d != null && !d.IsHomeOffice)
            {
                ProxyManager.Enable(d); _lblStandort.ForeColor = Clr.Grn;
                _lblStandort.Text = "\u26a1 Erkannt: " + d.Name + " \u2013 mit Firmennetzwerk verbunden";
                for (int i = 0; i < _cfg.Profiles.Count; i++) if (_cfg.Profiles[i].Name == d.Name) { _cmbProfile.SelectedIndex = i; break; }
            }
            else
            {
                ProxyManager.Disable();
                // HomeOffice-Profil automatisch waehlen
                bool hoFound = false;
                for (int i = 0; i < _cfg.Profiles.Count; i++)
                {
                    if (_cfg.Profiles[i].IsHomeOffice)
                    {
                        _cmbProfile.SelectedIndex = i;
                        _lblStandort.ForeColor = Clr.Blu;
                        _lblStandort.Text = "\u26a1 " + _cfg.Profiles[i].Name + " \u2013 nicht im Firmennetzwerk";
                        hoFound = true;
                        break;
                    }
                }
                if (!hoFound)
                {
                    _lblStandort.ForeColor = Clr.Org;
                    _lblStandort.Text = "\u26a1 Kein Firmennetzwerk erkannt \u2013 nicht im Firmennetzwerk";
                }
            }
            RefreshStatus(); UpdateTrayIcon(); UpdateWtsInfo();
        }

        void ToggleProxyFields(bool show)
        {
            _txtServer.Enabled = show; _txtPort.Enabled = show;
            _txtExceptions.Enabled = show; _txtSubnet.Enabled = show;
            Color c = show ? Clr.Inp : Clr.Card;
            _txtServer.BackColor = c; _txtPort.BackColor = c;
            _txtExceptions.BackColor = c; _txtSubnet.BackColor = c;
        }

        void LstProf_Changed(object sender, EventArgs e)
        {
            if (_lstProfiles.SelectedIndex < 0) return;
            var p = _cfg.Profiles[_lstProfiles.SelectedIndex];
            _txtName.Text = p.Name; _txtServer.Text = p.Server; _txtPort.Text = p.Port.ToString();
            _txtExceptions.Text = p.Exceptions; _txtSubnet.Text = p.SubnetPrefix;
            _txtRdp.Text = p.RdpFile;
            _chkHomeOffice.Checked = p.IsHomeOffice;
            ToggleProxyFields(!p.IsHomeOffice);
        }

        void BtnAdd_Click(object s, EventArgs e)
        { _cfg.Profiles.Add(new ProxyProfile("Neuer Standort", "", 3128, "<local>", "", "")); RefreshProfileList(); _lstProfiles.SelectedIndex = _lstProfiles.Items.Count - 1; }

        void BtnRem_Click(object s, EventArgs e)
        {
            if (_lstProfiles.SelectedIndex < 0) return;
            if (MessageBox.Show("\"" + _cfg.Profiles[_lstProfiles.SelectedIndex].Name + "\" l\u00f6schen?", "L\u00f6schen", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            { _cfg.Profiles.RemoveAt(_lstProfiles.SelectedIndex); RefreshProfileList(); }
        }

        void BtnSave_Click(object sender, EventArgs e)
        {
            if (_lstProfiles.SelectedIndex >= 0)
            {
                var p = _cfg.Profiles[_lstProfiles.SelectedIndex];
                p.Name = _txtName.Text.Trim(); p.Server = _txtServer.Text.Trim();
                int pt; p.Port = int.TryParse(_txtPort.Text.Trim(), out pt) ? pt : 3128;
                p.Exceptions = _txtExceptions.Text.Trim(); p.SubnetPrefix = _txtSubnet.Text.Trim();
                p.RdpFile = _txtRdp.Text.Trim();
                p.IsHomeOffice = _chkHomeOffice.Checked;
            }
            _cfg.AutoDetectOnStart = _chkAutoDetect.Checked;
            _cfg.ConfigLocked = _chkLockConfig.Checked;
            _cfg.StartMinimized = _chkStartMin.Checked;
            _cfg.LogoFile = _txtLogo.Text.Trim();
            _cfg.EnableRdp = _chkEnableRdp.Checked;
            _cfg.EnableVpn = _chkEnableVpn.Checked;
            _cfg.RdpFileProxyOff = _txtRdpOff.Text.Trim();
            _cfg.VpnCommand = _txtVpnCmd.Text.Trim();
            _cfg.VpnArgs = _txtVpnArgs.Text.Trim();
            _cfg.Save();

            if (_chkAutostart.Checked && !AutostartManager.IsInstalled())
            { if (!AutostartManager.InstallSimple()) MessageBox.Show("Autostart konnte nicht eingerichtet werden.\nBitte als Administrator.", "Hinweis", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            else if (!_chkAutostart.Checked && AutostartManager.IsInstalled()) AutostartManager.Uninstall();

            _isFirstStart = false;
            RefreshProfileList(); PopulateCombo(); RebuildTrayMenu();
            _btnSettings.Visible = !_cfg.ConfigLocked;
            UpdateMainLayout();
            MessageBox.Show("Einstellungen gespeichert!\n\nConfig: " + AppConfig.ConfigPath, "OK", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        void ShowConfig()
        {
            _mainPage.Visible = false; _cfgPage.Visible = true; _cfgPage.BringToFront();
            _chkAutostart.Checked = AutostartManager.IsInstalled();
            RefreshProfileList(); if (_lstProfiles.Items.Count > 0) _lstProfiles.SelectedIndex = 0;
        }

        void ShowMain() { _cfgPage.Visible = false; _mainPage.Visible = true; _mainPage.BringToFront(); RefreshStatus(); UpdateWtsInfo(); }

        void RefreshStatus()
        {
            bool on = ProxyManager.IsEnabled(); string srv = ProxyManager.GetServer(); string exc = ProxyManager.GetExceptions(); var ips = NetDetect.GetIPs();
            if (on)
            {
                string name = "Unbekannt"; foreach (var p in _cfg.Profiles) if (p.ProxyAddress == srv) { name = p.Name; break; }
                _lblStatus.Text = "\u25cf Im Firmennetzwerk"; _lblStatus.ForeColor = Clr.Grn; _statusIndicator.BackColor = Clr.Grn;
                _lblStatusDetail.Text = "Mit dem Firmennetzwerk verbunden (Proxy Ein)";
                _lblProxy.Text = "Server: " + srv;
                _lblExceptions.Text = "Ausnahmen: " + (exc.Length > 42 ? exc.Substring(0, 39) + "..." : exc);
            }
            else
            {
                bool vpnUp = _cfg.EnableVpn && NetDetect.IsVpnConnected();
                if (vpnUp)
                {
                    _lblStatus.Text = "\u25cb HO / Extern"; _lblStatus.ForeColor = Clr.Blu; _statusIndicator.BackColor = Clr.Blu;
                    _lblStatusDetail.Text = "\u00dcber VPN mit dem Firmennetzwerk verbunden (Proxy Aus)";
                }
                else
                {
                    _lblStatus.Text = "\u25cb HO / Extern"; _lblStatus.ForeColor = Clr.Org; _statusIndicator.BackColor = Clr.Org;
                    _lblStatusDetail.Text = "Keine Verbindung zum Firmennetzwerk (Proxy Aus)";
                }
                _lblProxy.Text = ""; _lblExceptions.Text = "";
            }
            _lblNetwork.Text = "IP: " + (ips.Count > 0 ? string.Join(", ", ips) : "Keine Verbindung");
        }

        void UpdateTrayIcon()
        {
            bool proxyOn = ProxyManager.IsEnabled();
            string profileName = "";
            ProxyProfile activeProfile = null;

            if (proxyOn)
            {
                foreach (var p in _cfg.Profiles)
                    if (!p.IsHomeOffice && p.ProxyAddress == ProxyManager.GetServer()) { profileName = p.Name; activeProfile = p; break; }
            }
            else
            {
                // HomeOffice: Schaue ob ein HomeOffice-Profil als letztes gewählt war
                string last = _cfg.LastProfileName ?? "";
                foreach (var p in _cfg.Profiles)
                    if (p.IsHomeOffice && p.Name == last) { profileName = p.Name; activeProfile = p; break; }
            }

            Color iconColor;
            string tooltip;
            string label = "";

            if (proxyOn)
            {
                iconColor = Clr.Grn;
                tooltip = "LoginPilot: " + profileName;
                if (profileName.Length >= 2)
                    label = profileName.Substring(0, 2).ToUpper();
                else if (profileName.Length > 0)
                    label = profileName.Substring(0, 1).ToUpper();
            }
            else if (_cfg.EnableVpn && NetDetect.IsVpnConnected())
            {
                iconColor = Clr.Blu;
                tooltip = "LoginPilot: " + (profileName.Length > 0 ? profileName : "HomeOffice") + " (VPN)";
                label = "HO";
            }
            else
            {
                iconColor = Clr.Org;
                tooltip = "LoginPilot: " + (profileName.Length > 0 ? profileName : "HomeOffice") + " (kein VPN)";
                label = "HO";
            }

            _tray.Icon = MkTrayIcon(iconColor, label);
            _tray.Text = tooltip;
        }

        void RefreshProfileList() { _lstProfiles.Items.Clear(); foreach (var p in _cfg.Profiles) _lstProfiles.Items.Add(p.Name); }
        void PopulateCombo() { _cmbProfile.Items.Clear(); foreach (var p in _cfg.Profiles) _cmbProfile.Items.Add(p.Name); if (_cmbProfile.Items.Count > 0) _cmbProfile.SelectedIndex = 0; }

        Icon MkTrayIcon(Color c, string label = "")
        {
            var b = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                g.Clear(Color.Transparent);

                // Hintergrund-Kreis
                using (var br = new SolidBrush(c)) g.FillEllipse(br, 0, 0, 15, 15);
                using (var pn = new Pen(Color.FromArgb(180, 255, 255, 255), 1.2f)) g.DrawEllipse(pn, 0, 0, 15, 15);

                // Text-Label im Icon
                if (!string.IsNullOrEmpty(label))
                {
                    float fontSize = label.Length > 2 ? 5.5f : (label.Length > 1 ? 6f : 7.5f);
                    using (var f = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (var tb = new SolidBrush(Color.White))
                    {
                        var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                        g.DrawString(label, f, tb, new RectangleF(0, 0, 16, 16), sf);
                    }
                }
            }
            return Icon.FromHandle(b.GetHicon());
        }

        protected override void OnFormClosing(FormClosingEventArgs e) { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } else _tray.Visible = false; }
        protected override void OnLoad(EventArgs e) { base.OnLoad(e); UpdateTrayIcon(); UpdateWtsInfo(); }

        // ========== NETZWERK-WATCHER ==========
        Timer _netDebounce;
        string _lastNetworkHash = "";

        void InitNetworkWatcher()
        {
            // Bei IP-Adress\u00e4nderungen (WLAN-Wechsel, Kabel rein/raus, VPN connect/disconnect)
            NetworkChange.NetworkAddressChanged += OnNetworkChanged;
            // Bei Resume aus Sleep/Hibernate
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
        }

        void OnNetworkChanged(object sender, EventArgs e)
        {
            // Debounce: Netzwerk\u00e4nderungen kommen oft in Salven (2-5 Events hintereinander)
            if (InvokeRequired) { try { Invoke(new Action(() => ScheduleNetworkRecheck())); } catch { } return; }
            ScheduleNetworkRecheck();
        }

        void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume)
            {
                // Nach Resume etwas warten bis Netzwerk steht
                if (InvokeRequired) { try { Invoke(new Action(() => ScheduleNetworkRecheck(5000))); } catch { } return; }
                ScheduleNetworkRecheck(5000);
            }
        }

        void ScheduleNetworkRecheck(int delayMs = 2000)
        {
            if (_netDebounce != null) { _netDebounce.Stop(); _netDebounce.Dispose(); }
            _netDebounce = new Timer();
            _netDebounce.Interval = delayMs;
            _netDebounce.Tick += (s, ev) =>
            {
                _netDebounce.Stop(); _netDebounce.Dispose(); _netDebounce = null;
                DoNetworkRecheck();
            };
            _netDebounce.Start();
        }

        void DoNetworkRecheck()
        {
            // Nur im AUTO-Modus automatisch reagieren
            if ((_cfg.LastMode ?? "").ToLower() != "auto") return;

            // Pr\u00fcfen ob sich das Netzwerk tats\u00e4chlich ge\u00e4ndert hat
            string currentHash = GetNetworkHash();
            if (currentHash == _lastNetworkHash) return;
            _lastNetworkHash = currentHash;

            // Neuerkennung durchf\u00fchren
            _wtsConnected = false;
            SetActive(_btnAuto);
            AutoDetect();
            RebuildTrayMenu();

            // Im HomeOffice: automatisch VPN + RDP starten
            if (_cfg.EnableRdp && !ProxyManager.IsEnabled() && !_wtsConnected && !_rdpLaunchInProgress) // FIX: Nicht starten wenn bereits verbunden oder Vorgang läuft
            {
                // Kurz warten bis Netzwerk stabil ist, dann Arbeitsplatz starten
                var autoLaunchTimer = new Timer();
                autoLaunchTimer.Interval = 3000;
                autoLaunchTimer.Tick += (s, ev) =>
                {
                    autoLaunchTimer.Stop(); autoLaunchTimer.Dispose();
                    LaunchWts();
                };
                autoLaunchTimer.Start();
            }
        }

        string GetNetworkHash()
        {
            try
            {
                var ips = new List<string>();
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var addr in ni.GetIPProperties().UnicastAddresses)
                        if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                            ips.Add(addr.Address.ToString());
                }
                ips.Sort();
                return string.Join(",", ips);
            }
            catch { return ""; }
        }
    }

    public class DarkRenderer : ToolStripProfessionalRenderer
    {
        public DarkRenderer() : base(new DarkColors()) { }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) { e.TextColor = Clr.T1; base.OnRenderItemText(e); }
    }

    public class DarkColors : ProfessionalColorTable
    {
        public override Color MenuItemSelected { get { return Clr.CardHi; } }
        public override Color MenuItemBorder { get { return Clr.Brd; } }
        public override Color ToolStripDropDownBackground { get { return Clr.Card; } }
        public override Color ImageMarginGradientBegin { get { return Clr.Card; } }
        public override Color ImageMarginGradientMiddle { get { return Clr.Card; } }
        public override Color ImageMarginGradientEnd { get { return Clr.Card; } }
        public override Color SeparatorDark { get { return Clr.Brd; } }
        public override Color SeparatorLight { get { return Clr.Card; } }
    }

    static class Program
    {
        static System.Threading.Mutex _mutex;
        [STAThread]
        static void Main(string[] args)
        {
            // Prozess-Prioritaet erhoehen: LoginPilot startet schneller als normale
            // Hintergrundprozesse beim Windows-Login
            try { System.Diagnostics.Process.GetCurrentProcess().PriorityClass =
                System.Diagnostics.ProcessPriorityClass.AboveNormal; } catch { }

            bool created; _mutex = new System.Threading.Mutex(true, "LoginPilot_SingleInstance", out created);
            if (!created) { MessageBox.Show("LoginPilot l\u00e4uft bereits.\n\nDoppelklick auf das Tray-Icon zum \u00d6ffnen.", "LoginPilot", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            bool cfg = false; foreach (string a in args) if (a.ToLower() == "/k" || a.ToLower() == "-k") cfg = true;

            // Auto-Update im Hintergrund pruefen – blockiert den Start nicht
            AutoUpdater.CheckAndUpdateAsync();

            Application.Run(new MainForm(cfg));
        }
    }
}
