// Main window: game library on the left, the selected game (play, mod, DLSS settings) on the right.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using DlssgUi;

namespace DlssgLauncher
{
    static class Status
    {
        public static Color Tone(ModKind k)
        {
            switch (k)
            {
                case ModKind.Installed: return Theme.Accent;
                case ModKind.Outdated: return Theme.Warn;
                case ModKind.Partial: return Theme.Warn;
                default: return Theme.Muted;
            }
        }
    }

    class GameListBox : ListBox
    {
        int hot = -1;
        public GameListBox()
        {
            DrawMode = DrawMode.OwnerDrawFixed; ItemHeight = Theme.S(62); BorderStyle = BorderStyle.None;
            BackColor = Theme.Side; ForeColor = Theme.Text; IntegralHeight = false; Font = Theme.Body;
        }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Native.DarkScroll(this); }
        void InvalidateItem(int i) { if (i >= 0 && i < Items.Count) Invalidate(GetItemRectangle(i)); }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            int i = IndexFromPoint(e.Location);
            if (i != hot) { int old = hot; hot = i; InvalidateItem(old); InvalidateItem(hot); }
            base.OnMouseMove(e);
        }
        protected override void OnMouseLeave(EventArgs e) { int old = hot; hot = -1; InvalidateItem(old); base.OnMouseLeave(e); }
        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            var g = e.Graphics; var r = e.Bounds;
            using (var b = new SolidBrush(Theme.Side)) g.FillRectangle(b, r);
            if (e.Index < 0 || e.Index >= Items.Count) return;
            var game = Items[e.Index] as Game;
            if (game == null) return;
            Theme.Prep(g);
            bool sel = (e.State & DrawItemState.Selected) != 0;
            var card = new RectangleF(r.X + Theme.Sf(6), r.Y + Theme.Sf(3), r.Width - Theme.Sf(12), r.Height - Theme.Sf(6));
            if (sel)
            {
                Theme.FillRound(g, card, Theme.Sf(8), Theme.Selected);
                Theme.FillRound(g, new RectangleF(card.X, card.Y + Theme.Sf(12), Theme.Sf(3), card.Height - Theme.Sf(24)), Theme.Sf(1.5f), Theme.Accent);
            }
            else if (e.Index == hot) Theme.FillRound(g, card, Theme.Sf(8), Theme.Card);
            int ic = Theme.S(38);
            var ir = new Rectangle((int)card.X + Theme.S(12), r.Y + (r.Height - ic) / 2, ic, ic);
            if (game.Icon != null) g.DrawImage(game.Icon, ir);
            else { Theme.FillRound(g, ir, Theme.Sf(8), Theme.Input); Theme.DrawGlyph(g, Glyphs.Game, Theme.Glyph, Theme.Muted, ir); }
            int tx = ir.Right + Theme.S(12), tw = (int)card.Right - tx - Theme.S(8);
            var fl = TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;
            TextRenderer.DrawText(g, game.Name, Theme.Bold, new Rectangle(tx, r.Y + Theme.S(11), tw, Theme.S(20)), Theme.Text, fl);
            int sy = r.Y + Theme.S(33);
            using (var b = new SolidBrush(Status.Tone(game.State.Kind))) g.FillEllipse(b, tx, sy + Theme.S(6), Theme.S(7), Theme.S(7));
            string st = game.State.Short + (game.AntiCheat != null ? "   \u00B7   anti-cheat" : "");
            TextRenderer.DrawText(g, st, Theme.Small, new Rectangle(tx + Theme.S(13), sy, tw - Theme.S(13), Theme.S(19)), Theme.Muted, fl);
        }
    }

    class BrandHeader : UiControl
    {
        public string Sub = "";
        public BrandHeader() { BackColor = Theme.Side; }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Theme.Prep(g); g.Clear(ParentBack);
            int s = Theme.S(38);
            Theme.DrawLogo(g, new RectangleF(0, (Height - s) / 2f, s, s));
            var fl = TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
            TextRenderer.DrawText(g, "DLSSG Launcher", Theme.H2, new Rectangle(s + Theme.S(12), Theme.S(1), Width - s - Theme.S(12), Theme.S(24)), Theme.Text, fl);
            TextRenderer.DrawText(g, Sub, Theme.Small, new Rectangle(s + Theme.S(12), Theme.S(24), Width - s - Theme.S(12), Theme.S(18)), Theme.Muted, fl);
        }
    }

    class HeroHeader : UiControl, IAutoHeight
    {
        public Game Game;
        public HeroHeader() { BackColor = Theme.Card; }
        public int HeightFor(int w) { return Theme.S(84); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Theme.Prep(g); g.Clear(ParentBack);
            if (Game == null) return;
            int ic = Theme.S(76);
            var ir = new Rectangle(0, (Height - ic) / 2, ic, ic);
            if (Game.Icon != null) g.DrawImage(Game.Icon, ir);
            else { Theme.FillRound(g, ir, Theme.Sf(12), Theme.Input); Theme.DrawGlyph(g, Glyphs.Game, Theme.GlyphHuge, Theme.Muted, ir); }
            int tx = ic + Theme.S(20), tw = Width - tx;
            TextRenderer.DrawText(g, Game.Name, Theme.H1, new Rectangle(tx, 0, tw, Theme.S(38)), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, Game.Exe, Theme.Small, new Rectangle(tx, Theme.S(39), tw, Theme.S(18)), Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.PathEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            int cx = tx, cy = Theme.S(61);
            cx += Chip(g, cx, cy, Game.State.Text, Status.Tone(Game.State.Kind));
            if (Game.AntiCheat != null) cx += Chip(g, cx, cy, "Anti-cheat", Theme.Danger);
            string src = Game.Source == "Added" ? "Added by you" : Game.Source == "Store" ? "Epic / GOG" : Game.Source == "Library" || Game.Source == "Games" ? null : Game.Source;
            if (!string.IsNullOrEmpty(src)) Chip(g, cx, cy, src, Theme.Muted);
        }
        int Chip(Graphics g, int x, int y, string text, Color tone)
        {
            var sz = TextRenderer.MeasureText(g, text, Theme.Small, Size.Empty, TextFormatFlags.NoPadding);
            var r = new RectangleF(x, y, sz.Width + Theme.S(22), Theme.S(22));
            Theme.FillRound(g, r, r.Height / 2, Theme.Mix(Theme.Card, tone, 0.18f));
            TextRenderer.DrawText(g, text, Theme.Small, Rectangle.Round(r), Theme.Mix(tone, Theme.Text, 0.3f),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            return (int)r.Width + Theme.S(8);
        }
    }

    class PlayRow : Panel, IAutoHeight
    {
        public readonly FlatButton Play;
        public readonly Label Status = new Label { AutoSize = false, ForeColor = Theme.Muted, BackColor = Theme.Card, Font = Theme.Body, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
        public readonly ProgressLine Bar = new ProgressLine { Visible = false, BackColor = Theme.Card };
        public PlayRow(FlatButton play) { BackColor = Theme.Card; Play = play; Controls.Add(play); Controls.Add(Status); Controls.Add(Bar); }
        public int HeightFor(int w) { return w < Theme.S(560) ? Theme.S(110) : Theme.S(56); }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int bw = Math.Min(Theme.S(300), Width);
            Play.SetBounds(0, 0, bw, Theme.S(56));
            if (Width < Theme.S(560)) { Status.SetBounds(0, Theme.S(64), Width, Theme.S(22)); Bar.SetBounds(0, Theme.S(94), Width, Theme.S(6)); }
            else
            {
                int x = bw + Theme.S(20);
                Status.SetBounds(x, Bar.Visible ? Theme.S(8) : Theme.S(17), Width - x, Theme.S(22));
                Bar.SetBounds(x, Theme.S(36), Width - x, Theme.S(6));
            }
        }
    }

    class EmptyState : Panel
    {
        public string Title = "Choose a game", Body = "Pick a game on the left, or add one.";
        public readonly FlatButton AddFolder = new FlatButton("Add game folder", Glyphs.Folder, BtnKind.Primary);
        public readonly FlatButton AddExe = new FlatButton("Pick game EXE", Glyphs.Add, BtnKind.Secondary);
        public EmptyState()
        {
            BackColor = Theme.Bg; DoubleBuffered = true; ResizeRedraw = true;
            AddFolder.BackColor = Theme.Bg; AddExe.BackColor = Theme.Bg;
            Controls.Add(AddFolder); Controls.Add(AddExe);
        }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int w1 = AddFolder.PreferredWidth, w2 = AddExe.PreferredWidth, gap = Theme.S(10);
            int x = (Width - w1 - w2 - gap) / 2, y = Height / 2 + Theme.S(40);
            AddFolder.SetBounds(x, y, w1, Theme.S(38)); AddExe.SetBounds(x + w1 + gap, y, w2, Theme.S(38));
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Theme.Prep(g);
            int s = Theme.S(64), cy = Height / 2 - Theme.S(110);
            Theme.DrawLogo(g, new RectangleF((Width - s) / 2f, cy, s, s));
            TextRenderer.DrawText(g, Title, Theme.H1, new Rectangle(0, cy + s + Theme.S(16), Width, Theme.S(38)), Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, Body, Theme.Body, new Rectangle(Theme.S(40), cy + s + Theme.S(58), Width - Theme.S(80), Theme.S(44)), Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
        }
    }

    public class MainForm : DarkForm
    {
        readonly List<Game> games = new List<Game>();
        Game current;
        bool scanning, busy, dirty, loading, suppressList;
        IniFile ini;
        string customFrames;
        List<string> otherInstalls = new List<string>();
        string pendingSelect;

        Panel side; BrandHeader brand; InputBox search; FlatButton bAddFolder, bAddExe, bRescan, bLibraries; GameListBox list; Label sideStatus;
        Panel host; VStack stack; EmptyState empty;
        Card hero; HeroHeader heroHead; Card.Row acRow; Notice acNotice; PlayRow playRow;
        FlatButton bPlay, bInstall, bRemove, bPlain, bShortcut, bFolder, bLog;
        Card exeCard; DarkCombo cmbExe, cmbProxy; FlatButton bBrowse; InputBox txtArgs; Toggle tKeepIni;
        Card setCard; Card.Row rowNoMod, rowEnabled, rowEngine, rowFrames, rowTier, rowPreset, rowFlow, rowHud, rowSr, rowLog, rowAll, rowButtons;
        Toggle tEnabled, tAll; Segmented segEngine, segFrames; DarkCombo cTier, cPreset, cFlow, cHud, cSr, cLog;
        FlatButton bSave, bDefaults, bNotepad;
        Card logCard; TextBox logBox;
        readonly ToolTip tips = new ToolTip();
        ContextMenuStrip listMenu;

        public MainForm(string selectExe)
        {
            pendingSelect = selectExe;
            Text = Core.AppName;
            ClientSize = new Size(Theme.S(1260), Theme.S(840));
            MinimumSize = new Size(Theme.S(960), Theme.S(640));
            StartPosition = FormStartPosition.CenterScreen;
            BuildContent();
            Controls.Add(new Panel { Dock = DockStyle.Left, Width = 1, BackColor = Theme.Border });
            BuildSidebar();
            Core.LogLine += OnLogLine;
            Shown += delegate { Core.Log("DLSSG Launcher - mod " + Core.PackageVersion + " from " + Core.PackageDir); if (Core.IsAdmin()) Core.Log("Running as administrator."); LoadGames(); };
            FormClosing += delegate { Core.LogLine -= OnLogLine; SaveArgs(); Core.SaveSettings(); };
            ShowGame(null, true);
        }

        // ---------------------------------------------------------------- layout
        void BuildSidebar()
        {
            side = new Panel { Dock = DockStyle.Left, Width = Theme.S(340), BackColor = Theme.Side };
            brand = new BrandHeader { Sub = "Frame generation mod " + Core.PackageVersion };
            search = new InputBox(Glyphs.Search, "Search games");
            bAddFolder = new FlatButton("Add folder", Glyphs.Folder, BtnKind.Secondary);
            bAddExe = new FlatButton("Add EXE", Glyphs.Add, BtnKind.Secondary);
            bRescan = new FlatButton("", Glyphs.Refresh, BtnKind.Ghost);
            bLibraries = new FlatButton("", Glyphs.Library, BtnKind.Ghost);
            list = new GameListBox();
            sideStatus = new Label { AutoSize = false, ForeColor = Theme.Muted, BackColor = Theme.Side, Font = Theme.Small, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
            side.Controls.AddRange(new Control[] { brand, search, bAddFolder, bAddExe, bRescan, bLibraries, list, sideStatus });
            side.Layout += delegate
            {
                int p = Theme.S(16), w = side.ClientSize.Width - 2 * p, y = Theme.S(18), gap = Theme.S(6), sq = Theme.S(34);
                brand.SetBounds(p, y, w, Theme.S(44)); y += Theme.S(62);
                search.SetBounds(p, y, w, Theme.S(36)); y += Theme.S(46);
                int bw = (w - 2 * sq - 3 * gap) / 2;
                bAddFolder.SetBounds(p, y, bw, sq);
                bAddExe.SetBounds(p + bw + gap, y, bw, sq);
                bRescan.SetBounds(p + 2 * bw + 2 * gap, y, sq, sq);
                bLibraries.SetBounds(p + 2 * bw + 3 * gap + sq, y, sq, sq);
                y += sq + Theme.S(12);
                int bottom = side.ClientSize.Height - Theme.S(36);
                list.SetBounds(Theme.S(6), y, side.ClientSize.Width - Theme.S(12), Math.Max(Theme.S(60), bottom - y));
                sideStatus.SetBounds(p, bottom + Theme.S(7), w, Theme.S(22));
            };
            Controls.Add(side);

            tips.SetToolTip(bAddFolder, "Add a game by picking its install folder - the game EXE is found automatically");
            tips.SetToolTip(bAddExe, "Add a game by picking its EXE");
            tips.SetToolTip(bRescan, "Scan the game libraries again");
            tips.SetToolTip(bLibraries, "Game libraries: which folders are scanned");

            search.Box.TextChanged += delegate { FilterList(); };
            list.SelectedIndexChanged += delegate { if (!suppressList) SelectGame(list.SelectedItem as Game); };
            list.DoubleClick += delegate { if (current != null) Play(true); };
            list.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter && current != null) { Play(true); e.Handled = true; } };
            bAddFolder.Click += delegate { AddFolder(); };
            bAddExe.Click += delegate { AddExe(); };
            bRescan.Click += delegate { LoadGames(); };
            bLibraries.Click += delegate { Libraries(); };

            listMenu = new ContextMenuStrip { Renderer = new DarkMenuRenderer(), ShowImageMargin = false, Font = Theme.Body };
            var miPlay = listMenu.Items.Add("Play with DLSS mod");
            var miFolder = listMenu.Items.Add("Open game folder");
            listMenu.Items.Add(new ToolStripSeparator());
            var miHide = listMenu.Items.Add("Hide from list");
            foreach (ToolStripItem it in listMenu.Items) it.ForeColor = Theme.Text;
            miPlay.Click += delegate { Play(true); };
            miFolder.Click += delegate { if (current != null) Core.OpenInExplorer(current.Exe); };
            miHide.Click += delegate { HideCurrent(); };
            listMenu.Opening += delegate { miHide.Text = current != null && current.Source == "Added" ? "Remove from list" : "Hide from list"; };
            list.MouseDown += (s, e) =>
            {
                if (e.Button != MouseButtons.Right) return;
                int i = list.IndexFromPoint(e.Location);
                if (i < 0) return;
                list.SelectedIndex = i;
                listMenu.Show(list, e.Location);
            };
        }

        void BuildContent()
        {
            host = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
            stack = new VStack { Dock = DockStyle.Fill };
            empty = new EmptyState { Dock = DockStyle.Fill, Visible = false };
            empty.AddFolder.Click += delegate { AddFolder(); };
            empty.AddExe.Click += delegate { AddExe(); };
            host.Controls.Add(stack); host.Controls.Add(empty);
            Controls.Add(host);

            // --- hero: game, play, mod actions
            hero = new Card(null, null);
            heroHead = new HeroHeader();
            hero.AddFull(heroHead, null);
            acNotice = new Notice(NoticeKind.Danger, "Anti-cheat detected - online play can get you banned", "");
            acRow = hero.AddFull(acNotice, null);
            bPlay = new FlatButton("Play with DLSS mod", Glyphs.Play, BtnKind.Primary) { Big = true };
            playRow = new PlayRow(bPlay);
            hero.AddFull(playRow, null);
            bInstall = new FlatButton("Install mod", Glyphs.Download, BtnKind.Secondary);
            bRemove = new FlatButton("Remove mod", Glyphs.Delete, BtnKind.Danger);
            bPlain = new FlatButton("Play without mod", Glyphs.Game, BtnKind.Secondary);
            bShortcut = new FlatButton("Shortcut", Glyphs.Link, BtnKind.Secondary);
            bFolder = new FlatButton("Open folder", Glyphs.Folder, BtnKind.Ghost);
            bLog = new FlatButton("Mod log", Glyphs.Doc, BtnKind.Ghost);
            var actions = new ButtonRow();
            actions.Add(bInstall, bRemove, bPlain, bShortcut, bFolder, bLog);
            hero.AddFull(actions, null);
            stack.Controls.Add(hero);

            tips.SetToolTip(bPlay, "Checks the mod files next to the game EXE (copies what is missing or old), then starts the game");
            tips.SetToolTip(bShortcut, "A desktop shortcut that checks the mod and starts this game - with the game's own icon");
            tips.SetToolTip(bLog, "dlssg_sm86.log - set Log level to 2 to see whether the mod loads");
            bPlay.Click += delegate { Play(true); };
            bInstall.Click += delegate { Play(false); };
            bRemove.Click += delegate { RemoveMod(); };
            bPlain.Click += delegate { PlayPlain(); };
            bShortcut.Click += delegate { MakeShortcut(); };
            bFolder.Click += delegate { if (current != null) Core.OpenInExplorer(current.Exe); };
            bLog.Click += delegate { OpenLog(); };

            // --- DLSS settings
            setCard = new Card("DLSS settings", Glyphs.Settings);
            rowNoMod = setCard.AddFull(new Notice(NoticeKind.Info, "Install the mod to change this game's settings",
                "Press Play or Install mod. The settings are stored in dlssg_sm86.ini next to the game EXE. Shown below: the defaults a new install starts with."), null);
            tEnabled = new Toggle("Generate frames   (off = the game's own DLSS-G: no frame generation on RTX 30 / 20)");
            rowEnabled = setCard.AddRow("Frame generation", tEnabled, null, 0);
            segEngine = new Segmented("DLSS", "XeSS");
            rowEngine = setCard.AddRow("Engine", segEngine, "", Theme.S(260));
            segFrames = new Segmented("2X", "3X", "4X", "5X", "6X");
            rowFrames = setCard.AddRow("Multiplier", segFrames, "", Theme.S(440));
            cTier = Combo(IniField.Tier); rowTier = setCard.AddRow(IniField.Tier.Label, cTier, "Higher is faster; tier 1 gives exactly NVIDIA's image.", Theme.S(540));
            cPreset = Combo(IniField.Preset); rowPreset = setCard.AddRow(IniField.Preset.Label, cPreset, null, Theme.S(540));
            cFlow = Combo(IniField.Flow); rowFlow = setCard.AddRow(IniField.Flow.Label, cFlow, null, Theme.S(540));
            cHud = Combo(IniField.Hudless); rowHud = setCard.AddRow(IniField.Hudless.Label, cHud, null, Theme.S(540));
            cSr = Combo(IniField.SuperRes); rowSr = setCard.AddRow(IniField.SuperRes.Label, cSr, "RTX 30 / 20 only. M / L are slower than the game's own preset K on Quality / Balanced / DLAA.", Theme.S(540));
            cLog = Combo(IniField.LogLevel); rowLog = setCard.AddRow(IniField.LogLevel.Label, cLog, null, Theme.S(540));
            tAll = new Toggle("");
            rowAll = setCard.AddRow("Apply to", tAll, null, 0);
            bSave = new FlatButton("Save settings", Glyphs.Save, BtnKind.Primary);
            bDefaults = new FlatButton("Factory defaults", Glyphs.Refresh, BtnKind.Secondary);
            bNotepad = new FlatButton("All settings (INI file)", Glyphs.Edit, BtnKind.Ghost);
            var sb = new ButtonRow(); sb.Add(bSave, bDefaults, bNotepad);
            rowButtons = setCard.AddFull(sb, "");
            stack.Controls.Add(setCard);

            tEnabled.CheckedChanged += delegate { MarkDirty(); };
            segEngine.SelectedChanged += delegate { UpdateEngineRows(); MarkDirty(); };
            segFrames.SelectedChanged += delegate { if (!loading) customFrames = null; MarkDirty(); };
            foreach (var c in new[] { cTier, cPreset, cFlow, cHud, cSr, cLog }) c.SelectedIndexChanged += delegate { MarkDirty(); };
            bSave.Click += delegate { SaveIni(); };
            bDefaults.Click += delegate { Defaults(); };
            bNotepad.Click += delegate { OpenIni(); };

            // --- executable / advanced
            exeCard = new Card("Game executable", Glyphs.App);
            cmbExe = new DarkCombo();
            bBrowse = new FlatButton("Browse...", Glyphs.OpenFile, BtnKind.Secondary);
            var hb = new HBox(0, cmbExe, bBrowse) { Height = Theme.S(34) };
            exeCard.AddRow("Executable", hb, "The EXE that renders the game - the mod files go next to it. Pick another one if the game has several.", 0);
            txtArgs = new InputBox(null, "Optional, e.g. -dx12");
            exeCard.AddRow("Launch options", txtArgs, null, 0);
            cmbProxy = new DarkCombo();
            cmbProxy.SetChoices(new[] {
                new Choice("version.dll", "version.dll  (works for nearly every game)"),
                new Choice("winmm.dll", "winmm.dll"), new Choice("dbghelp.dll", "dbghelp.dll"), new Choice("dinput8.dll", "dinput8.dll"),
                new Choice("dxgi.dll", "dxgi.dll  (last resort)"), new Choice("d3d12.dll", "d3d12.dll  (last resort)") });
            exeCard.AddRow("Proxy DLL", cmbProxy, "If the game never loads the mod, try the next name in this list. Only one is ever installed.", Theme.S(360));
            tKeepIni = new Toggle("Keep this game's DLSS settings when the mod is updated") { Checked = true };
            exeCard.AddRow("Updates", tKeepIni, null, 0);
            stack.Controls.Add(exeCard);

            cmbExe.SelectedIndexChanged += delegate { if (!loading && current != null && cmbExe.Value != null) SetExe(current, cmbExe.Value); };
            bBrowse.Click += delegate { AddExe(); };
            txtArgs.Box.Leave += delegate { SaveArgs(); };

            // --- activity
            logCard = new Card("Activity", Glyphs.Doc);
            logBox = new TextBox
            {
                Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Theme.Card, ForeColor = Theme.Muted,
                Font = Theme.Mono, ScrollBars = ScrollBars.None, Height = Theme.S(150), WordWrap = true
            };
            logCard.AddFull(logBox, null);
            stack.Controls.Add(logCard);
        }

        static DarkCombo Combo(IniField f) { var c = new DarkCombo(); c.SetChoices(f.Choices); return c; }

        // ---------------------------------------------------------------- helpers
        void Ui(Action a)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke(a); } catch (InvalidOperationException) { }
        }

        void OnLogLine(string line) { Ui(() => { logBox.AppendText(line + "\r\n"); }); }

        void SetSideStatus(string s) { sideStatus.Text = s; }

        void SaveArgs()
        {
            if (current == null) return;
            string a = txtArgs.Text.Trim();
            if (a.Length == 0) Core.Settings.Args.Remove(current.Exe); else Core.Settings.Args[current.Exe] = a;
        }

        // ---------------------------------------------------------------- game list
        void LoadGames()
        {
            if (scanning) return;
            scanning = true;
            bRescan.Enabled = false;
            SetSideStatus("Scanning your game libraries...");
            bool first = games.Count == 0;
            var t = new Thread(() =>
            {
                if (first)
                {
                    List<Game> cached = null;
                    try { cached = Core.GamesFromCache(); } catch { }
                    if (cached != null && cached.Count > 0) Ui(() => ApplyGames(cached, false));
                }
                List<Game> result = null; Exception err = null;
                try { result = Core.ScanGames(n => Ui(() => SetSideStatus("Scanning   " + n))); }
                catch (Exception ex) { err = ex; }
                Ui(() =>
                {
                    scanning = false; bRescan.Enabled = true;
                    if (err != null) { Core.Log("Scan failed: " + err.Message); SetSideStatus("Scan failed - see Activity"); }
                    else { ApplyGames(result, true); Core.SaveSettings(); }
                });
            }) { IsBackground = true };
            t.Start();
        }

        void ApplyGames(List<Game> result, bool final)
        {
            string keepFolder = current != null ? current.Folder : null;
            games.Clear(); games.AddRange(result);
            Game pick = null;
            if (pendingSelect != null)
            {
                pick = games.FirstOrDefault(g => Core.IsUnder(pendingSelect, g.Folder));
                if (pick != null && File.Exists(pendingSelect) && !pick.Exe.Equals(pendingSelect, StringComparison.OrdinalIgnoreCase)) SetExeQuiet(pick, pendingSelect);
                if (final) pendingSelect = null;
            }
            if (pick == null && keepFolder != null) pick = games.FirstOrDefault(g => g.Folder.Equals(keepFolder, StringComparison.OrdinalIgnoreCase));
            if (pick == null && !string.IsNullOrEmpty(Core.Settings.LastExe)) pick = games.FirstOrDefault(g => Core.IsUnder(Core.Settings.LastExe, g.Folder));
            if (pick == null && games.Count > 0) pick = games[0];

            bool sameGame = pick != null && current != null && pick.Folder.Equals(current.Folder, StringComparison.OrdinalIgnoreCase) && pick.Exe.Equals(current.Exe, StringComparison.OrdinalIgnoreCase);
            current = pick;
            FilterList();
            ShowGame(pick, !(sameGame && dirty));
            SetSideStatus(games.Count == 1 ? "1 game" : games.Count + " games" + (final ? "" : "   \u00B7   refreshing..."));
            if (games.Count == 0)
            {
                empty.Title = "No games found yet";
                empty.Body = "Nothing was found in your game libraries. Add a game by its install folder or its EXE, or add a library folder with the library button.";
                empty.Invalidate();
            }
        }

        void FilterList()
        {
            string q = search.Text.Trim();
            var view = games.Where(g => q.Length == 0 || g.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            suppressList = true;
            try
            {
                list.BeginUpdate();
                list.Items.Clear();
                foreach (var g in view) list.Items.Add(g);
                list.EndUpdate();
                list.SelectedIndex = current != null ? view.IndexOf(current) : -1;
            }
            finally { suppressList = false; }
        }

        void SelectGame(Game g)
        {
            if (g == current || g == null) return;
            if (dirty && current != null && ini != null)
            {
                if (Dialogs.Ask(this, "Save your DLSS settings?", "You changed the settings of " + current.Name + " but did not save them.", "Save", "Discard")) SaveIni();
                dirty = false;
            }
            SaveArgs();
            ShowGame(g, true);
        }

        void HideCurrent()
        {
            if (current == null) return;
            var g = current;
            if (g.Source == "Added") { Core.Settings.ExtraGames.RemoveAll(f => Core.SamePath(f, g.Folder)); Core.Settings.ExeChoice.Remove(g.Folder); }
            else Core.Settings.HiddenGames.Add(g.Folder);
            Core.SaveSettings();
            int idx = games.IndexOf(g);
            games.Remove(g);
            current = games.Count == 0 ? null : games[Math.Min(idx, games.Count - 1)];
            FilterList();
            ShowGame(current, true);
            Core.Log((g.Source == "Added" ? "Removed " : "Hid ") + g.Name + " from the list. (Game libraries > Unhide brings hidden games back.)");
        }

        // ---------------------------------------------------------------- game page
        void ShowGame(Game g, bool reloadSettings)
        {
            current = g;
            if (g == null)
            {
                stack.Visible = false; empty.Visible = true;
                return;
            }
            empty.Visible = false; stack.Visible = true;
            Core.Settings.LastExe = g.Exe;

            heroHead.Game = g; heroHead.Invalidate();
            acRow.Visible = g.AntiCheat != null;
            acNotice.Body = (g.AntiCheat ?? "") + " Anti-cheat treats proxy DLLs such as version.dll as cheating. Use the mod only offline / in single-player.";
            acNotice.Invalidate();
            var k = g.State.Kind;
            bInstall.Text = k == ModKind.Installed ? "Repair mod" : k == ModKind.Outdated ? "Update mod" : "Install mod";
            bRemove.Enabled = k != ModKind.None;
            if (!busy)
                playRow.Status.Text = k == ModKind.Installed ? "The mod is ready. Play checks it, then starts the game."
                    : k == ModKind.Outdated ? "Play updates the mod to " + Core.PackageVersion + ", then starts the game."
                    : "Play copies the mod next to the game EXE, then starts the game.";

            loading = true;
            try
            {
                var choices = g.Candidates.Select(c =>
                {
                    string rel = Core.IsUnder(c, g.Folder) ? c.Substring(g.Folder.TrimEnd('\\').Length + 1) : c;
                    long mb = 0; try { mb = new FileInfo(c).Length / 1048576; } catch { }
                    return new Choice(c, rel + "    (" + mb + " MB)");
                }).ToList();
                cmbExe.SetChoices(choices);
                cmbExe.SelectValue(g.Exe);
                string a; Core.Settings.Args.TryGetValue(g.Exe, out a);
                txtArgs.Text = a ?? "";
                cmbProxy.SelectValue(g.State.Ours.Count > 0 ? g.State.Ours[0] : "version.dll");
            }
            finally { loading = false; }

            if (reloadSettings) LoadSettingsCard();
            hero.PerformLayout(); exeCard.PerformLayout(); stack.PerformLayout();
        }

        void RefreshCurrent()
        {
            if (current == null) return;
            Core.Refresh(current);
            list.Invalidate();
            ShowGame(current, !dirty || ini == null);
        }

        void SetExeQuiet(Game g, string exe)
        {
            if (!g.Candidates.Contains(exe, StringComparer.OrdinalIgnoreCase)) g.Candidates.Insert(0, exe);
            g.Exe = exe;
            Core.Settings.ExeChoice[g.Folder] = exe;
            Core.Settings.LastExe = exe;
            Core.Refresh(g);
        }

        void SetExe(Game g, string exe)
        {
            if (g.Exe.Equals(exe, StringComparison.OrdinalIgnoreCase)) return;
            SaveArgs();
            dirty = false;
            SetExeQuiet(g, exe);
            Core.SaveSettings();
            list.Invalidate();
            ShowGame(g, true);
        }

        // ---------------------------------------------------------------- DLSS settings
        void LoadSettingsCard()
        {
            loading = true;
            try
            {
                dirty = false; ini = null; customFrames = null;
                string path = Path.Combine(current.ExeDir, "dlssg_sm86.ini");
                IniFile src = null;
                if (File.Exists(path)) { try { ini = IniFile.Load(path); } catch (Exception ex) { Core.Log("Cannot read " + path + ": " + ex.Message); } }
                try { src = ini ?? IniFile.Load(Path.Combine(Core.PackageDir, "dlssg_sm86.ini")); } catch { }
                bool has = ini != null;
                rowNoMod.Visible = !has;
                foreach (Control c in new Control[] { tEnabled, segEngine, segFrames, cTier, cPreset, cFlow, cHud, cSr, cLog, bSave, bDefaults }) c.Enabled = has;

                Func<IniField, string> get = f => { string v = src == null ? null : src.Get(f.Section, f.Key); return string.IsNullOrEmpty(v) ? f.Default : v; };
                tEnabled.Checked = get(IniField.Enabled) != "0";
                segEngine.SelectedIndex = get(IniField.Engine).Equals("XeSS", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                string fr = get(IniField.Frames); int n;
                if (int.TryParse(fr, out n) && n >= 1 && n <= 5) segFrames.SelectedIndex = n - 1;
                else { segFrames.SelectedIndex = -1; customFrames = fr; }
                cTier.SelectValue(get(IniField.Tier)); cPreset.SelectValue(get(IniField.Preset));
                cFlow.SelectValue(get(IniField.Flow)); cHud.SelectValue(get(IniField.Hudless));
                cSr.SelectValue(get(IniField.SuperRes)); cLog.SelectValue(get(IniField.LogLevel));

                otherInstalls = has ? Core.FindModInstalls(current.Folder).Where(d => !Core.SamePath(d, current.ExeDir)).ToList() : new List<string>();
                rowAll.Visible = otherInstalls.Count > 0;
                tAll.Text = "Also save to the other install(s) in this game:  " + string.Join(", ", otherInstalls.Select(d =>
                {
                    string rel = Core.IsUnder(d, current.Folder) ? d.Substring(current.Folder.TrimEnd('\\').Length).TrimStart('\\') : d;
                    return rel.Length == 0 ? "(game folder)" : rel + "\\";
                }));
                tAll.Checked = true;
                UpdateEngineRows();
                bSave.Enabled = false;
                SetSaveStatus(has ? "Saved in " + path : "", Theme.Muted);
            }
            finally { loading = false; }
            setCard.PerformLayout();
        }

        void UpdateEngineRows()
        {
            bool x = segEngine.SelectedIndex == 1;
            rowTier.Visible = rowPreset.Visible = !x;
            rowFlow.Visible = rowHud.Visible = x;
            segFrames.Disabled = x ? new[] { false, false, false, true, true } : null;
            if (x && segFrames.SelectedIndex > 2) segFrames.SelectedIndex = 2;
            segFrames.Invalidate();
            rowEngine.Hint.Text = x ? "Intel's XeSS-FG model on this mod's own runner. 2X to 4X." : "NVIDIA's DLSS-G model on kernels tuned for RTX 30 / RTX 20 (default).";
            rowFrames.Hint.Text = customFrames != null
                ? "The INI has MaxGeneratedFrames=" + customFrames + ", which is kept unless you pick a multiplier."
                : "Frames shown per rendered frame. 4X is the default" + (x ? "; 5X / 6X need the DLSS engine." : ", 6X the maximum.") + " The game's own menu can still choose less.";
            setCard.PerformLayout();
        }

        void MarkDirty()
        {
            if (loading || ini == null) return;
            dirty = true;
            bSave.Enabled = true;
            SetSaveStatus("Unsaved changes", Theme.Warn);
        }

        void SetSaveStatus(string text, Color c)
        {
            rowButtons.Hint.Text = text; rowButtons.Hint.ForeColor = c;
            setCard.PerformLayout();
        }

        void Defaults()
        {
            tEnabled.Checked = true; segEngine.SelectedIndex = 0; customFrames = null; segFrames.SelectedIndex = 2;
            cTier.SelectValue(IniField.Tier.Default); cPreset.SelectValue(IniField.Preset.Default);
            cFlow.SelectValue(IniField.Flow.Default); cHud.SelectValue(IniField.Hudless.Default);
            cSr.SelectValue(IniField.SuperRes.Default); cLog.SelectValue(IniField.LogLevel.Default);
            UpdateEngineRows(); MarkDirty();
        }

        bool SaveIni()
        {
            if (ini == null || current == null) return false;
            bool xess = segEngine.SelectedIndex == 1;
            var want = new List<KeyValuePair<IniField, string>> {
                new KeyValuePair<IniField, string>(IniField.Enabled, tEnabled.Checked ? "1" : "0"),
                new KeyValuePair<IniField, string>(IniField.Engine, xess ? "XeSS" : "DLSS"),
                new KeyValuePair<IniField, string>(IniField.Frames, segFrames.SelectedIndex >= 0 ? (segFrames.SelectedIndex + 1).ToString() : customFrames),
                new KeyValuePair<IniField, string>(IniField.Tier, cTier.Value), new KeyValuePair<IniField, string>(IniField.Preset, cPreset.Value),
                new KeyValuePair<IniField, string>(IniField.Flow, cFlow.Value), new KeyValuePair<IniField, string>(IniField.Hudless, cHud.Value),
                new KeyValuePair<IniField, string>(IniField.SuperRes, cSr.Value), new KeyValuePair<IniField, string>(IniField.LogLevel, cLog.Value) };
            int frames;
            if (xess && int.TryParse(want[2].Value, out frames) && frames > 3)
            {
                Dialogs.Warn(this, "XeSS goes up to 4X", "With 5X or 6X frame generation would not start. Pick 4X or less, or the DLSS engine.");
                return false;
            }
            var targets = new List<string> { ini.Path };
            if (rowAll.Visible && tAll.Checked) targets.AddRange(otherInstalls.Select(d => Path.Combine(d, "dlssg_sm86.ini")));
            try
            {
                foreach (var t in targets)
                {
                    var f = t == ini.Path ? ini : IniFile.Load(t);
                    var changes = new List<string>();
                    foreach (var kv in want)
                    {
                        if (kv.Value == null) continue;
                        string old = f.Get(kv.Key.Section, kv.Key.Key);
                        if (old == null && kv.Value.Equals(kv.Key.Default, StringComparison.OrdinalIgnoreCase)) continue;
                        if (!string.Equals(old, kv.Value, StringComparison.Ordinal)) { f.Set(kv.Key.Section, kv.Key.Key, kv.Value); changes.Add(kv.Key.Key + "=" + kv.Value); }
                    }
                    if (changes.Count > 0) { f.Save(); Core.Log("Saved " + t + ":  " + string.Join(", ", changes)); }
                    else Core.Log("No changes for " + t);
                }
            }
            catch (UnauthorizedAccessException ex) { HandleError(new NeedsAdminException(Path.GetDirectoryName(ini.Path)), ex.Message); return false; }
            catch (Exception ex) { Dialogs.Error(this, "Could not save the settings", ex.Message); return false; }
            dirty = false; bSave.Enabled = false;
            try { ini = IniFile.Load(ini.Path); } catch { }
            bool running = Core.IsRunning(current.Exe);
            SetSaveStatus(running ? "Saved. The game is running - restart it to apply the new settings." : "Saved. The new settings apply the next time the game starts.", Theme.Accent);
            return true;
        }

        void OpenIni()
        {
            if (current == null) return;
            string p = ini != null ? ini.Path : Path.Combine(Core.PackageDir, "dlssg_sm86.ini");
            if (ini == null && !Dialogs.Ask(this, "Open the default settings?", "The mod is not installed in this game yet. Open the INI new installs start with instead?\n\n" + p, "Open", "Cancel")) return;
            try { Core.OpenText(p); } catch (Exception ex) { Dialogs.Error(this, "Could not open the INI", ex.Message); }
        }

        // ---------------------------------------------------------------- actions
        void SetBusy(bool on, string text)
        {
            busy = on;
            foreach (Control c in new Control[] { bPlay, bInstall, bRemove, bPlain, cmbExe, cmbProxy, bBrowse, bSave, bDefaults }) c.Enabled = !on;
            if (!on && current != null) { bRemove.Enabled = current.State.Kind != ModKind.None; bSave.Enabled = dirty; bDefaults.Enabled = ini != null; }
            list.Enabled = !on;
            playRow.Bar.Value = -1;
            playRow.Bar.Visible = on;
            if (text != null) playRow.Status.Text = text;
            playRow.PerformLayout();
            UseWaitCursor = on;
        }

        void RunBusy(string text, Action work, Action after)
        {
            SetBusy(true, text);
            var t = new Thread(() =>
            {
                Exception err = null;
                try { work(); } catch (Exception ex) { err = ex; }
                Ui(() =>
                {
                    SetBusy(false, null);
                    if (err != null) HandleError(err, null);
                    if (after != null) after();
                    RefreshCurrent();
                });
            }) { IsBackground = true };
            t.Start();
        }

        bool ConfirmFromWorker(string title, string text)
        {
            return (bool)Invoke(new Func<bool>(() => Dialogs.Ask(this, title, text, "Replace", "Cancel")));
        }

        void ProgressFromWorker(string text, int pct)
        {
            Ui(() => { playRow.Status.Text = text + "   " + pct + "%"; playRow.Bar.Value = pct; });
        }

        void HandleError(Exception ex, string detail)
        {
            Core.Log("ERROR: " + ex.Message.Replace("\n", " "));
            if (ex is NeedsAdminException || ex is UnauthorizedAccessException)
            {
                if (Core.IsAdmin())
                    Dialogs.Error(this, "Cannot write to the game folder", (detail ?? ex.Message) + "\n\nCheck that the folder is not read-only and that the game is closed.");
                else if (Dialogs.Ask(this, "Administrator permission needed", ex.Message + "\n\nRestart DLSSG Launcher as administrator and try again?", "Restart as admin", "Cancel"))
                    Program.RestartElevated(current != null ? "--select \"" + current.Exe + "\"" : "", this);
                return;
            }
            Dialogs.Error(this, "Something went wrong", ex.Message);
        }

        void Play(bool start)
        {
            if (current == null || busy) return;
            var g = current;
            SaveArgs();
            if (dirty && ini != null && Dialogs.Ask(this, "Save your DLSS settings first?", "You changed the settings but did not save them.", "Save", "Don't save")) SaveIni();
            if (!Program.ConfirmAntiCheat(this, g)) return;
            string proxy = cmbProxy.Value ?? "version.dll";
            bool overwrite = !tKeepIni.Checked, ok = false;
            RunBusy(start ? "Checking the mod..." : "Installing the mod...", () =>
            {
                ok = Core.Install(g, proxy, overwrite, ConfirmFromWorker, ProgressFromWorker);
            }, () =>
            {
                if (!ok) return;
                if (start)
                {
                    try { Core.StartGame(g); playRow.Status.Text = "Started " + g.Name + ".  Turn on frame generation in the game's graphics menu."; }
                    catch (Exception ex) { Dialogs.Error(this, "Could not start the game", ex.Message); }
                }
                else playRow.Status.Text = "The mod is installed. Start the game whenever you like.";
                Core.SaveSettings();
            });
        }

        void PlayPlain()
        {
            if (current == null) return;
            var g = current;
            SaveArgs();
            if (g.State.Kind != ModKind.None &&
                !Dialogs.Ask(this, "The mod is still installed", "Its files are still next to the game EXE, so the game loads the mod anyway. Use \"Remove mod\" for a run without it.\n\nStart the game anyway?", "Start", "Cancel")) return;
            try { Core.StartGame(g); playRow.Status.Text = "Started " + g.Name + "."; }
            catch (Exception ex) { Dialogs.Error(this, "Could not start the game", ex.Message); }
        }

        void RemoveMod()
        {
            if (current == null || busy) return;
            var g = current;
            var items = Core.RemovalList(g.ExeDir);
            if (items.Count == 0) { Dialogs.Info(this, "Nothing to remove", "The mod is not installed next to " + Path.GetFileName(g.Exe) + "."); return; }
            if (Core.IsRunning(g.Exe)) { Dialogs.Warn(this, "The game is running", "Close the game completely, then remove the mod."); return; }
            if (!Dialogs.AskDanger(this, "Remove the mod from " + g.Name + "?",
                "Deletes: " + string.Join(", ", items) + "\nFrom: " + g.ExeDir + "\n\nFiles the mod replaced are put back. Your DLSS settings for this game are deleted too.", "Remove", "Cancel")) return;
            dirty = false;
            RunBusy("Removing the mod...", () => Core.Uninstall(g.ExeDir), () => { playRow.Status.Text = "The mod was removed from this game."; });
        }

        void MakeShortcut()
        {
            if (current == null) return;
            string name = Dialogs.Input(this, "Desktop shortcut", "Name of the shortcut on your desktop. It checks the mod, then starts the game - no launcher window.", Core.DefaultShortcutName(current));
            if (string.IsNullOrWhiteSpace(name)) return;
            try { Core.CreateGameShortcut(current, name); Dialogs.Success(this, "Shortcut created", "\"" + name.Trim() + "\" is on your desktop."); }
            catch (Exception ex) { Dialogs.Error(this, "Could not create the shortcut", ex.Message); }
        }

        void OpenLog()
        {
            if (current == null) return;
            string p = Path.Combine(current.ExeDir, @"dlssg_sm86\logs\dlssg_sm86.log");
            if (File.Exists(p)) { try { Core.OpenText(p); } catch (Exception ex) { Dialogs.Error(this, "Could not open the log", ex.Message); } }
            else Dialogs.Info(this, "No log yet", "The log appears after the game has run with the mod:\n" + p + "\n\nSet Log level to 2 in DLSS settings to see whether the mod loads (look for runtime_redirect).");
        }

        void AddFolder()
        {
            string start = current != null ? current.Folder : Directory.Exists(@"C:\Games") ? @"C:\Games" : null;
            string f = FolderPicker.Pick(this, "Choose the game's install folder", start);
            if (string.IsNullOrEmpty(f)) return;
            f = f.TrimEnd('\\');
            var existing = games.FirstOrDefault(x => Core.SamePath(x.Folder, f));
            if (existing != null) { SelectInList(existing); return; }
            var parent = games.FirstOrDefault(x => Core.IsUnder(f, x.Folder));
            Cursor = Cursors.WaitCursor;
            try
            {
                if (parent != null)
                {
                    var best = Core.RankExes(f);
                    if (best.Count > 0) { foreach (var b in best.AsEnumerable().Reverse()) if (!parent.Candidates.Contains(b, StringComparer.OrdinalIgnoreCase)) parent.Candidates.Insert(0, b); SetExe(parent, best[0]); }
                    SelectInList(parent);
                    return;
                }
                var g = Core.NewGame(f, Core.DisplayName(f), "Added");
                if (g == null) { Dialogs.Warn(this, "No game EXE found", "There is no .exe in\n" + f + "\n\nUse \"Add EXE\" to pick it yourself."); return; }
                AddGame(g);
                Core.Log("Added " + g.Name + " - detected " + g.Exe + (g.Candidates.Count > 1 ? "  (" + (g.Candidates.Count - 1) + " other EXEs in the Executable list)" : ""));
            }
            finally { Cursor = Cursors.Default; }
        }

        void AddExe()
        {
            using (var d = new OpenFileDialog { Title = "Select the game EXE", Filter = "Game executable (*.exe)|*.exe" })
            {
                string start = current != null ? current.ExeDir : Directory.Exists(@"C:\Games") ? @"C:\Games" : null;
                if (start != null) d.InitialDirectory = start;
                if (d.ShowDialog(this) != DialogResult.OK) return;
                string exe = d.FileName;
                var parent = games.FirstOrDefault(x => Core.IsUnder(exe, x.Folder));
                if (parent != null) { SetExe(parent, exe); SelectInList(parent); return; }
                string folder = Path.GetDirectoryName(exe);
                Core.Settings.ExeChoice[folder] = exe;
                var g = Core.NewGame(folder, Core.DisplayName(exe), "Added");
                if (g == null) return;
                AddGame(g);
                Core.Log("Added " + exe);
            }
        }

        void AddGame(Game g)
        {
            if (!Core.Settings.ExtraGames.Any(x => Core.SamePath(x, g.Folder))) Core.Settings.ExtraGames.Add(g.Folder);
            Core.Settings.HiddenGames.RemoveAll(x => Core.SamePath(x, g.Folder));
            Core.Settings.ExeChoice[g.Folder] = g.Exe;
            Core.Refresh(g);
            games.Add(g);
            games.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
            Core.SaveSettings();
            SelectInList(g);
        }

        void SelectInList(Game g)
        {
            if (search.Text.Length > 0 && g.Name.IndexOf(search.Text, StringComparison.OrdinalIgnoreCase) < 0) search.Text = "";
            SelectGame(g);
            FilterList();
        }

        void Libraries()
        {
            using (var f = new LibrariesForm())
            {
                f.ShowDialog(this);
                if (f.Changed) { Core.SaveSettings(); LoadGames(); }
            }
        }
    }

    // ------------------------------------------------------------------------ game libraries dialog
    class LibrariesForm : DarkForm
    {
        public bool Changed;
        readonly ListBox lb = new ListBox();
        readonly Toggle tAuto;
        readonly FlatButton bAdd, bRemove, bUnhide, bDone;

        public LibrariesForm()
        {
            Text = "Game libraries";
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            int w = Theme.S(680), pad = Theme.S(24);
            var title = new Label { Text = "Game libraries", Font = Theme.H2, ForeColor = Theme.Text, AutoSize = false };
            title.SetBounds(pad, pad, w - 2 * pad, Theme.S(28));
            var sub = new Label { Text = "Every folder inside these folders counts as one game. Games found elsewhere can be added with Add folder / Add EXE.", ForeColor = Theme.Muted, AutoSize = false };
            sub.SetBounds(pad, pad + Theme.S(32), w - 2 * pad, Theme.S(40));
            tAuto = new Toggle("Find Steam, Epic, GOG, Xbox and <drive>:\\Games folders automatically") { Checked = Core.Settings.AutoDetect };
            tAuto.SetBounds(pad, pad + Theme.S(78), w - 2 * pad, Theme.S(28));
            lb.SetBounds(pad, pad + Theme.S(118), w - 2 * pad, Theme.S(260));
            lb.DrawMode = DrawMode.OwnerDrawFixed; lb.ItemHeight = Theme.S(40); lb.BorderStyle = BorderStyle.None;
            lb.BackColor = Theme.Card; lb.ForeColor = Theme.Text; lb.IntegralHeight = false;
            lb.HandleCreated += delegate { Native.DarkScroll(lb); };
            lb.DrawItem += DrawRoot;
            int by = pad + Theme.S(394);
            bAdd = new FlatButton("Add folder...", Glyphs.Add, BtnKind.Secondary);
            bRemove = new FlatButton("Remove", Glyphs.Delete, BtnKind.Danger);
            bUnhide = new FlatButton("", Glyphs.Hide, BtnKind.Ghost);
            bDone = new FlatButton("Done", null, BtnKind.Primary);
            int x = pad;
            foreach (var b in new[] { bAdd, bRemove, bUnhide }) { b.BackColor = Theme.Bg; Controls.Add(b); }
            bDone.BackColor = Theme.Bg;
            Controls.AddRange(new Control[] { title, sub, tAuto, lb, bDone });
            ClientSize = new Size(w, by + Theme.S(34) + pad);
            // (Control.Visible reads false until the form is shown, so the wanted state is tracked here)
            bool unhideOn = false;
            Action layoutButtons = () =>
            {
                int bx = pad;
                foreach (var b in new[] { bAdd, bRemove, bUnhide })
                {
                    bool on = b != bUnhide || unhideOn;
                    b.Visible = on;
                    if (!on) continue;
                    int bw = b.PreferredWidth; b.SetBounds(bx, by, bw, Theme.S(34)); bx += bw + Theme.S(8);
                }
                bDone.SetBounds(w - pad - Theme.S(100), by, Theme.S(100), Theme.S(34));
            };
            Action refresh = () =>
            {
                lb.Items.Clear();
                foreach (var r in Core.GetLibraryRoots()) lb.Items.Add(r);
                int hidden = Core.Settings.HiddenGames.Count;
                bUnhide.Text = "Unhide " + hidden + " hidden game" + (hidden == 1 ? "" : "s");
                unhideOn = hidden > 0;
                layoutButtons();
            };
            refresh();
            tAuto.CheckedChanged += delegate { Core.Settings.AutoDetect = tAuto.Checked; Changed = true; refresh(); };
            bAdd.Click += delegate
            {
                string f = FolderPicker.Pick(this, "Choose a folder that contains games", null);
                if (string.IsNullOrEmpty(f)) return;
                if (!Core.Settings.ScanRoots.Any(r => Core.SamePath(r, f))) { Core.Settings.ScanRoots.Add(f.TrimEnd('\\')); Changed = true; }
                refresh();
            };
            bRemove.Click += delegate
            {
                var r = lb.SelectedItem as LibraryRoot;
                if (r == null) return;
                if (r.Auto) { Dialogs.Info(this, "Found automatically", "This folder was found automatically. Turn off automatic detection above to stop scanning it."); return; }
                Core.Settings.ScanRoots.RemoveAll(p => Core.SamePath(p, r.Path)); Changed = true; refresh();
            };
            bUnhide.Click += delegate { Core.Settings.HiddenGames.Clear(); Changed = true; refresh(); };
            bDone.Click += delegate { Close(); };
        }

        void DrawRoot(object sender, DrawItemEventArgs e)
        {
            var g = e.Graphics; var r = e.Bounds;
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (var b = new SolidBrush(sel ? Theme.Hover : Theme.Card)) g.FillRectangle(b, r);
            if (e.Index < 0) return;
            var root = (LibraryRoot)lb.Items[e.Index];
            Theme.Prep(g);
            Theme.DrawGlyph(g, Glyphs.Folder, Theme.Glyph, root.Auto ? Theme.Muted : Theme.Accent, new Rectangle(r.X + Theme.S(8), r.Y, Theme.S(24), r.Height));
            string tag = root.Auto ? "Auto  \u00B7  " + root.Label : "Library";
            var ts = TextRenderer.MeasureText(tag, Theme.Small, Size.Empty, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, root.Path, Theme.Body, new Rectangle(r.X + Theme.S(40), r.Y, r.Width - Theme.S(60) - ts.Width, r.Height), Theme.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.PathEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            TextRenderer.DrawText(g, tag, Theme.Small, new Rectangle(r.Right - ts.Width - Theme.S(12), r.Y, ts.Width + 2, r.Height), Theme.Muted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        }
    }
}
