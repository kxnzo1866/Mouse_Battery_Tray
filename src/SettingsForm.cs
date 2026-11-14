using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MouseBatteryTray
{
    // What the settings window needs from the running app (implemented by TrayContext).
    interface IAppHost
    {
        List<DeviceTrack> Tracks { get; }
        string SelectedKey { get; }
        DateTime LastRefresh { get; }    // DateTime.MinValue = no reading yet
        bool Busy { get; }
        void SelectDevice(string key);
        void SetLanguage(string code);
        void SetTheme(string mode);
        bool SetStartup(bool on, out string error);
        void SetAlert(bool enabled, int level);
        void SetInterval(int seconds);
        void SetAdaptive(bool on);
        void SetLogging(bool on);
        DateTime NextRefresh { get; }
        void RefreshNow();
        void Exit();
        event EventHandler StateChanged;
    }

    enum UpdateState { Idle, Checking, UpToDate, Available, Downloading, Error }

    // ------------------------------------------------------------------
    //  Settings window: a normal window with its own taskbar button.
    //  Closing it does not stop the app.
    // ------------------------------------------------------------------
    class SettingsForm : Form
    {
        public const string GitHubUrl = "https://github.com/" + Updater.Repo;

        readonly IAppHost host;
        readonly float s;
        bool loading;

        readonly TableLayoutPanel table = new TableLayoutPanel();
        readonly Label hGeneral = new Label(), lLanguage = new Label(), lTheme = new Label();
        readonly Label hBattery = new Label(), lPercent = new Label(), lMouse = new Label(), lMouseHint = new Label();
        readonly Label lInterval = new Label(), lLast = new Label();
        readonly Label hAbout = new Label(), lVersion = new Label(), lLicense = new Label();
        readonly Label lChargeTitle = new Label(), lChargeText = new Label(), lUnofficial = new Label();
        readonly ComboBox cLanguage = new ComboBox(), cTheme = new ComboBox(), cMouse = new ComboBox(), cInterval = new ComboBox();
        readonly CheckBox xStartup = new CheckBox(), xAlert = new CheckBox();
        readonly NumericUpDown nAlert = new NumericUpDown();
        readonly Button bRefresh = new Button(), bClose = new Button();
        readonly LinkLabel kGitHub = new LinkLabel(), kLicense = new LinkLabel();
        readonly List<string> mouseKeys = new List<string>();

        // Reading pace and diagnostic log
        readonly CheckBox xAdaptive = new CheckBox(), xLog = new CheckBox();
        readonly Label hDiag = new Label(), lLogHint = new Label();
        readonly LinkLabel kLogFolder = new LinkLabel();

        // Updates
        readonly Label hUpdates = new Label(), lCurrent = new Label(), lUpdate = new Label();
        readonly Button bCheck = new Button(), bInstall = new Button();
        readonly LinkLabel kNotes = new LinkLabel();
        UpdateState updState = UpdateState.Idle;
        UpdateInfo update;
        string updError = "";
        int updPercent;

        public SettingsForm(IAppHost appHost)
        {
            host = appHost;
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) s = g.DpiX / 96f;

            Font = new Font("Segoe UI", 9f);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = true;
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(S(18), S(10), S(18), S(14));
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            table.ColumnCount = 2;
            table.AutoSize = true;
            table.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            table.Location = new Point(Padding.Left, Padding.Top);
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            // General
            Header(hGeneral, false);
            Row(hGeneral, null);
            Row(Caption(lLanguage), Combo(cLanguage));
            Row(Caption(lTheme), Combo(cTheme));
            Check(xStartup);
            Row(xStartup, null);

            // Battery
            Header(hBattery, true);
            Row(hBattery, null);
            Check(xAlert);
            nAlert.Minimum = AppSettings.AlertMin;
            nAlert.Maximum = AppSettings.AlertMax;
            nAlert.Increment = 5;
            nAlert.Width = S(64);
            nAlert.BorderStyle = BorderStyle.FixedSingle;
            nAlert.Margin = new Padding(0, S(3), S(4), S(3));
            lPercent.AutoSize = true;
            lPercent.Anchor = AnchorStyles.Left;
            Row(xAlert, Flow(nAlert, lPercent));
            Row(Caption(lMouse), Combo(cMouse));
            Hint(lMouseHint);
            Row(lMouseHint, null);
            Row(Caption(lInterval), Combo(cInterval));
            Check(xAdaptive);
            Row(xAdaptive, null);
            Button(bRefresh);
            lLast.AutoSize = true;
            lLast.Anchor = AnchorStyles.Left;
            lLast.MaximumSize = new Size(S(250), 0);
            Row(bRefresh, lLast);

            // Diagnostic
            Header(hDiag, true);
            Row(hDiag, null);
            Check(xLog);
            kLogFolder.AutoSize = true;
            kLogFolder.Anchor = AnchorStyles.Left;
            Row(xLog, kLogFolder);
            Hint(lLogHint);
            Row(lLogHint, null);

            // Updates
            Header(hUpdates, true);
            Row(hUpdates, null);
            Wrapped(lCurrent);
            Row(lCurrent, null);
            Button(bCheck);
            lUpdate.AutoSize = true;
            lUpdate.Anchor = AnchorStyles.Left;
            lUpdate.MaximumSize = new Size(S(250), 0);
            Row(bCheck, lUpdate);
            Button(bInstall);
            kNotes.AutoSize = true;
            kNotes.Anchor = AnchorStyles.Left;
            Row(bInstall, kNotes);

            // About
            Header(hAbout, true);
            Row(hAbout, null);
            Wrapped(lVersion);
            Row(lVersion, null);
            kGitHub.AutoSize = true;
            kGitHub.Margin = new Padding(0, S(3), 0, S(3));
            Row(kGitHub, null);
            lLicense.AutoSize = true;
            lLicense.Anchor = AnchorStyles.Left;
            lLicense.Margin = new Padding(0, S(3), S(6), S(3));
            kLicense.AutoSize = true;
            kLicense.Anchor = AnchorStyles.Left;
            Row(Flow(lLicense, kLicense), null);
            lChargeTitle.Font = new Font("Segoe UI Semibold", 9f);
            Wrapped(lChargeTitle);
            lChargeTitle.Margin = new Padding(0, S(10), 0, S(2));
            Row(lChargeTitle, null);
            Wrapped(lChargeText);
            Row(lChargeText, null);
            Wrapped(lUnofficial);
            Row(lUnofficial, null);

            Button(bClose);
            FlowLayoutPanel closeRow = Flow(bClose);
            closeRow.FlowDirection = FlowDirection.RightToLeft;
            closeRow.Dock = DockStyle.Fill;
            closeRow.Margin = new Padding(0, S(12), 0, 0);
            Row(closeRow, null);
            Controls.Add(table);
            CancelButton = bClose;

            // Events
            cLanguage.SelectedIndexChanged += delegate { if (!loading) host.SetLanguage(cLanguage.SelectedIndex == 0 ? "fr" : "en"); };
            cTheme.SelectedIndexChanged += delegate { if (!loading && cTheme.SelectedIndex >= 0) host.SetTheme(Theme.Modes[cTheme.SelectedIndex]); };
            xStartup.CheckedChanged += delegate
            {
                if (loading) return;
                string error;
                if (!host.SetStartup(xStartup.Checked, out error))
                    MessageBox.Show(this, Lang.F("Err.Startup", error), Lang.T("App.Name"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            };
            xAlert.CheckedChanged += delegate { nAlert.Enabled = xAlert.Checked; if (!loading) host.SetAlert(xAlert.Checked, (int)nAlert.Value); };
            nAlert.ValueChanged += delegate { if (!loading) host.SetAlert(xAlert.Checked, (int)nAlert.Value); };
            cMouse.SelectedIndexChanged += delegate
            {
                if (loading || cMouse.SelectedIndex < 0) return;
                host.SelectDevice(cMouse.SelectedIndex == 0 ? "" : mouseKeys[cMouse.SelectedIndex - 1]);
            };
            cInterval.SelectedIndexChanged += delegate { if (!loading && cInterval.SelectedIndex >= 0) host.SetInterval(AppSettings.Intervals[cInterval.SelectedIndex]); };
            bRefresh.Click += delegate { host.RefreshNow(); lLast.Text = Lang.T("Set.Reading"); };
            xAdaptive.CheckedChanged += delegate { if (!loading) host.SetAdaptive(xAdaptive.Checked); };
            xLog.CheckedChanged += delegate { if (!loading) host.SetLogging(xLog.Checked); };
            kLogFolder.LinkClicked += delegate
            {
                try { Directory.CreateDirectory(Logger.Folder); } catch { }
                Open(Logger.Folder);
            };
            bClose.Click += delegate { Close(); };
            bCheck.Click += delegate { CheckForUpdate(); };
            bInstall.Click += delegate { InstallUpdate(); };
            kNotes.LinkClicked += delegate { if (update != null && update.PageUrl.Length > 0) Open(update.PageUrl); };
            kGitHub.LinkClicked += delegate { Open(GitHubUrl); };
            kLicense.LinkClicked += delegate
            {
                string local = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "LICENSE.txt");
                Open(File.Exists(local) ? local : GitHubUrl + "/blob/main/LICENSE");
            };

            host.StateChanged += OnHostChanged;
            Lang.Changed += OnLangChanged;
            Theme.Changed += OnThemeChanged;

            ApplyTexts();
            LoadValues();
            ApplyTheme();
        }

        int S(int px) { return (int)Math.Round(px * s); }

        // ---- Layout helpers ----
        void Row(Control a, Control b)
        {
            int r = table.RowCount++;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.Controls.Add(a, 0, r);
            if (b != null) table.Controls.Add(b, 1, r);
            else table.SetColumnSpan(a, 2);
        }

        void Header(Label l, bool spaced)
        {
            l.AutoSize = true;
            l.Font = new Font("Segoe UI Semibold", 10.5f);
            l.Margin = new Padding(0, S(spaced ? 16 : 4), 0, S(4));
        }

        Label Caption(Label l)
        {
            l.AutoSize = true;
            l.Anchor = AnchorStyles.Left;
            l.Margin = new Padding(0, S(6), S(18), S(6));
            return l;
        }

        ComboBox Combo(ComboBox c)
        {
            c.DropDownStyle = ComboBoxStyle.DropDownList;
            c.FlatStyle = FlatStyle.Flat;
            c.Width = S(210);
            c.Margin = new Padding(0, S(3), 0, S(3));
            c.HandleCreated += delegate { Theme.ApplyNative(c, true); };
            return c;
        }

        void Check(CheckBox x)
        {
            x.AutoSize = true;
            x.Anchor = AnchorStyles.Left;
            x.FlatStyle = FlatStyle.Flat;
            x.Margin = new Padding(0, S(6), S(18), S(6));
        }

        void Hint(Label l)
        {
            Wrapped(l);
            l.Margin = new Padding(0, 0, 0, S(4));
        }

        void Wrapped(Label l)
        {
            l.AutoSize = true;
            l.MaximumSize = new Size(S(440), 0);
            l.Margin = new Padding(0, S(3), 0, S(3));
        }

        void Button(Button b)
        {
            b.AutoSize = true;
            b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            b.FlatStyle = FlatStyle.Flat;
            b.Padding = new Padding(S(10), S(2), S(10), S(2));
            b.Margin = new Padding(0, S(6), S(18), S(6));
            b.Anchor = AnchorStyles.Left;
        }

        FlowLayoutPanel Flow(params Control[] items)
        {
            FlowLayoutPanel f = new FlowLayoutPanel();
            f.AutoSize = true;
            f.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            f.WrapContents = false;
            f.Margin = new Padding(0);
            f.Padding = new Padding(0);
            f.Anchor = AnchorStyles.Left;
            f.Controls.AddRange(items);
            return f;
        }

        // ---- Texts (called again when the language changes) ----
        void ApplyTexts()
        {
            bool was = loading;
            loading = true;
            SuspendLayout();
            Text = Lang.T("Set.Title");
            hGeneral.Text = Lang.T("Set.General");
            lLanguage.Text = Lang.T("Set.Language");
            lTheme.Text = Lang.T("Set.Theme");
            xStartup.Text = Lang.T("Set.Startup");
            hBattery.Text = Lang.T("Set.Battery");
            xAlert.Text = Lang.T("Set.Alert");
            lPercent.Text = Lang.Pct("").Trim();
            lMouse.Text = Lang.T("Set.Mouse");
            lMouseHint.Text = Lang.T("Set.MouseHint");
            lInterval.Text = Lang.T("Set.Interval");
            bRefresh.Text = Lang.T("Set.RefreshNow");
            xAdaptive.Text = Lang.T("Set.Adaptive");
            hDiag.Text = Lang.T("Set.Diagnostic");
            xLog.Text = Lang.T("Set.Log");
            kLogFolder.Text = Lang.T("Set.LogFolder");
            lLogHint.Text = Lang.F("Set.LogHint", Logger.Files, Logger.MaxBytes / (1024 * 1024));
            hUpdates.Text = Lang.T("Set.Updates");
            lCurrent.Text = Lang.F("Upd.Current", Updater.Format(Updater.Current));
            bCheck.Text = Lang.T("Upd.Check");
            bInstall.Text = Lang.T("Upd.Install");
            kNotes.Text = Lang.T("Upd.Notes");
            ShowUpdateState();
            hAbout.Text = Lang.T("Set.About");
            lVersion.Text = Lang.T("App.Name") + " — " + Lang.F("About.Version", Updater.Format(Updater.Current));
            kGitHub.Text = Lang.T("About.GitHub");
            lLicense.Text = Lang.T("About.License");
            kLicense.Text = Lang.T("About.ViewLicense");
            lChargeTitle.Text = Lang.T("About.ChargeTitle");
            lChargeText.Text = Lang.T("About.ChargeText");
            lUnofficial.Text = Lang.T("About.Unofficial");
            bClose.Text = Lang.T("Set.Close");

            Fill(cLanguage, new string[] { Lang.T("Lang.fr"), Lang.T("Lang.en") });
            List<string> themes = new List<string>();
            foreach (string m in Theme.Modes) themes.Add(Lang.T("Theme." + m));
            Fill(cTheme, themes.ToArray());
            List<string> intervals = new List<string>();
            foreach (int sec in AppSettings.Intervals)
                intervals.Add(sec < 60 ? Lang.F("Interval.Sec", sec) : Lang.F("Interval.Min", sec / 60));
            Fill(cInterval, intervals.ToArray());
            FillMice();
            UpdateLastReading();
            ResumeLayout(true);
            loading = was;
        }

        // Replaces the items of a combo box and keeps the selected position.
        static void Fill(ComboBox c, string[] items)
        {
            int sel = c.SelectedIndex;
            c.BeginUpdate();
            c.Items.Clear();
            c.Items.AddRange(items);
            if (sel >= 0 && sel < items.Length) c.SelectedIndex = sel;
            c.EndUpdate();
        }

        void FillMice()
        {
            mouseKeys.Clear();
            List<string> names = new List<string>();
            names.Add(Lang.T("Menu.Automatic"));
            foreach (DeviceTrack t in host.Tracks)
            {
                mouseKeys.Add(t.Key);
                names.Add(t.Name);
            }
            cMouse.BeginUpdate();
            cMouse.Items.Clear();
            cMouse.Items.AddRange(names.ToArray());
            int index = mouseKeys.IndexOf(host.SelectedKey);
            cMouse.SelectedIndex = host.SelectedKey.Length == 0 || index < 0 ? 0 : index + 1;
            cMouse.EndUpdate();
            bool several = host.Tracks.Count > 1 || host.SelectedKey.Length > 0;
            cMouse.Enabled = several;
            lMouseHint.Visible = !several;
        }

        void UpdateLastReading()
        {
            if (host.Busy) lLast.Text = Lang.T("Set.Reading");
            else if (host.LastRefresh == DateTime.MinValue) lLast.Text = Lang.T("Set.NoReading");
            else if (host.NextRefresh > DateTime.Now)
                lLast.Text = Lang.F("Set.LastNext", host.LastRefresh.ToString("HH:mm:ss"), host.NextRefresh.ToString("HH:mm:ss"));
            else lLast.Text = Lang.F("Set.LastReading", host.LastRefresh.ToString("HH:mm:ss"));
        }

        // ---- Updates ----
        void ShowUpdateState()
        {
            switch (updState)
            {
                case UpdateState.Checking: lUpdate.Text = Lang.T("Upd.Checking"); break;
                case UpdateState.UpToDate: lUpdate.Text = Lang.T("Upd.UpToDate"); break;
                case UpdateState.Available: lUpdate.Text = Lang.F("Upd.Available", Updater.Format(update.Version)); break;
                case UpdateState.Downloading: lUpdate.Text = Lang.F("Upd.Downloading", Lang.Pct(updPercent)); break;
                case UpdateState.Error: lUpdate.Text = Lang.F("Upd.Error", updError); break;
                default: lUpdate.Text = ""; break;
            }
            bool available = updState == UpdateState.Available || updState == UpdateState.Downloading;
            bInstall.Visible = available;
            kNotes.Visible = available && update != null && update.PageUrl.Length > 0;
            bInstall.Enabled = updState == UpdateState.Available;
            bCheck.Enabled = updState != UpdateState.Checking && updState != UpdateState.Downloading;
        }

        void SetUpdateState(UpdateState state)
        {
            if (IsDisposed) return;
            updState = state;
            ShowUpdateState();
        }

        void CheckForUpdate()
        {
            SetUpdateState(UpdateState.Checking);
            TaskScheduler uiThread = TaskScheduler.FromCurrentSynchronizationContext();
            Task.Factory.StartNew<UpdateInfo>(Updater.FetchLatest).ContinueWith(delegate(Task<UpdateInfo> t)
            {
                if (IsDisposed) return;
                if (t.IsFaulted)
                {
                    updError = t.Exception.GetBaseException().Message;
                    SetUpdateState(UpdateState.Error);
                    return;
                }
                update = t.Result;
                SetUpdateState(Updater.IsNewer(update) ? UpdateState.Available : UpdateState.UpToDate);
            }, uiThread);
        }

        void InstallUpdate()
        {
            if (update == null) return;
            if (MessageBox.Show(this, Lang.F("Upd.Confirm", Updater.Format(update.Version)), Lang.T("App.Name"),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            updPercent = 0;
            SetUpdateState(UpdateState.Downloading);
            UpdateInfo u = update;
            TaskScheduler uiThread = TaskScheduler.FromCurrentSynchronizationContext();
            Task.Factory.StartNew<string>(delegate
            {
                return Updater.Download(u, delegate(int pct)
                {
                    if (IsDisposed) return;
                    BeginInvoke(new MethodInvoker(delegate { updPercent = pct; ShowUpdateState(); }));
                });
            }).ContinueWith(delegate(Task<string> t)
            {
                if (IsDisposed) return;
                if (t.IsFaulted)
                {
                    updError = t.Exception.GetBaseException().Message;
                    SetUpdateState(UpdateState.Error);
                    return;
                }
                try
                {
                    // The installer closes the app anyway; quitting first avoids a "file in use" pause.
                    Updater.Launch(t.Result);
                    host.Exit();
                }
                catch (Exception ex)
                {
                    updError = ex.Message;
                    SetUpdateState(UpdateState.Error);
                }
            }, uiThread);
        }

        // ---- Values (called again when something changes elsewhere, e.g. from the tray menu) ----
        void LoadValues()
        {
            bool was = loading;
            loading = true;
            cLanguage.SelectedIndex = Lang.Code == "fr" ? 0 : 1;
            cTheme.SelectedIndex = Array.IndexOf(Theme.Modes, Theme.Mode);
            xStartup.Checked = AppSettings.StartupEnabled();
            xAlert.Checked = AppSettings.AlertEnabled;
            nAlert.Value = AppSettings.AlertLevel;
            nAlert.Enabled = xAlert.Checked;
            cInterval.SelectedIndex = Math.Max(0, Array.IndexOf(AppSettings.Intervals, AppSettings.IntervalSeconds));
            xAdaptive.Checked = AppSettings.Adaptive;
            xLog.Checked = AppSettings.LogEnabled;
            FillMice();
            UpdateLastReading();
            loading = was;
        }

        // ---- Theme ----
        void ApplyTheme()
        {
            BackColor = Theme.Bg;
            ForeColor = Theme.Fg;
            Style(this);
            foreach (Label l in new Label[] { lMouseHint, lChargeText, lUnofficial, lLast, lVersion, lLicense, lUpdate, lLogHint })
                l.ForeColor = Theme.Sub;
            Theme.ApplyTitleBar(this);
            Invalidate(true);
        }

        void Style(Control parent)
        {
            foreach (Control c in parent.Controls)
            {
                c.ForeColor = Theme.Fg;
                if (c is ComboBox || c is NumericUpDown)
                {
                    c.BackColor = Theme.Surface;
                    Theme.ApplyNative(c, c is ComboBox);
                }
                else if (c is Button)
                {
                    Button b = (Button)c;
                    b.BackColor = Theme.Surface;
                    b.FlatAppearance.BorderColor = Theme.Border;
                    b.FlatAppearance.MouseOverBackColor = Theme.Hover;
                    b.FlatAppearance.MouseDownBackColor = Theme.Track;
                }
                else if (c is LinkLabel)
                {
                    LinkLabel k = (LinkLabel)c;
                    k.BackColor = Theme.Bg;
                    k.LinkColor = Theme.Accent;
                    k.ActiveLinkColor = Theme.Accent;
                    k.VisitedLinkColor = Theme.Accent;
                }
                else c.BackColor = Theme.Bg;
                Style(c);
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.ApplyTitleBar(this);
        }

        // ---- Live updates ----
        void OnHostChanged(object sender, EventArgs e)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { BeginInvoke(new EventHandler(OnHostChanged), sender, e); return; }
            LoadValues();
        }

        void OnLangChanged(object sender, EventArgs e)
        {
            ApplyTexts();
            LoadValues();
        }

        void OnThemeChanged(object sender, EventArgs e)
        {
            ApplyTheme();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            host.StateChanged -= OnHostChanged;
            Lang.Changed -= OnLangChanged;
            Theme.Changed -= OnThemeChanged;
            base.OnFormClosed(e);
        }

        void Open(string target)
        {
            try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
            catch (Exception ex)
            {
                MessageBox.Show(this, Lang.F("Err.OpenLink", ex.Message), Lang.T("App.Name"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
