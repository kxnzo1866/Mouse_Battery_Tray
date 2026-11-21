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
        void SetVisualStyle(string style);
        void SetEffects(bool on);
        bool SetStartup(bool on, out string error);
        void SetAlert(bool enabled, int level);
        void SetInterval(int seconds);
        void SetAdaptive(bool on);
        void SetIconPercent(bool on);
        void SetColorLevels(int green, int yellow);
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

        // Two pages ("General", "Styles") under a tab strip, the Close button below them.
        readonly TableLayoutPanel outer = new TableLayoutPanel();
        readonly TabStrip tabs = new TabStrip();
        readonly Panel pages = new Panel();
        readonly TableLayoutPanel table = new TableLayoutPanel(), styles = new TableLayoutPanel();
        TableLayoutPanel target;          // page that Row() fills

        // Styles page
        readonly Label hStyle = new Label(), lEffectsHint = new Label();
        readonly CheckBox xEffects = new CheckBox();
        readonly List<StyleCard> cards = new List<StyleCard>();

        readonly Label hGeneral = new Label(), lLanguage = new Label(), lTheme = new Label();
        readonly Label hBattery = new Label(), lPercent = new Label(), lMouse = new Label(), lMouseHint = new Label();
        readonly Label lInterval = new Label(), lLast = new Label();
        readonly Label hAbout = new Label(), lVersion = new Label(), lLicense = new Label();
        readonly Label lChargeTitle = new Label(), lChargeText = new Label(), lUnofficial = new Label();
        readonly ComboBox cLanguage = new ComboBox(), cTheme = new ComboBox(), cMouse = new ComboBox(), cInterval = new ComboBox();
        readonly CheckBox xStartup = new CheckBox(), xAlert = new CheckBox(), xIconPercent = new CheckBox();
        readonly NumericUpDown nAlert = new NumericUpDown();

        // Icon colour thresholds (green from / yellow from, red below)
        readonly Label lGreen = new Label(), lYellow = new Label(), lGreenPct = new Label(), lYellowPct = new Label(), lColorHint = new Label();
        readonly NumericUpDown nGreen = new NumericUpDown(), nYellow = new NumericUpDown();
        readonly Button bRefresh = new StyledButton(), bClose = new StyledButton();
        readonly LinkLabel kGitHub = new LinkLabel(), kLicense = new LinkLabel();
        readonly List<string> mouseKeys = new List<string>();

        // Reading pace and diagnostic log
        readonly CheckBox xAdaptive = new CheckBox(), xLog = new CheckBox();
        readonly Label hDiag = new Label(), lLogHint = new Label();
        readonly LinkLabel kLogFolder = new LinkLabel();

        // Updates
        readonly Label hUpdates = new Label(), lCurrent = new Label(), lUpdate = new Label();
        readonly Button bCheck = new StyledButton(), bInstall = new StyledButton();
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

            Page(table);
            Page(styles);
            target = table;

            // General
            Header(hGeneral, false);
            Row(hGeneral, null);
            Row(Caption(lLanguage), Combo(cLanguage));
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
            Check(xIconPercent);
            Row(xIconPercent, null);
            Row(Caption(lGreen), Flow(Spin(nGreen), PctLabel(lGreenPct)));
            Row(Caption(lYellow), Flow(Spin(nYellow), PctLabel(lYellowPct)));
            Hint(lColorHint);
            Row(lColorHint, null);
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

            // Styles: light/dark theme, one card per style, transparency effects
            target = styles;
            Header(hStyle, false);
            Row(hStyle, null);
            Row(Caption(lTheme), Combo(cTheme));
            TableLayoutPanel grid = new TableLayoutPanel();
            grid.ColumnCount = 2;
            grid.AutoSize = true;
            grid.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            grid.Margin = new Padding(0, S(6), 0, S(2));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            for (int i = 0; i < Theme.Styles.Length; i++)
            {
                StyleCard card = new StyleCard(Theme.Styles[i]);
                card.Size = new Size(S(214), S(196));
                card.Margin = new Padding(0, 0, i % 2 == 0 ? S(12) : 0, S(12));
                card.Picked += delegate(object sender, EventArgs e) { host.SetVisualStyle(((StyleCard)sender).Key); };
                cards.Add(card);
                grid.Controls.Add(card, i % 2, i / 2);
            }
            Row(grid, null);
            Check(xEffects);
            Row(xEffects, null);
            Hint(lEffectsHint);
            Row(lEffectsHint, null);

            // Window: tabs, the page shown, Close
            pages.Margin = new Padding(0);
            table.Location = Point.Empty;
            styles.Location = Point.Empty;
            styles.Visible = false;
            pages.Controls.Add(table);
            pages.Controls.Add(styles);
            tabs.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            tabs.Margin = new Padding(0, S(2), 0, S(10));

            Button(bClose);
            FlowLayoutPanel closeRow = Flow(bClose);
            closeRow.FlowDirection = FlowDirection.RightToLeft;
            closeRow.Dock = DockStyle.Fill;
            closeRow.Margin = new Padding(0, S(12), 0, 0);

            outer.ColumnCount = 1;
            outer.AutoSize = true;
            outer.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            outer.Location = new Point(Padding.Left, Padding.Top);
            outer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            for (int i = 0; i < 3; i++) outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            outer.Controls.Add(tabs, 0, 0);
            outer.Controls.Add(pages, 0, 1);
            outer.Controls.Add(closeRow, 0, 2);
            Controls.Add(outer);
            CancelButton = bClose;

            // Events
            tabs.SelectedChanged += delegate
            {
                table.Visible = tabs.Selected == 0;
                styles.Visible = tabs.Selected == 1;
            };
            xEffects.CheckedChanged += delegate { if (!loading) host.SetEffects(xEffects.Checked); };
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
            xIconPercent.CheckedChanged += delegate { if (!loading) host.SetIconPercent(xIconPercent.Checked); };
            nGreen.ValueChanged += delegate { if (!loading) host.SetColorLevels((int)nGreen.Value, (int)nYellow.Value); };
            nYellow.ValueChanged += delegate { if (!loading) host.SetColorLevels((int)nGreen.Value, (int)nYellow.Value); };
            cMouse.SelectedIndexChanged += delegate
            {
                if (loading || cMouse.SelectedIndex < 0) return;
                host.SelectDevice(cMouse.SelectedIndex == 0 ? "" : mouseKeys[cMouse.SelectedIndex - 1]);
            };
            cInterval.SelectedIndexChanged += delegate { if (!loading && cInterval.SelectedIndex >= 0) host.SetInterval(AppSettings.Intervals[cInterval.SelectedIndex]); };
            bRefresh.Click += delegate { host.RefreshNow(); lLast.Text = Lang.T("Set.Reading"); };
            xAdaptive.CheckedChanged += delegate { if (!loading) host.SetAdaptive(xAdaptive.Checked); };
            xLog.CheckedChanged += delegate { if (!loading) host.SetLogging(xLog.Checked); };
            kLogFolder.LinkClicked += delegate { OpenFolder(Logger.Folder); };
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
        static void Page(TableLayoutPanel t)
        {
            t.ColumnCount = 2;
            t.AutoSize = true;
            t.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            t.Margin = new Padding(0);
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        }

        void Row(Control a, Control b)
        {
            int r = target.RowCount++;
            target.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            target.Controls.Add(a, 0, r);
            if (b != null) target.Controls.Add(b, 1, r);
            else target.SetColumnSpan(a, 2);
        }

        // Both pages share one size (the larger), so the window does not jump when switching tabs.
        void FitPages()
        {
            Size a = table.GetPreferredSize(Size.Empty), b = styles.GetPreferredSize(Size.Empty);
            Size size = new Size(Math.Max(a.Width, b.Width), Math.Max(a.Height, b.Height));
            if (pages.Size != size) pages.Size = size;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.Tab) || keyData == (Keys.Control | Keys.Shift | Keys.Tab))
            {
                tabs.Selected = 1 - tabs.Selected;
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
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

        NumericUpDown Spin(NumericUpDown n)
        {
            n.Minimum = 0;
            n.Maximum = 100;
            n.Increment = 5;
            n.Width = S(64);
            n.BorderStyle = BorderStyle.FixedSingle;
            n.Margin = new Padding(0, S(3), S(4), S(3));
            return n;
        }

        Label PctLabel(Label l)
        {
            l.AutoSize = true;
            l.Anchor = AnchorStyles.Left;
            return l;
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
            xIconPercent.Text = Lang.T("Set.IconPercent");
            lGreen.Text = Lang.T("Set.ColorGreen");
            lYellow.Text = Lang.T("Set.ColorYellow");
            lGreenPct.Text = lPercent.Text;
            lYellowPct.Text = lPercent.Text;
            lColorHint.Text = Lang.T("Set.ColorHint");
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
            tabs.SetItems(new string[] { Lang.T("Set.TabGeneral"), Lang.T("Set.TabStyles") });
            hStyle.Text = Lang.T("Set.Style");
            xEffects.Text = Lang.T("Set.Effects");
            lEffectsHint.Text = Lang.T("Set.EffectsHint");
            foreach (StyleCard card in cards)
            {
                card.Title = Lang.T("Style." + card.Key);
                card.Description = Lang.T("Style." + card.Key + ".Desc");
                card.AccessibleName = card.Title;
                card.Invalidate();
            }

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
            FitPages();
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
            FitPages();
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
            xIconPercent.Checked = AppSettings.IconPercent;
            // Ranges keep yellow below green; Value is set after the widest range so it is never clamped.
            nGreen.Minimum = 0; nGreen.Maximum = 100; nYellow.Minimum = 0; nYellow.Maximum = 100;
            nGreen.Value = AppSettings.GreenLevel;
            nYellow.Value = AppSettings.YellowLevel;
            nGreen.Minimum = nYellow.Value + AppSettings.ColorGap;
            nGreen.Maximum = AppSettings.GreenMax;
            nYellow.Minimum = AppSettings.YellowMin;
            nYellow.Maximum = nGreen.Value - AppSettings.ColorGap;
            cInterval.SelectedIndex = Math.Max(0, Array.IndexOf(AppSettings.Intervals, AppSettings.IntervalSeconds));
            xAdaptive.Checked = AppSettings.Adaptive;
            xLog.Checked = AppSettings.LogEnabled;
            xEffects.Checked = Theme.Effects;
            foreach (StyleCard card in cards) card.Selected = card.Key == Theme.Style;
            FillMice();
            UpdateLastReading();
            FitPages();
            loading = was;
        }

        // ---- Theme and visual style ----
        void ApplyTheme()
        {
            Palette p = Theme.P;
            SuspendLayout();
            Font = Theme.MakeFont(p.BodyFont, 9f);
            foreach (Label h in new Label[] { hGeneral, hBattery, hDiag, hUpdates, hAbout, hStyle })
                h.Font = Theme.MakeFont(p.HeaderFont, p.HeaderSize);
            lChargeTitle.Font = Theme.MakeFont(p.Style == "metro" ? "Segoe UI Semilight" : "Segoe UI Semibold", 9f);
            BackColor = p.Bg;
            ForeColor = p.Fg;
            Style(this);
            foreach (Label h in new Label[] { hGeneral, hBattery, hDiag, hUpdates, hAbout, hStyle })
                h.ForeColor = p.HeaderFg;
            foreach (Label l in new Label[] { lColorHint, lMouseHint,lChargeText, lUnofficial, lLast, lVersion, lLicense, lUpdate, lLogHint, lEffectsHint })
                l.ForeColor = p.Sub;
            tabs.Measure();
            ResumeLayout(true);
            FitPages();
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
                    c.BackColor = Theme.Surface;    // StyledButton draws itself from Theme.P
                    c.Invalidate();
                }
                else if (c is CheckBox)
                {
                    c.BackColor = Theme.Bg;
                    ((CheckBox)c).FlatAppearance.BorderColor = Theme.Sub;
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
            Unsubscribe();
            base.OnFormClosed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Unsubscribe();
            base.Dispose(disposing);
        }

        void Unsubscribe()
        {
            host.StateChanged -= OnHostChanged;
            Lang.Changed -= OnLangChanged;
            Theme.Changed -= OnThemeChanged;
        }

        // Tab 0 = General, 1 = Styles.
        public void ShowTab(int index)
        {
            tabs.Selected = index;
            table.Visible = index == 0;
            styles.Visible = index == 1;
        }

        void Open(string target)
        {
            try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
            catch (Exception ex)
            {
                MessageBox.Show(this, Lang.F("Err.OpenLink", ex.Message), Lang.T("App.Name"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // Folders are opened with explorer.exe directly: ShellExecute on a folder path depends on the "Folder"
        // shell association, which can be broken or hijacked (error "not found", then later clicks do nothing).
        void OpenFolder(string folder)
        {
            try
            {
                folder = Path.GetFullPath(folder);
                Directory.CreateDirectory(folder);
                if (!Directory.Exists(folder)) throw new DirectoryNotFoundException(folder);
                string explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
                string log = Logger.FilePath;
                string args = File.Exists(log) ? "/select,\"" + log + "\"" : "\"" + folder + "\"";
                Process.Start(new ProcessStartInfo(explorer, args) { UseShellExecute = false });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, Lang.F("Err.OpenFolder", folder, ex.Message), Lang.T("App.Name"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
