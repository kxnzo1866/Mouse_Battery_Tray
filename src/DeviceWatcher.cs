using System;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace MouseBatteryTray
{
    // ------------------------------------------------------------------
    //  Tells the app when a Pulsar or Logitech HID interface appears or disappears
    //  (receiver plugged in or out, cable, Bluetooth connection), through WM_DEVICECHANGE.
    // ------------------------------------------------------------------
    class DeviceWatcher : NativeWindow, IDisposable
    {
        const int WM_DEVICECHANGE = 0x0219;
        const int DBT_DEVICEARRIVAL = 0x8000;
        const int DBT_DEVICEREMOVECOMPLETE = 0x8004;
        const int DBT_DEVTYP_DEVICEINTERFACE = 5;
        static readonly Guid HidInterface = new Guid("4d1e55b2-f16f-11cf-88cb-001111000030");
        static readonly Regex Vendors = new Regex(@"vid[_&](0002|02)?(3554|046d)(?![0-9a-f])", RegexOptions.IgnoreCase);

        [StructLayout(LayoutKind.Sequential)]
        struct DEV_BROADCAST_DEVICEINTERFACE
        {
            public int Size;
            public int DeviceType;
            public int Reserved;
            public Guid ClassGuid;
            public short Name;
        }

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr RegisterDeviceNotification(IntPtr recipient, IntPtr filter, int flags);
        [DllImport("user32.dll")]
        static extern bool UnregisterDeviceNotification(IntPtr handle);

        IntPtr notification;

        // path of the interface, true = arrived / false = removed
        public event Action<string, bool> Changed;

        public DeviceWatcher()
        {
            CreateHandle(new CreateParams());   // hidden window that only receives messages
            DEV_BROADCAST_DEVICEINTERFACE filter = new DEV_BROADCAST_DEVICEINTERFACE();
            filter.Size = Marshal.SizeOf(typeof(DEV_BROADCAST_DEVICEINTERFACE));
            filter.DeviceType = DBT_DEVTYP_DEVICEINTERFACE;
            filter.ClassGuid = HidInterface;
            IntPtr buf = Marshal.AllocHGlobal(filter.Size);
            try
            {
                Marshal.StructureToPtr(filter, buf, false);
                notification = RegisterDeviceNotification(Handle, buf, 0);   // DEVICE_NOTIFY_WINDOW_HANDLE
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        public static bool IsMouseVendor(string path)
        {
            return path != null && Vendors.IsMatch(path);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_DEVICECHANGE && m.LParam != IntPtr.Zero)
            {
                int ev = m.WParam.ToInt32();
                if ((ev == DBT_DEVICEARRIVAL || ev == DBT_DEVICEREMOVECOMPLETE) &&
                    Marshal.ReadInt32(m.LParam, 4) == DBT_DEVTYP_DEVICEINTERFACE)
                {
                    // dbcc_name follows size, devicetype, reserved and the class GUID.
                    string path = Marshal.PtrToStringUni(IntPtr.Add(m.LParam, 28));
                    if (IsMouseVendor(path) && Changed != null) Changed(path, ev == DBT_DEVICEARRIVAL);
                }
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            if (notification != IntPtr.Zero) { UnregisterDeviceNotification(notification); notification = IntPtr.Zero; }
            DestroyHandle();
        }
    }

    // ------------------------------------------------------------------
    //  Time until the next reading. The base is the interval chosen in the settings.
    // ------------------------------------------------------------------
    static class Pacing
    {
        public const int ChargingSeconds = 30;
        public const int MaxStretchedSeconds = 900;   // 15 min

        // stableReads = number of consecutive readings in which nothing changed.
        public static int NextDelay(int baseSeconds, bool adaptive, bool allOk, bool charging, bool settling, bool low, int stableReads)
        {
            if (charging) return Math.Min(baseSeconds, ChargingSeconds);        // follow the charge closely
            if (!adaptive || !allOk || settling || low) return baseSeconds;      // asleep, unplugged, settling, low: no stretching
            int factor = stableReads >= 6 ? 4 : stableReads >= 3 ? 2 : 1;
            return Math.Min(baseSeconds * factor, Math.Max(baseSeconds, MaxStretchedSeconds));
        }
    }
}
