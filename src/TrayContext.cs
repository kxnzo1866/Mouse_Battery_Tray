using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MouseBatteryTray
{
    // ------------------------------------------------------------------
    //  Notification area icon
    // ------------------------------------------------------------------
    class TrayContext : ApplicationContext, IAppHost
    {
        const int SettleMinutes = 5;

        readonly NotifyIcon tray = new NotifyIcon();
        readonly ContextMenuStrip menu = new ContextMenuStrip();
        readonly DetailsForm details = new DetailsForm();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        readonly TaskScheduler ui;
        readonly ToolStripMenuItem itemHeader, itemDetails, itemRefresh, itemDevices, itemSettings, itemStartup, itemAlert, itemExit;
        SettingsForm settings;

        readonly List<DeviceTrack> tracks = new List<DeviceTrack>();
        List<Reading> lastReadings = new List<Reading>();
        string selectedKey;          // "" = automatic
        string autoKey = "";         // mouse shown last in automatic mode
        bool busy;
        DateTime lastRefresh = DateTime.MinValue;
        Icon currentIcon;

        public event EventHandler StateChanged;

        public TrayContext()
        {
            if (SynchronizationContext.Current == null)
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            ui = TaskScheduler.FromCurrentSynchronizationContext();
            Lang.Set(AppSettings.Language);
            Theme.Set(AppSettings.Theme);
            selectedKey = AppSettings.SelectedDevice;

            itemHeader = new ToolStripMenuItem();
            itemHeader.Enabled = false;
            itemDetails = new ToolStripMenuItem("", null, delegate { ShowDetails(); });
            itemRefresh = new ToolStripMenuItem("", null, delegate { RefreshNow(); });
            itemDevices = new ToolStripMenuItem();
            itemSettings = new ToolStripMenuItem("", null, delegate { ShowSettings(); });
            itemStartup = new ToolStripMenuItem("", null, delegate { ToggleStartup(); });
            itemAlert = new ToolStripMenuItem("", null, delegate { SetAlert(!AppSettings.AlertEnabled, AppSettings.AlertLevel); });
            itemExit = new ToolStripMenuItem("", null, delegate { ExitThread(); });
            menu.Items.Add(itemHeader);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(itemDetails);
            menu.Items.Add(itemRefresh);
            menu.Items.Add(itemDevices);
            menu.Items.Add(itemSettings);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(itemStartup);
            menu.Items.Add(itemAlert);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(itemExit);
            menu.Renderer = Theme.MenuRenderer();
            menu.Opening += delegate
            {
                itemStartup.Checked = AppSettings.StartupEnabled();
                itemAlert.Checked = AppSettings.AlertEnabled;
                itemAlert.Text = Lang.F("Menu.Alert", Lang.Pct(AppSettings.AlertLevel));
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
            details.SettingsRequested += delegate { details.Hide(); ShowSettings(); };

            Lang.Changed += OnLangChanged;
            Theme.Changed += OnThemeChanged;
            SystemEvents.UserPreferenceChanged += OnWindowsPreferenceChanged;

            ApplyMenuTexts();
            Apply(new List<Reading>());
            tray.Visible = true;

            timer.Interval = AppSettings.IntervalSeconds * 1000;
            timer.Tick += delegate { RefreshNow(); };
            timer.Start();
            RefreshNow();
        }

        void ApplyMenuTexts()
        {
            itemDetails.Text = Lang.T("Menu.ShowDetails");
            itemRefresh.Text = Lang.T("Menu.Refresh");
            itemDevices.Text = Lang.T("Menu.Mouse");
            itemSettings.Text = Lang.T("Menu.Settings");
            itemStartup.Text = Lang.T("Menu.Startup");
            itemAlert.Text = Lang.F("Menu.Alert", Lang.Pct(AppSettings.AlertLevel));
            itemExit.Text = Lang.T("Menu.Exit");
            FillDeviceMenu();
        }

        void OnLangChanged(object sender, EventArgs e)
        {
            ApplyMenuTexts();
            Render();
        }

        void OnThemeChanged(object sender, EventArgs e)
        {
            menu.Renderer = Theme.MenuRenderer();
        }

        // Windows switched between light and dark: follow it in "system" mode, and redraw the tray icon.
        void OnWindowsPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category != UserPreferenceCategory.General) return;
            if (Theme.Mode == "system") Theme.Notify();
            Render();
        }

        void RaiseStateChanged()
        {
            if (StateChanged != null) StateChanged(this, EventArgs.Empty);
        }

        // "Mouse to display" submenu: automatic, or one of the mice seen since the app started.
        void FillDeviceMenu()
        {
            itemDevices.DropDownItems.Clear();
            ToolStripMenuItem auto = new ToolStripMenuItem(Lang.T("Menu.Automatic"));
            auto.Checked = selectedKey.Length == 0;
            auto.Click += delegate { SelectDevice(""); };
            itemDevices.DropDownItems.Add(auto);
            if (tracks.Count > 0) itemDevices.DropDownItems.Add(new ToolStripSeparator());
            foreach (DeviceTrack t in tracks)
            {
                string key = t.Key;
                ToolStripMenuItem it = new ToolStripMenuItem(t.Name);
                it.Checked = selectedKey == key;
                it.Click += delegate { SelectDevice(key); };
                itemDevices.DropDownItems.Add(it);
            }
            itemDevices.Visible = tracks.Count > 1 || selectedKey.Length > 0;
        }

        void ShowDetails()
        {
            details.ShowNearTray();
            RefreshNow();
        }

        // Only one settings window at a time: bring it back if it is already open.
        void ShowSettings()
        {
            if (settings == null || settings.IsDisposed)
            {
                settings = new SettingsForm(this);
                settings.Show();
            }
            else
            {
                if (settings.WindowState == FormWindowState.Minimized) settings.WindowState = FormWindowState.Normal;
                settings.Show();
            }
            settings.BringToFront();
            settings.Activate();
        }

        void ToggleStartup()
        {
            string error;
            if (!SetStartup(!AppSettings.StartupEnabled(), out error))
                MessageBox.Show(Lang.F("Err.Startup", error), Lang.T("App.Name"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        // ---- IAppHost ----
        public List<DeviceTrack> Tracks { get { return tracks; } }
        public string SelectedKey { get { return selectedKey; } }
        public DateTime LastRefresh { get { return lastRefresh; } }
        public bool Busy { get { return busy; } }

        public void SelectDevice(string key)
        {
            selectedKey = key;
            AppSettings.SelectedDevice = key;
            Render();
            RaiseStateChanged();
        }

        public void SetLanguage(string code)
        {
            AppSettings.Language = code;
            Lang.Set(code);
            RaiseStateChanged();
        }

        public void SetTheme(string mode)
        {
            AppSettings.Theme = mode;
            Theme.Set(mode);
            RaiseStateChanged();
        }

        public bool SetStartup(bool on, out string error)
        {
            error = null;
            try { AppSettings.SetStartup(on); }
            catch (Exception e) { error = e.Message; }
            RaiseStateChanged();
            return error == null;
        }

        public void SetAlert(bool enabled, int level)
        {
            AppSettings.AlertEnabled = enabled;
            AppSettings.AlertLevel = level;
            itemAlert.Text = Lang.F("Menu.Alert", Lang.Pct(AppSettings.AlertLevel));
            RaiseStateChanged();
        }

        public void SetInterval(int seconds)
        {
            AppSettings.IntervalSeconds = seconds;
            timer.Stop();
            timer.Interval = AppSettings.IntervalSeconds * 1000;
            timer.Start();
            RaiseStateChanged();
        }

        public void Exit()
        {
            ExitThread();
        }

        public void RefreshNow()
        {
            if (busy) return;
            busy = true;
            Task.Factory.StartNew<List<Reading>>(delegate { return Devices.ReadAll(null); })
                .ContinueWith(delegate(Task<List<Reading>> t)
                {
                    busy = false;
                    lastRefresh = DateTime.Now;
                    Apply(t.IsFaulted ? new List<Reading>() : t.Result);
                    RaiseStateChanged();
                }, ui);
        }

        // ---- Readings ----
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
            int low = AppSettings.AlertLevel;
            if (r.Charging || r.Percent > low + 5) t.LowAlerted = false;
            else if (r.Percent <= low && !t.LowAlerted && AppSettings.AlertEnabled)
            {
                t.LowAlerted = true;
                tray.ShowBalloonTip(8000, Lang.T("Alert.Title"), Lang.F("Alert.Text", r.Name, Lang.Pct(r.Percent)), ToolTipIcon.Warning);
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
            Render();
        }

        // Redraws the icon, tooltip and details window from the last readings (also after a language change).
        void Render()
        {
            DeviceTrack t;
            Reading r0 = Pick(lastReadings, out t);
            bool settling = IsSettling(t, r0);
            string name = r0.Name.Length > 0 ? r0.Name : Lang.T("NoMouse");
            Reading lastTrusted = t != null ? t.LastTrusted : null;

            string tip;
            int level = 0;
            IconMode mode;
            if (r0.State == MouseState.Ok && r0.Charging && r0.Full)
            {
                mode = IconMode.Normal;
                level = 100;
                tip = Lang.F("Tip.Charged", name, Lang.Pct(100));
            }
            else if (r0.State == MouseState.Ok && r0.Charging)
            {
                mode = IconMode.Charging;
                tip = r0.VoltageBased ? Lang.F("Tip.Charging", name) : Lang.F("Tip.ChargingPct", name, Lang.Pct(r0.Percent));
            }
            else if (settling)
            {
                mode = IconMode.Dim;
                level = r0.Percent;
                tip = Lang.F("Tip.Settling", name);
            }
            else if (r0.State == MouseState.Ok)
            {
                mode = IconMode.Normal;
                level = r0.Percent;
                tip = Lang.F("Tip.Level", name, Lang.Pct(r0.Percent));
            }
            else if (lastTrusted != null)
            {
                mode = IconMode.Dim;
                level = lastTrusted.Percent;
                tip = Lang.F(r0.State == MouseState.Asleep ? "Tip.AsleepLast" : "Tip.NotDetectedLast", name, Lang.Pct(lastTrusted.Percent));
            }
            else
            {
                mode = IconMode.Empty;
                tip = r0.State == MouseState.Asleep ? Lang.F("Tip.Asleep", name)
                    : t != null ? Lang.F("Tip.NotDetected", name)
                    : Lang.T("NoMouse");
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

        protected override void ExitThreadCore()
        {
            timer.Stop();
            SystemEvents.UserPreferenceChanged -= OnWindowsPreferenceChanged;
            Lang.Changed -= OnLangChanged;
            Theme.Changed -= OnThemeChanged;
            if (settings != null && !settings.IsDisposed) settings.Close();
            tray.Visible = false;
            tray.Dispose();
            base.ExitThreadCore();
        }
    }
}
