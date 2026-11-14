// Mouse Battery Tray - shows the battery level of a wireless mouse (Pulsar, Logitech) in the Windows notification area.
// Pulsar protocol taken from the open-source project python-pulsar-mouse-tool (andrewrabert, MIT license).
// Logitech support uses the HID++ 2.0 protocol (device name 0x0005, battery features 0x1004, 0x1000, 0x1001).
// Build: see build.ps1 (uses the csc.exe that ships with Windows, no dependencies).
// This file holds the device detection and the Pulsar / HID++ protocols. The interface lives in the other files of src\.

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
[assembly: System.Reflection.AssemblyVersion("1.3.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.3.0.0")]

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

}
