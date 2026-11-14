// Mouse Battery Tray - shows the battery level of a wireless mouse (Pulsar, Logitech) in the Windows notification area.
// Pulsar protocol taken from the open-source project python-pulsar-mouse-tool (andrewrabert, MIT license).
// Logitech support uses the HID++ 2.0 protocol (device name 0x0005, battery features 0x1004, 0x1000, 0x1001).
// Build: see build.ps1 (uses the csc.exe that ships with Windows, no dependencies).

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

[assembly: System.Reflection.AssemblyTitle("Mouse Battery Tray")]
[assembly: System.Reflection.AssemblyProduct("Mouse Battery Tray")]
[assembly: System.Reflection.AssemblyDescription("Battery level of a wireless mouse (Pulsar, Logitech) in the notification area (unofficial project)")]
[assembly: System.Reflection.AssemblyVersion("1.1.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.1.0.0")]

namespace MouseBatteryTray
{
    enum MouseState { NoDevice, Asleep, Ok, Error }
    enum IconMode { Normal, Charging, Dim, Empty }

    class Reading
    {
        public MouseState State = MouseState.NoDevice;
        public string Key = "";          // stable identifier, e.g. "pulsar:Pulsar X2 V2 Mini"
        public string Brand = "";        // "Pulsar", "Logitech"
        public string Name = "";         // display name read from the device
        public string Connection = "";   // "USB cable", "2.4 GHz wireless receiver", "Bluetooth"
        public int Percent;
        public bool Charging;
        public bool Full;                // plugged in and fully charged
        public int Millivolts;
        public bool VoltageBased;        // level estimated from the voltage: overestimated while charging
        public DateTime Time = DateTime.Now;
    }

    // ------------------------------------------------------------------
    //  Low-level HID access shared by all brands
    // ------------------------------------------------------------------
    static class Hid
    {
        [StructLayout(LayoutKind.Sequential)]
        struct SP_DEVICE_INTERFACE_DATA
        {
            public int cbSize;
            public Guid InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HIDP_CAPS
        {
            public ushort Usage;
            public ushort UsagePage;
            public ushort InputReportByteLength;
            public ushort OutputReportByteLength;
            public ushort FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
            public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes;
            public ushort NumberInputButtonCaps;
            public ushort NumberInputValueCaps;
            public ushort NumberInputDataIndices;
            public ushort NumberOutputButtonCaps;
            public ushort NumberOutputValueCaps;
            public ushort NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps;
            public ushort NumberFeatureValueCaps;
            public ushort NumberFeatureDataIndices;
        }

        [DllImport("hid.dll")]
        static extern void HidD_GetHidGuid(out Guid guid);
        [DllImport("hid.dll", SetLastError = true)]
        static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr data);
        [DllImport("hid.dll")]
        static extern bool HidD_FreePreparsedData(IntPtr data);
        [DllImport("hid.dll")]
        static extern int HidP_GetCaps(IntPtr data, out HIDP_CAPS caps);
        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_SetOutputReport(SafeFileHandle h, byte[] buf, int len);
        [DllImport("hid.dll", CharSet = CharSet.Unicode)]
        static extern bool HidD_GetProductString(SafeFileHandle h, StringBuilder buf, int len);

        [DllImport("setupapi.dll", SetLastError = true)]
        static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr enumerator, IntPtr hwnd, int flags);
        [DllImport("setupapi.dll", SetLastError = true)]
        static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr devInfo, ref Guid g, int index, ref SP_DEVICE_INTERFACE_DATA data);
        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "SetupDiGetDeviceInterfaceDetailW")]
        static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, int size, out int required, IntPtr devInfoData);
        [DllImport("setupapi.dll")]
        static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateFileW")]
        static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr template);

        // Lists the HID interfaces of a vendor. USB paths contain "vid_xxxx", Bluetooth paths "vid&0002xxxx" or "vid&02xxxx".
        public static List<string> FindPaths(string vendorId)
        {
            Regex vendor = new Regex(@"vid[_&](0002|02)?" + vendorId + "(?![0-9a-f])", RegexOptions.IgnoreCase);
            List<string> list = new List<string>();
            Guid g;
            HidD_GetHidGuid(out g);
            IntPtr set = SetupDiGetClassDevs(ref g, IntPtr.Zero, IntPtr.Zero, 0x12); // PRESENT | DEVICEINTERFACE
            if (set == new IntPtr(-1)) return list;
            try
            {
                for (int i = 0; ; i++)
                {
                    SP_DEVICE_INTERFACE_DATA d = new SP_DEVICE_INTERFACE_DATA();
                    d.cbSize = Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA));
                    if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref g, i, ref d)) break;
                    int req;
                    SetupDiGetDeviceInterfaceDetail(set, ref d, IntPtr.Zero, 0, out req, IntPtr.Zero);
                    if (req <= 0) continue;
                    IntPtr buf = Marshal.AllocHGlobal(req);
                    try
                    {
                        Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
                        if (SetupDiGetDeviceInterfaceDetail(set, ref d, buf, req, out req, IntPtr.Zero))
                        {
                            string path = Marshal.PtrToStringUni(IntPtr.Add(buf, 4));
                            if (path != null && vendor.IsMatch(path)) list.Add(path);
                        }
                    }
                    finally { Marshal.FreeHGlobal(buf); }
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
            return list;
        }

        public static SafeFileHandle Open(string path)
        {
            return CreateFile(path, 0xC0000000, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero);
        }

        public static int LastError() { return Marshal.GetLastWin32Error(); }

        public static bool GetCaps(SafeFileHandle h, out HIDP_CAPS caps)
        {
            caps = new HIDP_CAPS();
            IntPtr pp;
            if (!HidD_GetPreparsedData(h, out pp)) return false;
            try { HidP_GetCaps(pp, out caps); }
            finally { HidD_FreePreparsedData(pp); }
            return true;
        }

        public static string ProductString(SafeFileHandle h)
        {
            StringBuilder sb = new StringBuilder(128);
            try { if (HidD_GetProductString(h, sb, sb.Capacity * 2)) return sb.ToString().Trim(); }
            catch { }
            return "";
        }

        public static int ParsePid(string path)
        {
            Match m = Regex.Match(path, @"pid[_&]([0-9a-f]{4})", RegexOptions.IgnoreCase);
            int pid;
            return m.Success && int.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.HexNumber, null, out pid) ? pid : 0;
        }

        public static bool IsBluetooth(string path)
        {
            string p = path.ToLowerInvariant();
            return p.Contains("{00001124-0000-1000-8000-00805f9b34fb}") || p.Contains("{00001812-0000-1000-8000-00805f9b34fb}");
        }

        public static string Hex(byte[] b)
        {
            return BitConverter.ToString(b).Replace("-", " ");
        }
    }

    // One opened HID interface. A read that timed out stays pending and is reused by the next Read,
    // so a late reply is never lost and never mixed up with a later request.
    class HidChannel : IDisposable
    {
        readonly SafeFileHandle handle;
        readonly FileStream stream;
        readonly int inLen;
        IAsyncResult pending;
        byte[] pendingBuf;

        public HidChannel(SafeFileHandle h, int inputLength)
        {
            handle = h;
            inLen = inputLength;
            stream = new FileStream(h, FileAccess.ReadWrite, inLen, true);
        }

        // controlFirst: send through a SET_REPORT control transfer first (Pulsar), otherwise a normal write first (Logitech).
        public bool Write(byte[] data, bool controlFirst, StringBuilder log)
        {
            if (controlFirst && Hid.HidD_SetOutputReport(handle, data, data.Length)) return true;
            try
            {
                stream.Write(data, 0, data.Length);
                stream.Flush();
                return true;
            }
            catch (Exception e)
            {
                if (!controlFirst && Hid.HidD_SetOutputReport(handle, data, data.Length)) return true;
                if (log != null) log.AppendLine("    send failed (" + e.Message + ")");
                return false;
            }
        }

        public byte[] Read(int timeoutMs)
        {
            if (pending == null)
            {
                pendingBuf = new byte[inLen];
                pending = stream.BeginRead(pendingBuf, 0, inLen, null, null);
            }
            if (!pending.AsyncWaitHandle.WaitOne(Math.Max(0, timeoutMs))) return null;
            IAsyncResult done = pending;
            pending = null;
            int n = stream.EndRead(done);
            return n > 0 ? pendingBuf : null;
        }

        public void Dispose()
        {
            try { stream.Dispose(); } catch { }
        }
    }

    // ------------------------------------------------------------------
    //  Pulsar (X2 V2 Mini and mice using the same protocol)
    // ------------------------------------------------------------------
    static class PulsarDevices
    {
        const int PidWired = 0xF507;              // X2 V2 Mini plugged in over USB
        const byte ReportId = 0x08;
        const byte CmdStatus = 0x03;
        const byte CmdPower = 0x04;
        const int PacketLen = 17;

        static byte[] Build(byte cmd)
        {
            byte[] p = new byte[PacketLen];
            p[0] = ReportId;
            p[1] = cmd;
            int sum = 0;
            for (int i = 0; i < 16; i++) sum += p[i];
            p[16] = (byte)(0x55 - sum);
            return p;
        }

        static byte[] Transact(HidChannel ch, byte cmd, StringBuilder log)
        {
            byte[] req = Build(cmd);
            if (!ch.Write(req, true, log)) return null;
            if (log != null) log.AppendLine("    -> " + Hid.Hex(req));
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(1000);
            while (true)
            {
                int left = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
                byte[] buf = left > 0 ? ch.Read(left) : null;
                if (buf == null)
                {
                    if (log != null) log.AppendLine("    <- (no reply)");
                    return null;
                }
                if (log != null) log.AppendLine("    <- " + Hid.Hex(buf));
                if (buf[0] == ReportId && buf[1] == cmd) return buf;
            }
        }

        static string MakeName(string product)
        {
            if (product.Length == 0) return "Pulsar mouse";
            if (product.StartsWith("pulsar", StringComparison.OrdinalIgnoreCase)) return product;
            return "Pulsar " + product;
        }

        public static void Read(List<Reading> found, StringBuilder log)
        {
            List<string> paths = Hid.FindPaths("3554");
            if (log != null) log.AppendLine("Pulsar: " + paths.Count + " HID interface(s)");
            Reading best = null;
            foreach (string path in paths)
            {
                int pid = Hid.ParsePid(path);
                using (SafeFileHandle h = Hid.Open(path))
                {
                    if (h.IsInvalid) continue;
                    Hid.HIDP_CAPS caps;
                    if (!Hid.GetCaps(h, out caps)) continue;
                    if (caps.InputReportByteLength != PacketLen || caps.OutputReportByteLength != PacketLen) continue;
                    if (log != null) log.AppendLine("  " + path);
                    string name = MakeName(Hid.ProductString(h));

                    using (HidChannel ch = new HidChannel(h, PacketLen))
                    {
                        byte[] st = Transact(ch, CmdStatus, log);
                        if (st == null) continue;
                        Reading r = new Reading();
                        r.Brand = "Pulsar";
                        r.Name = name;
                        r.Key = "pulsar:" + name;
                        r.Connection = pid == PidWired ? "USB cable" : "2.4 GHz wireless receiver";
                        r.VoltageBased = true;
                        if (st[6] == 0)   // receiver present but mouse off / asleep
                        {
                            r.State = MouseState.Asleep;
                            if (best == null) best = r;
                            continue;
                        }

                        byte[] pw = Transact(ch, CmdPower, log);
                        if (pw == null || pw[6] > 100) continue;
                        r.State = MouseState.Ok;
                        r.Percent = pw[6];
                        r.Charging = pw[7] != 0;
                        r.Millivolts = (pw[8] << 8) | pw[9];
                        r.Full = r.Charging && r.Percent >= 100;
                        // If the mouse is also plugged in over USB, prefer that reading.
                        if (best == null || best.State != MouseState.Ok || pid == PidWired) best = r;
                    }
                }
            }
            if (best != null) found.Add(best);
        }
    }

    // ------------------------------------------------------------------
    //  Logitech (HID++ 2.0: Lightspeed, Unifying, Bolt receivers, USB cable, Bluetooth)
    // ------------------------------------------------------------------
    static class LogitechDevices
    {
        const byte LongReport = 0x11;
        const byte SoftwareId = 0x0B;
        const int ReplyTimeout = 500;
        const int LongLen = 20;

        // Sends a request and returns the matching reply, or null on error / timeout.
        static byte[] Request(HidChannel ch, byte dev, byte feature, int function, byte[] args, StringBuilder log)
        {
            byte[] req = new byte[LongLen];
            req[0] = LongReport;
            req[1] = dev;
            req[2] = feature;
            req[3] = (byte)((function << 4) | SoftwareId);
            if (args != null) Array.Copy(args, 0, req, 4, Math.Min(args.Length, 16));
            if (!ch.Write(req, false, log)) return null;
            if (log != null) log.AppendLine("    -> " + Hid.Hex(req));
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(ReplyTimeout);
            while (true)
            {
                int left = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
                byte[] r = left > 0 ? ch.Read(left) : null;
                if (r == null)
                {
                    if (log != null) log.AppendLine("    <- (no reply)");
                    return null;
                }
                if (log != null) log.AppendLine("    <- " + Hid.Hex(r));
                if (r[1] != dev) continue;
                bool error20 = r[0] == LongReport && r[2] == 0xFF && r[3] == feature && r[4] == req[3];
                bool error10 = r[2] == 0x8F && r[3] == feature && r[4] == req[3];
                if (error20 || error10) return null;
                if (r[0] == LongReport && r[2] == feature && r[3] == req[3]) return r;
            }
        }

        // HID++ protocol version of the device at this index (0 = no HID++ 2.0 device there).
        static int Ping(HidChannel ch, byte dev, StringBuilder log)
        {
            byte[] r = Request(ch, dev, 0x00, 1, new byte[] { 0, 0, 0x5A }, log);
            return r == null ? 0 : r[4];
        }

        static byte FeatureIndex(HidChannel ch, byte dev, int featureId, StringBuilder log)
        {
            byte[] r = Request(ch, dev, 0x00, 0, new byte[] { (byte)(featureId >> 8), (byte)featureId }, log);
            return r == null ? (byte)0 : r[4];
        }

        public static void Read(List<Reading> found, StringBuilder log)
        {
            List<string> paths = Hid.FindPaths("046d");
            if (log != null) log.AppendLine("Logitech: " + paths.Count + " HID interface(s)");
            foreach (string path in paths)
            {
                using (SafeFileHandle h = Hid.Open(path))
                {
                    if (h.IsInvalid) continue;
                    Hid.HIDP_CAPS caps;
                    if (!Hid.GetCaps(h, out caps)) continue;
                    // HID++ long-report collection: vendor usage page (FF00 or FF43), 20-byte reports.
                    if (caps.UsagePage < 0xFF00 || caps.InputReportByteLength != LongLen || caps.OutputReportByteLength != LongLen) continue;
                    if (log != null) log.AppendLine("  " + path + "  usage " + caps.UsagePage.ToString("X4") + ":" + caps.Usage.ToString("X4"));
                    string product = Hid.ProductString(h);

                    using (HidChannel ch = new HidChannel(h, LongLen))
                    {
                        // Device connected directly (USB cable or Bluetooth): it answers at index 0xFF.
                        if (Ping(ch, 0xFF, log) >= 2)
                        {
                            AddDevice(found, ch, 0xFF, Hid.IsBluetooth(path) ? "Bluetooth" : "USB cable", product, log);
                            continue;
                        }
                        // Otherwise it is a receiver: ask each pairing slot.
                        for (byte slot = 1; slot <= 6; slot++)
                            if (Ping(ch, slot, log) >= 2)
                                AddDevice(found, ch, slot, "2.4 GHz wireless receiver", "", log);
                    }
                }
            }
        }

        static void AddDevice(List<Reading> found, HidChannel ch, byte dev, string connection, string product, StringBuilder log)
        {
            string name = "";
            int type = -1;
            byte fName = FeatureIndex(ch, dev, 0x0005, log);
            if (fName != 0)
            {
                byte[] count = Request(ch, dev, fName, 0, null, log);
                if (count != null)
                {
                    int len = count[4];
                    List<byte> chars = new List<byte>();
                    while (chars.Count < len)
                    {
                        byte[] part = Request(ch, dev, fName, 1, new byte[] { (byte)chars.Count }, log);
                        if (part == null) break;
                        int before = chars.Count;
                        for (int i = 4; i < LongLen && chars.Count < len && part[i] != 0; i++) chars.Add(part[i]);
                        if (chars.Count == before) break;
                    }
                    name = Encoding.UTF8.GetString(chars.ToArray()).Trim();
                }
                byte[] t = Request(ch, dev, fName, 2, null, log);
                if (t != null) type = t[4];
            }

            // Device types: 3 mouse, 4 trackpad, 5 trackball. Keyboards, headsets etc. are ignored.
            if (type >= 0 && type != 3 && type != 4 && type != 5)
            {
                if (log != null) log.AppendLine("    skipped: \"" + name + "\" is not a mouse (type " + type + ")");
                return;
            }

            Reading r = ReadBattery(ch, dev, log);
            if (r == null)
            {
                if (log != null) log.AppendLine("    skipped: \"" + name + "\" has no readable battery");
                return;
            }
            if (name.Length == 0) name = product.Length > 0 ? product : "mouse";
            r.Brand = "Logitech";
            r.Name = name.StartsWith("Logitech", StringComparison.OrdinalIgnoreCase) ? name : "Logitech " + name;
            r.Key = "logitech:" + r.Name;
            r.Connection = connection;
            foreach (Reading other in found)
                if (other.Key == r.Key) return;   // same mouse seen twice (e.g. receiver + cable)
            found.Add(r);
        }

        static Reading ReadBattery(HidChannel ch, byte dev, StringBuilder log)
        {
            Reading r = new Reading();
            r.State = MouseState.Ok;

            // 0x1004 Unified Battery: real state of charge.
            byte f = FeatureIndex(ch, dev, 0x1004, log);
            if (f != 0)
            {
                byte[] cap = Request(ch, dev, f, 0, null, log);
                byte[] st = Request(ch, dev, f, 1, null, log);
                if (st != null)
                {
                    bool socSupported = cap == null || (cap[5] & 0x02) != 0;
                    r.Percent = socSupported && st[4] > 0 ? st[4] : LevelFromFlags(st[5]);
                    r.Charging = st[6] >= 1 && st[6] <= 3;
                    r.Full = st[6] == 3;
                    if (r.Full) r.Percent = 100;
                    return r;
                }
            }

            // 0x1000 Battery Level Status.
            f = FeatureIndex(ch, dev, 0x1000, log);
            if (f != 0)
            {
                byte[] st = Request(ch, dev, f, 0, null, log);
                if (st != null)
                {
                    r.Percent = Math.Min(100, (int)st[4]);
                    r.Charging = st[6] >= 1 && st[6] <= 4;
                    r.Full = st[6] == 3;
                    if (r.Full) r.Percent = 100;
                    return r;
                }
            }

            // 0x1001 Battery Voltage: only a voltage, the percentage is estimated from it.
            f = FeatureIndex(ch, dev, 0x1001, log);
            if (f != 0)
            {
                byte[] st = Request(ch, dev, f, 0, null, log);
                if (st != null)
                {
                    r.Millivolts = (st[4] << 8) | st[5];
                    r.Charging = (st[6] & 0x80) != 0;
                    r.Full = r.Charging && (st[6] & 0x03) == 1;
                    r.Percent = r.Full ? 100 : PercentFromVoltage(r.Millivolts);
                    r.VoltageBased = true;
                    return r;
                }
            }
            return null;
        }

        static int LevelFromFlags(int flags)
        {
            if ((flags & 0x08) != 0) return 100;   // full
            if ((flags & 0x04) != 0) return 50;    // good
            if ((flags & 0x02) != 0) return 20;    // low
            if ((flags & 0x01) != 0) return 5;     // critical
            return 0;
        }

        // Typical discharge curve of a single Li-ion cell (mV -> %), linearly interpolated.
        static readonly int[,] Curve = {
            { 4180, 100 }, { 4070, 90 }, { 3990, 80 }, { 3920, 70 }, { 3860, 60 }, { 3810, 50 },
            { 3780, 40 }, { 3750, 30 }, { 3720, 20 }, { 3670, 10 }, { 3650, 5 }, { 3580, 2 }, { 3500, 0 } };

        static int PercentFromVoltage(int mv)
        {
            if (mv >= Curve[0, 0]) return 100;
            for (int i = 1; i < Curve.GetLength(0); i++)
            {
                if (mv >= Curve[i, 0])
                {
                    double t = (double)(mv - Curve[i, 0]) / (Curve[i - 1, 0] - Curve[i, 0]);
                    return (int)Math.Round(Curve[i, 1] + t * (Curve[i - 1, 1] - Curve[i, 1]));
                }
            }
            return 0;
        }
    }

    static class Devices
    {
        public static List<Reading> ReadAll(StringBuilder log)
        {
            List<Reading> list = new List<Reading>();
            try { PulsarDevices.Read(list, log); }
            catch (Exception e) { if (log != null) log.AppendLine("Pulsar exception: " + e); }
            try { LogitechDevices.Read(list, log); }
            catch (Exception e) { if (log != null) log.AppendLine("Logitech exception: " + e); }
            DateTime now = DateTime.Now;
            foreach (Reading r in list) r.Time = now;
            return list;
        }
    }

    // What the app remembers about each mouse it has seen.
    class DeviceTrack
    {
        public string Key = "", Name = "", Brand = "";
        public Reading LastOk;        // last successful reading (time, voltage)
        public Reading LastTrusted;   // last trustworthy percentage (not charging, not settling)
        public bool WasCharging, LowAlerted;
        public DateTime SettleUntil = DateTime.MinValue;
    }

    // ------------------------------------------------------------------
    //  Appearance
    // ------------------------------------------------------------------
    static class Ui
    {
        public const string NoMouse = "No mouse detected";

        [DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr h);

        public static bool IsDark()
        {
            try
            {
                object v = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1);
                return v is int && (int)v == 0;
            }
            catch { return false; }
        }

        public static Color LevelColor(int percent, bool charging)
        {
            if (charging) return Color.FromArgb(31, 111, 235);
            if (percent >= 50) return Color.FromArgb(40, 160, 70);
            if (percent >= 20) return Color.FromArgb(214, 134, 0);
            return Color.FromArgb(214, 48, 49);
        }

        public static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            float d = radius * 2;
            GraphicsPath p = new GraphicsPath();
            if (d <= 0 || d > r.Width || d > r.Height) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static bool IsTaskbarDark()
        {
            try
            {
                object v = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme", 0);
                return !(v is int) || (int)v == 0;
            }
            catch { return true; }
        }

        public static readonly Color ChargeBlue = Color.FromArgb(31, 111, 235);

        // Lightning bolt fitted inside the given rectangle.
        public static PointF[] Bolt(RectangleF r)
        {
            float[] u = { 0.62f, 0f, 0.16f, 0.57f, 0.46f, 0.57f, 0.36f, 1f, 0.84f, 0.40f, 0.54f, 0.40f };
            PointF[] pts = new PointF[6];
            for (int i = 0; i < 6; i++) pts[i] = new PointF(r.X + u[2 * i] * r.Width, r.Y + u[2 * i + 1] * r.Height);
            return pts;
        }

        // Notification area icon: a battery whose fill follows the level.
        public static Icon MakeIcon(int percent, IconMode mode, int size, bool darkTaskbar)
        {
            using (Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = PixelOffsetMode.Half;   // crisp lines: x.5 = pixel centre
                    g.Clear(Color.Transparent);
                    float u = size / 16f;
                    float pw = Math.Max(1f, (float)Math.Round(1.2f * u));
                    Color line = darkTaskbar ? Color.White : Color.FromArgb(30, 30, 30);
                    if (mode == IconMode.Empty) line = Color.FromArgb(140, line);

                    RectangleF body = new RectangleF(pw / 2f, 3.5f * u, 13f * u, 9f * u);
                    using (Pen pen = new Pen(line, pw))
                    using (GraphicsPath path = RoundRect(body, 2f * u))
                        g.DrawPath(pen, path);
                    using (SolidBrush nb = new SolidBrush(line))
                    using (GraphicsPath nub = RoundRect(new RectangleF(body.Right + pw / 2f, 6f * u, 1.7f * u, 4f * u), 0.6f * u))
                        g.FillPath(nb, nub);

                    float inset = pw / 2f + u;
                    RectangleF inner = new RectangleF(body.X + inset, body.Y + inset, body.Width - 2 * inset, body.Height - 2 * inset);
                    if (mode == IconMode.Charging)
                    {
                        using (SolidBrush b = new SolidBrush(ChargeBlue)) g.FillRectangle(b, inner);
                        RectangleF br = new RectangleF(body.X + body.Width / 2f - 2.6f * u, body.Y + 0.6f * u, 5.2f * u, body.Height - 1.2f * u);
                        g.FillPolygon(Brushes.White, Bolt(br));
                    }
                    else if (mode != IconMode.Empty && percent > 0)
                    {
                        Color c = mode == IconMode.Dim ? Color.FromArgb(140, 140, 140) : LevelColor(percent, false);
                        float fw = Math.Max(1.5f * u, inner.Width * Math.Min(100, percent) / 100f);
                        using (SolidBrush b = new SolidBrush(c)) g.FillRectangle(b, inner.X, inner.Y, fw, inner.Height);
                    }
                }
                IntPtr h = bmp.GetHicon();
                try
                {
                    using (Icon tmp = Icon.FromHandle(h)) return (Icon)tmp.Clone();
                }
                finally { DestroyIcon(h); }
            }
        }
    }

    // ------------------------------------------------------------------
    //  Details window (left click on the icon)
    // ------------------------------------------------------------------
    class DetailsForm : Form
    {
        public Reading Current = new Reading();
        public DeviceTrack Track;     // history of the displayed mouse (may be null)
        public DateTime HiddenAt = DateTime.MinValue;

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll")]
        static extern bool GetWindowRect(IntPtr h, out RECT r);

        public DetailsForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            KeyPreview = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

            // Small discreet "i" in the top-right corner: explains why the level is off while charging.
            info.AutoSize = false;
            info.Cursor = Cursors.Help;
            info.Paint += delegate(object sender, PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                Color c = Ui.IsDark() ? Color.FromArgb(150, 150, 150) : Color.FromArgb(110, 110, 110);
                using (Pen p = new Pen(c, Math.Max(1f, g.DpiX / 96f)))
                    g.DrawEllipse(p, 1.5f, 1.5f, info.Width - 4f, info.Height - 4f);
                using (SolidBrush b = new SolidBrush(c))
                using (Font f = new Font("Segoe UI Semibold", 7.5f))
                using (StringFormat sf = new StringFormat())
                {
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;
                    g.DrawString("i", f, b, new RectangleF(0.5f, 0, info.Width, info.Height), sf);
                }
            };
            Controls.Add(info);
            // Clicking the "i" replaces the window content with the explanation; another click closes it.
            info.Click += delegate { showInfo = !showInfo; Relayout(); };
        }

        string Brand
        {
            get
            {
                if (Current.Brand.Length > 0) return Current.Brand;
                return Track != null ? Track.Brand : "";
            }
        }

        string Title
        {
            get
            {
                if (Current.Name.Length > 0) return Current.Name;
                return Track != null ? Track.Name : Ui.NoMouse;
            }
        }

        // The explanation only applies to mice whose level is estimated from the voltage.
        bool VoltageBased
        {
            get
            {
                if (Current.State == MouseState.Ok) return Current.VoltageBased;
                return Track != null && Track.LastOk != null && Track.LastOk.VoltageBased;
            }
        }

        string InfoTitle
        {
            get { return Brand == "Pulsar" ? "Why ~95% as soon as it's plugged in?" : "Why is the level hidden while charging?"; }
        }

        string InfoText
        {
            get
            {
                string mouse = Brand.Length > 0 ? "your " + Brand + " mouse" : "the mouse";
                return "This comes from " + mouse + ", not from this app.\n\n" +
                       "The mouse estimates its battery from the voltage. When plugged in, the voltage rises " +
                       "and the reported level is overestimated.\n\n" +
                       "Once unplugged, it settles back to the real level within a few minutes. " +
                       "So the percentage is hidden while charging and for 5 min afterwards.";
            }
        }

        readonly Label info = new Label();
        bool showInfo;

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (showInfo) { showInfo = false; Relayout(); }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { int round = 2; DwmSetWindowAttribute(Handle, 33, ref round, 4); } catch { }
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            HiddenAt = DateTime.UtcNow;
            Hide();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { HiddenAt = DateTime.UtcNow; Hide(); }
            base.OnKeyDown(e);
        }

        public void ShowNearTray()
        {
            showInfo = false;
            Relayout();
            Show();
            Activate();
            Invalidate();
        }

        // Size and position: the window has one extra row while charging (level).
        public void Relayout()
        {
            float s;
            using (Graphics g = CreateGraphics()) s = g.DpiX / 96f;
            if (!VoltageBased) showInfo = false;
            bool tall = showInfo || (Current.State == MouseState.Ok && Current.Charging);
            Size = new Size((int)(310 * s), (int)((tall ? 262 : 237) * s));
            int isz = (int)(18 * s);
            info.SetBounds(Width - (int)(20 * s) - isz, (int)(14 * s), isz, isz);
            info.BackColor = Ui.IsDark() ? Color.FromArgb(32, 32, 32) : Color.FromArgb(249, 249, 249);
            info.Visible = VoltageBased;
            Screen screen = Visible ? Screen.FromControl(this) : Screen.FromPoint(Cursor.Position);
            Rectangle wa = screen.WorkingArea;
            int right = wa.Right, bottom = wa.Bottom;

            // The working area does not always exclude the taskbar (auto-hide, etc.):
            // read its real size so the window never ends up underneath it.
            IntPtr bar = FindWindow("Shell_TrayWnd", null);
            RECT tr;
            if (bar != IntPtr.Zero && GetWindowRect(bar, out tr))
            {
                Rectangle b = screen.Bounds;
                int tw = tr.Right - tr.Left, th = tr.Bottom - tr.Top;
                bool sameScreen = tr.Left < b.Right && tr.Right > b.Left;
                if (sameScreen && tw >= th && tr.Top + th / 2 > b.Top + b.Height / 2)
                    bottom = Math.Min(bottom, b.Bottom - th);          // taskbar at the bottom
                else if (sameScreen && th > tw && tr.Left + tw / 2 > b.Left + b.Width / 2)
                    right = Math.Min(right, b.Right - tw);             // taskbar on the right
            }

            int m = (int)(12 * s);
            Location = new Point(right - Width - m, bottom - Height - m);
            Invalidate();
        }

        public void ShowInfoPreview() { showInfo = true; }
        public void PaintTo(Graphics g) { OnPaint(new PaintEventArgs(g, ClientRectangle)); }

        bool Settling
        {
            get { return Current.State == MouseState.Ok && !Current.Charging && Track != null && Current.Time < Track.SettleUntil; }
        }

        string StateText(Reading r)
        {
            switch (r.State)
            {
                case MouseState.Ok:
                    if (r.Charging) return r.Full ? "Charged (plugged in)" : "Charging";
                    if (Settling) return "Settling after charge";
                    return "On battery";
                case MouseState.Asleep: return "Asleep or off";
                case MouseState.Error: return "Read error";
                default: return Track != null ? "Not detected" : "No mouse found";
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            float s = g.DpiX / 96f;
            bool dark = Ui.IsDark();
            Color bg = dark ? Color.FromArgb(32, 32, 32) : Color.FromArgb(249, 249, 249);
            Color fg = dark ? Color.FromArgb(242, 242, 242) : Color.FromArgb(26, 26, 26);
            Color sub = dark ? Color.FromArgb(160, 160, 160) : Color.FromArgb(96, 96, 96);
            Color track = dark ? Color.FromArgb(62, 62, 62) : Color.FromArgb(222, 222, 222);
            Color border = dark ? Color.FromArgb(70, 70, 70) : Color.FromArgb(200, 200, 200);

            g.Clear(bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            using (Pen p = new Pen(border)) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);

            bool ok = Current.State == MouseState.Ok;
            bool charging = ok && Current.Charging;
            bool settling = Settling;
            bool live = ok && !charging && !settling;                       // percentage is trustworthy and current
            Reading lastTrusted = Track != null ? Track.LastTrusted : null;
            Reading lastOk = Track != null ? Track.LastOk : null;
            Reading shown = live ? Current : lastTrusted;                   // source of the displayed percentage
            Reading volt = ok ? Current : lastOk;
            float pad = 20 * s;
            float w = ClientSize.Width - 2 * pad;

            using (SolidBrush bFg = new SolidBrush(fg))
            using (SolidBrush bSub = new SolidBrush(sub))
            using (Font fTitle = new Font("Segoe UI Semibold", 10.5f))
            using (Font fBig = new Font("Segoe UI Light", 32f))
            using (Font fRow = new Font("Segoe UI", 9.5f))
            using (StringFormat right = new StringFormat())
            using (StringFormat titleFmt = new StringFormat())
            {
                right.Alignment = StringAlignment.Far;
                right.FormatFlags |= StringFormatFlags.NoWrap;
                titleFmt.FormatFlags |= StringFormatFlags.NoWrap;
                titleFmt.Trimming = StringTrimming.EllipsisCharacter;

                g.DrawString(Title, fTitle, bFg, new RectangleF(pad - 2 * s, 14 * s, w - 22 * s, 22 * s), titleFmt);

                if (showInfo)
                {
                    using (Font fHead = new Font("Segoe UI Semibold", 9.5f))
                    using (Font fBody = new Font("Segoe UI", 9f))
                    {
                        RectangleF head = new RectangleF(pad - 2 * s, 46 * s, w + 4 * s, 40 * s);
                        g.DrawString(InfoTitle, fHead, bFg, head);
                        float hh = g.MeasureString(InfoTitle, fHead, (int)head.Width).Height;
                        g.DrawString(InfoText, fBody, bSub, new RectangleF(pad - 2 * s, 46 * s + hh + 8 * s, w + 4 * s, ClientSize.Height - 60 * s - hh));
                    }
                    return;
                }

                RectangleF bar = new RectangleF(pad, 100 * s, w, 8 * s);
                using (GraphicsPath pt = Ui.RoundRect(bar, 4 * s))
                using (SolidBrush bt = new SolidBrush(track))
                    g.FillPath(bt, pt);

                if (charging)
                {
                    // Charging: show a bolt instead of the percentage (overestimated on voltage-based mice).
                    bool full = Current.Full;
                    using (SolidBrush bb = new SolidBrush(full ? Ui.LevelColor(100, false) : Ui.ChargeBlue))
                    {
                        g.FillPolygon(bb, Ui.Bolt(new RectangleF(pad, 44 * s, 24 * s, 40 * s)));
                        float fw = Current.VoltageBased || full ? w : Math.Max(8 * s, w * Current.Percent / 100f);
                        using (GraphicsPath pf = Ui.RoundRect(new RectangleF(pad, 100 * s, fw, 8 * s), 4 * s)) g.FillPath(bb, pf);
                    }
                    using (Font fMid = new Font("Segoe UI Light", 22f))
                        g.DrawString(full ? "Charged" : "Charging", fMid, bFg, pad + 28 * s, 44 * s);
                }
                else if (settling)
                {
                    using (Font fMid = new Font("Segoe UI Light", 22f))
                        g.DrawString("Settling…", fMid, bSub, pad - 6 * s, 44 * s);
                    int min = Math.Max(1, (int)Math.Ceiling((Track.SettleUntil - DateTime.Now).TotalMinutes));
                    g.DrawString("~" + min + " min left", fRow, bSub, new RectangleF(pad, 64 * s, w, 20 * s), right);
                }
                else
                {
                    string big = shown != null ? shown.Percent + "%" : "—%";
                    g.DrawString(big, fBig, live ? bFg : bSub, pad - 8 * s, 34 * s);
                    string side = live ? StateText(Current) : (shown != null ? "last known value" : "");
                    g.DrawString(side, fRow, bSub, new RectangleF(pad, 64 * s, w, 20 * s), right);
                    if (shown != null && shown.Percent > 0)
                    {
                        float fw = Math.Max(8 * s, w * shown.Percent / 100f);
                        Color c = live ? Ui.LevelColor(shown.Percent, false) : sub;
                        using (GraphicsPath pf = Ui.RoundRect(new RectangleF(pad, 100 * s, fw, 8 * s), 4 * s))
                        using (SolidBrush bf = new SolidBrush(c))
                            g.FillPath(bf, pf);
                    }
                }

                string level = (Current.VoltageBased && !Current.Full ? "≈ " : "") + Current.Percent + "%";
                string connection = Current.Connection.Length > 0 ? Current.Connection
                                  : (lastOk != null && Current.State != MouseState.NoDevice ? lastOk.Connection : "—");
                string[][] rows = new string[][] {
                    new string[] { "Status", StateText(Current) },
                    new string[] { "Voltage", volt != null && volt.Millivolts > 0 ? (volt.Millivolts / 1000.0).ToString("0.00") + " V" : "—" },
                    new string[] { "Connection", connection },
                    // Row shown only while charging: level reported by the mouse.
                    new string[] { charging ? (Current.VoltageBased ? "Reported level" : "Level") : null, level },
                    new string[] { "Last reading", lastOk != null ? lastOk.Time.ToString("HH:mm:ss") : "—" },
                };
                float y = 124 * s;
                foreach (string[] row in rows)
                {
                    if (row[0] == null) continue;
                    g.DrawString(row[0], fRow, bSub, pad - 2 * s, y);
                    g.DrawString(row[1], fRow, bFg, new RectangleF(pad, y, w + 2 * s, 22 * s), right);
                    y += 25 * s;
                }
            }
        }
    }

    // ------------------------------------------------------------------
    //  Notification area icon
    // ------------------------------------------------------------------
    class TrayContext : ApplicationContext
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunName = "MouseBatteryTray";
        const string SettingsKey = @"Software\MouseBatteryTray";
        const int LowLevel = 15;
        const int SettleMinutes = 5;

        readonly NotifyIcon tray = new NotifyIcon();
        readonly DetailsForm details = new DetailsForm();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        readonly TaskScheduler ui;
        readonly ToolStripMenuItem itemHeader, itemDevices, itemStartup, itemAlert;

        readonly List<DeviceTrack> tracks = new List<DeviceTrack>();
        List<Reading> lastReadings = new List<Reading>();
        string selectedKey;          // "" = automatic
        string autoKey = "";         // mouse shown last in automatic mode
        bool busy;
        Icon currentIcon;

        public TrayContext()
        {
            if (SynchronizationContext.Current == null)
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            ui = TaskScheduler.FromCurrentSynchronizationContext();
            selectedKey = GetSetting("SelectedDevice", "");

            ContextMenuStrip menu = new ContextMenuStrip();
            itemHeader = new ToolStripMenuItem(Ui.NoMouse);
            itemHeader.Enabled = false;
            itemDevices = new ToolStripMenuItem("Mouse to display");
            itemStartup = new ToolStripMenuItem("Start with Windows");
            itemStartup.Click += delegate { ToggleStartup(); };
            itemAlert = new ToolStripMenuItem("Low battery alert (" + LowLevel + "%)");
            itemAlert.Click += delegate { SetAlertEnabled(!AlertEnabled()); };
            menu.Items.Add(itemHeader);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Show details", null, delegate { ShowDetails(); });
            menu.Items.Add("Refresh", null, delegate { RefreshNow(); });
            menu.Items.Add(itemDevices);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(itemStartup);
            menu.Items.Add(itemAlert);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, delegate { ExitThread(); });
            menu.Opening += delegate
            {
                itemStartup.Checked = StartupEnabled();
                itemAlert.Checked = AlertEnabled();
                FillDeviceMenu();
            };

            tray.ContextMenuStrip = menu;
            tray.MouseClick += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left) return;
                if (details.Visible) { details.Hide(); return; }
                // Clicking the icon first closes the window (focus loss): do not reopen it right away.
                if ((DateTime.UtcNow - details.HiddenAt).TotalMilliseconds < 300) return;
                ShowDetails();
            };
            Apply(new List<Reading>());
            tray.Visible = true;

            timer.Interval = 60000;
            timer.Tick += delegate { RefreshNow(); };
            timer.Start();
            RefreshNow();
        }

        // "Mouse to display" submenu: automatic, or one of the mice seen since the app started.
        void FillDeviceMenu()
        {
            itemDevices.DropDownItems.Clear();
            ToolStripMenuItem auto = new ToolStripMenuItem("Automatic");
            auto.Checked = selectedKey.Length == 0;
            auto.Click += delegate { Select(""); };
            itemDevices.DropDownItems.Add(auto);
            if (tracks.Count > 0) itemDevices.DropDownItems.Add(new ToolStripSeparator());
            foreach (DeviceTrack t in tracks)
            {
                string key = t.Key;
                ToolStripMenuItem it = new ToolStripMenuItem(t.Name);
                it.Checked = selectedKey == key;
                it.Click += delegate { Select(key); };
                itemDevices.DropDownItems.Add(it);
            }
            itemDevices.Visible = tracks.Count > 1 || selectedKey.Length > 0;
        }

        void Select(string key)
        {
            selectedKey = key;
            SetSetting("SelectedDevice", key);
            Apply(lastReadings);
        }

        void ShowDetails()
        {
            details.ShowNearTray();
            RefreshNow();
        }

        void RefreshNow()
        {
            if (busy) return;
            busy = true;
            Task.Factory.StartNew<List<Reading>>(delegate { return Devices.ReadAll(null); })
                .ContinueWith(delegate(Task<List<Reading>> t)
                {
                    busy = false;
                    Apply(t.IsFaulted ? new List<Reading>() : t.Result);
                }, ui);
        }

        DeviceTrack FindTrack(string key)
        {
            foreach (DeviceTrack t in tracks) if (t.Key == key) return t;
            return null;
        }

        bool IsSettling(DeviceTrack t, Reading r)
        {
            return t != null && r.State == MouseState.Ok && !r.Charging && r.Time < t.SettleUntil;
        }

        // Updates the history of one mouse and raises its low battery alert.
        void Update(Reading r)
        {
            DeviceTrack t = FindTrack(r.Key);
            if (t == null)
            {
                t = new DeviceTrack();
                t.Key = r.Key;
                tracks.Add(t);
            }
            t.Name = r.Name;
            t.Brand = r.Brand;
            if (r.State != MouseState.Ok) return;

            // While charging, voltage-based mice overestimate their level; after unplugging the value takes
            // a few minutes to settle. The percentage is not shown during these two phases.
            t.LastOk = r;
            if (r.Charging) t.WasCharging = true;
            else
            {
                if (t.WasCharging)
                {
                    t.WasCharging = false;
                    if (r.VoltageBased) t.SettleUntil = r.Time.AddMinutes(SettleMinutes);
                }
                if (!IsSettling(t, r)) t.LastTrusted = r;
            }

            if (IsSettling(t, r)) return;
            if (r.Charging || r.Percent > LowLevel + 5) t.LowAlerted = false;
            else if (r.Percent <= LowLevel && !t.LowAlerted && AlertEnabled())
            {
                t.LowAlerted = true;
                tray.ShowBalloonTip(8000, "Mouse battery low",
                    r.Name + ": " + r.Percent + "% left. Time to plug it in.", ToolTipIcon.Warning);
            }
        }

        // Chooses the mouse shown by the icon: the selected one, or in automatic mode the one shown before
        // if it is still there, otherwise the first mouse that answers.
        Reading Pick(List<Reading> readings, out DeviceTrack track)
        {
            string key = selectedKey.Length > 0 ? selectedKey : autoKey;
            Reading pick = null;
            foreach (Reading r in readings)
                if (r.Key == key && (selectedKey.Length > 0 || r.State == MouseState.Ok)) pick = r;
            if (pick == null && selectedKey.Length == 0)
            {
                foreach (Reading r in readings) if (pick == null && r.State == MouseState.Ok) pick = r;
                if (pick == null && readings.Count > 0) pick = readings[0];
            }
            if (pick != null)
            {
                if (selectedKey.Length == 0) autoKey = pick.Key;
                track = FindTrack(pick.Key);
                return pick;
            }
            // The mouse is gone: keep showing it as not detected, with its last known level.
            track = FindTrack(key);
            Reading none = new Reading();
            if (track != null)
            {
                none.Key = track.Key;
                none.Name = track.Name;
                none.Brand = track.Brand;
            }
            return none;
        }

        void Apply(List<Reading> readings)
        {
            lastReadings = readings;
            foreach (Reading r in readings) Update(r);

            DeviceTrack t;
            Reading r0 = Pick(readings, out t);
            bool settling = IsSettling(t, r0);
            string name = r0.Name.Length > 0 ? r0.Name : Ui.NoMouse;
            Reading lastTrusted = t != null ? t.LastTrusted : null;

            string tip;
            int level = 0;
            IconMode mode;
            if (r0.State == MouseState.Ok && r0.Charging && r0.Full)
            {
                mode = IconMode.Normal;
                level = 100;
                tip = name + ": charged (100%)";
            }
            else if (r0.State == MouseState.Ok && r0.Charging)
            {
                mode = IconMode.Charging;
                tip = name + ": charging" + (r0.VoltageBased ? "" : " (" + r0.Percent + "%)");
            }
            else if (settling)
            {
                mode = IconMode.Dim;
                level = r0.Percent;
                tip = name + ": settling after charge";
            }
            else if (r0.State == MouseState.Ok)
            {
                mode = IconMode.Normal;
                level = r0.Percent;
                tip = name + ": " + r0.Percent + "%";
            }
            else if (lastTrusted != null)
            {
                mode = IconMode.Dim;
                level = lastTrusted.Percent;
                tip = name + ": " + (r0.State == MouseState.Asleep ? "asleep" : "not detected") + " (" + lastTrusted.Percent + "%)";
            }
            else
            {
                mode = IconMode.Empty;
                tip = r0.State == MouseState.Asleep ? name + ": asleep or off"
                    : t != null ? name + ": not detected"
                    : Ui.NoMouse;
            }

            Icon old = currentIcon;
            currentIcon = Ui.MakeIcon(level, mode, SystemInformation.SmallIconSize.Width, Ui.IsTaskbarDark());
            tray.Icon = currentIcon;
            if (old != null) old.Dispose();
            tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
            itemHeader.Text = tip;

            details.Current = r0;
            details.Track = t;
            if (details.Visible) details.Relayout();
        }

        static bool StartupEnabled()
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey))
                return k != null && k.GetValue(RunName) != null;
        }

        void ToggleStartup()
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (k.GetValue(RunName) != null) k.DeleteValue(RunName, false);
                else k.SetValue(RunName, "\"" + Application.ExecutablePath + "\"");
            }
        }

        // Portable mode: if a portable.ini file sits next to the exe, settings are stored there
        // and nothing is written to the registry (unless the user enables start with Windows).
        static string PortableFile
        {
            get { return Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "portable.ini"); }
        }

        static string GetSetting(string name, string def)
        {
            try
            {
                if (File.Exists(PortableFile))
                {
                    foreach (string line in File.ReadAllLines(PortableFile))
                    {
                        int eq = line.IndexOf('=');
                        if (eq > 0 && string.Equals(line.Substring(0, eq).Trim(), name, StringComparison.OrdinalIgnoreCase))
                            return line.Substring(eq + 1).Trim();
                    }
                    return def;
                }
                object v = Registry.GetValue(@"HKEY_CURRENT_USER\" + SettingsKey, name, null);
                return v == null ? def : v.ToString();
            }
            catch { return def; }
        }

        static void SetSetting(string name, string value)
        {
            try
            {
                if (File.Exists(PortableFile))
                {
                    List<string> lines = new List<string>(File.ReadAllLines(PortableFile));
                    bool found = false;
                    for (int i = 0; i < lines.Count; i++)
                    {
                        int eq = lines[i].IndexOf('=');
                        if (eq > 0 && string.Equals(lines[i].Substring(0, eq).Trim(), name, StringComparison.OrdinalIgnoreCase))
                        {
                            lines[i] = name + "=" + value;
                            found = true;
                        }
                    }
                    if (!found) lines.Add(name + "=" + value);
                    File.WriteAllLines(PortableFile, lines.ToArray());
                }
                else Registry.SetValue(@"HKEY_CURRENT_USER\" + SettingsKey, name, value, RegistryValueKind.String);
            }
            catch { }
        }

        static bool AlertEnabled() { return GetSetting("LowBatteryAlert", "1") != "0"; }
        static void SetAlertEnabled(bool on) { SetSetting("LowBatteryAlert", on ? "1" : "0"); }

        protected override void ExitThreadCore()
        {
            timer.Stop();
            tray.Visible = false;
            tray.Dispose();
            base.ExitThreadCore();
        }
    }

    static class Program
    {
        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        [STAThread]
        static int Main(string[] args)
        {
            // Diagnostic mode: MouseBatteryTray.exe --once [file] writes a detailed reading, then exits.
            if (args.Length > 0 && args[0] == "--once")
            {
                StringBuilder log = new StringBuilder();
                List<Reading> list = Devices.ReadAll(log);
                log.AppendLine();
                log.AppendLine(list.Count + " mouse/mice found");
                foreach (Reading r in list)
                    log.AppendLine("Result: " + r.Name + " | " + r.State + " | " + r.Percent + "% | charging=" + r.Charging +
                                   " | full=" + r.Full + " | " + r.Millivolts + " mV | " + r.Connection + (r.VoltageBased ? " | voltage-based" : ""));
                if (args.Length > 1) File.WriteAllText(args[1], log.ToString(), Encoding.UTF8);
                else Console.Write(log.ToString());
                return list.Count > 0 ? 0 : 1;
            }

            // Preview mode: MouseBatteryTray.exe --preview file.png draws the icons and the window into an image.
            if (args.Length > 1 && args[0] == "--preview")
            {
                using (Bitmap sheet = new Bitmap(760, 560))
                {
                    using (Graphics g = Graphics.FromImage(sheet))
                    {
                        g.InterpolationMode = InterpolationMode.NearestNeighbor;
                        g.PixelOffsetMode = PixelOffsetMode.Half;
                        for (int row = 0; row < 2; row++)
                        {
                            bool dark = row == 0;
                            using (SolidBrush b = new SolidBrush(dark ? Color.FromArgb(28, 28, 28) : Color.FromArgb(238, 238, 238)))
                                g.FillRectangle(b, 0, row * 130, 760, 130);
                            int x = 10;
                            int[] levels = { 100, 60, 30, 8, 0, 70, 0 };
                            IconMode[] modes = { IconMode.Normal, IconMode.Normal, IconMode.Normal, IconMode.Normal, IconMode.Charging, IconMode.Dim, IconMode.Empty };
                            for (int i = 0; i < levels.Length; i++)
                            {
                                using (Icon ic = Ui.MakeIcon(levels[i], modes[i], 16, dark))
                                using (Bitmap bm = ic.ToBitmap())
                                {
                                    g.DrawImage(bm, new Rectangle(x, row * 130 + 10, 64, 64));
                                    g.DrawImage(bm, new Rectangle(x + 70, row * 130 + 80, 16, 16));
                                }
                                using (Icon ic = Ui.MakeIcon(levels[i], modes[i], 24, dark))
                                using (Bitmap bm = ic.ToBitmap())
                                    g.DrawImage(bm, new Rectangle(x + 10, row * 130 + 80, 24, 24));
                                x += 105;
                            }
                        }
                        Reading[] states = new Reading[2];
                        states[0] = new Reading(); states[0].State = MouseState.Ok; states[0].Percent = 95; states[0].Charging = true; states[0].Millivolts = 4059;
                        states[0].Brand = "Pulsar"; states[0].Name = "Pulsar X2 V2 Mini"; states[0].Connection = "USB cable"; states[0].VoltageBased = true;
                        states[1] = new Reading(); states[1].State = MouseState.Ok; states[1].Percent = 72;
                        states[1].Brand = "Logitech"; states[1].Name = "Logitech PRO X SUPERLIGHT"; states[1].Connection = "2.4 GHz wireless receiver";
                        for (int i = 0; i < 2; i++)
                        {
                            using (DetailsForm f = new DetailsForm())
                            using (Bitmap bm = new Bitmap(310, 262))
                            {
                                f.ClientSize = new Size(310, 262);
                                f.Current = states[i];
                                DeviceTrack t = new DeviceTrack();
                                t.LastOk = states[i];
                                t.LastTrusted = states[i];
                                f.Track = t;
                                using (Graphics fg = Graphics.FromImage(bm)) f.PaintTo(fg);
                                g.DrawImage(bm, 10 + i * 330, 280);
                            }
                        }
                    }
                    sheet.Save(args[1], ImageFormat.Png);
                }
                return 0;
            }

            bool created;
            using (Mutex mutex = new Mutex(true, "MouseBatteryTray_SingleInstance", out created))
            {
                if (!created) return 0;
                try { SetProcessDPIAware(); } catch { }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayContext());
            }
            return 0;
        }
    }
}
