// Picker.cs – "Choose what to show" for the system monitor.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace BatteryWidget
{
    public class PickerForm : ThemedForm
    {
        readonly SystemView W;
        readonly Label _status;
        readonly Button _action;
        readonly TextBox _search;
        readonly TreeView _tree;
        readonly ListBox _list;
        readonly TextBox _eLabel, _eMax, _eWarn, _eCrit;
        readonly ComboBox _eIcon;
        readonly Button _eColor, _eRemove;
        readonly Panel _editor;
        Dictionary<string, Metric> _ms = new Dictionary<string, Metric>();
        string _treeKeys = "";
        readonly Dictionary<string, TreeNode> _leaf = new Dictionary<string, TreeNode>();
        bool _busy;
        string _act = "";

        public PickerForm(SystemView w) : base(Themes.Resolve(w.C.Str("theme", "Twilight") == "Auto" ? ((Themes.WindowsLight(false) ?? false) ? "Light" : "Dark") : w.C.Str("theme", "Twilight"), w.C.Str("accent", ""), false, true))
        {
            W = w;
            Text = "Choose what to show – System monitor";
            ClientSize = new Size(960, 660);
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(20, 14, 20, 12) };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(Lbl("Choose what to show", false, 16, true));
            var srow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true, Margin = new Padding(0, 4, 0, 8) };
            srow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); srow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _status = Lbl("", true); _status.MaximumSize = new Size(740, 0);
            _action = Btn("", true); _action.Visible = false; _action.Click += (s, e) => DoAction();
            srow.Controls.Add(_status, 0, 0); srow.Controls.Add(_action, 1, 0);
            root.Controls.Add(srow);

            var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 540, BackColor = BgSolid };
            var left = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
            left.RowStyles.Add(new RowStyle(SizeType.AutoSize)); left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            _search = new TextBox { Dock = DockStyle.Fill, BackColor = CardSolid, ForeColor = ForeColor, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 0, 0, 6) };
            _search.TextChanged += (s, e) => RebuildTree();
            left.Controls.Add(_search);
            _tree = new TreeView { Dock = DockStyle.Fill, CheckBoxes = true, BackColor = CardSolid, ForeColor = ForeColor, BorderStyle = BorderStyle.None, LineColor = BorderSolid,
                ShowLines = false, FullRowSelect = true, ItemHeight = (int)(22 * WidgetWindow.Dpi), ImageList = new ImageList { ImageSize = new Size(18, 18), ColorDepth = ColorDepth.Depth32Bit } };
            foreach (var ic in Draw.SysIcons) _tree.ImageList.Images.Add(ic[0], Draw.Preview(true, ic[0], 0, Color.FromArgb(255, T.Text), 18));
            _tree.AfterCheck += OnTick;
            left.Controls.Add(_tree);
            split.Panel1.Controls.Add(left);

            var right = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, Padding = new Padding(10, 0, 0, 0) };
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize)); right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            right.Controls.Add(Lbl("Shown in the widget", false, 11, true));
            right.Controls.Add(Lbl("Drag to change the order. Pick one to edit it.", true));
            _list = new ListBox { Dock = DockStyle.Fill, BackColor = CardSolid, ForeColor = ForeColor, BorderStyle = BorderStyle.None, DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = (int)(26 * WidgetWindow.Dpi), AllowDrop = true };
            _list.DrawItem += DrawListItem;
            _list.SelectedIndexChanged += (s, e) => LoadEditor();
            int dragFrom = -1;
            _list.MouseDown += (s, e) => { dragFrom = _list.IndexFromPoint(e.Location); };
            _list.MouseMove += (s, e) => { if (e.Button == MouseButtons.Left && dragFrom >= 0) _list.DoDragDrop(dragFrom, DragDropEffects.Move); };
            _list.DragOver += (s, e) => e.Effect = DragDropEffects.Move;
            _list.DragDrop += (s, e) =>
            {
                int to = _list.IndexFromPoint(_list.PointToClient(new Point(e.X, e.Y)));
                if (to < 0) to = _list.Items.Count - 1;
                int from = (int)e.Data.GetData(typeof(int));
                if (from < 0 || from == to) return;
                var items = W.Items; var it = items[from]; items.RemoveAt(from); items.Insert(to, it);
                W.C["items"] = items.Cast<object>().ToList(); Commit(); RefreshList(Cfg.S(it, "key"));
                dragFrom = -1;
            };
            right.Controls.Add(_list);
            _editor = Card(); _editor.Height = 250; _editor.Dock = DockStyle.Fill;
            int y = 8;
            Func<string, Control, Control> row = (label, ctl) =>
            {
                var l = Lbl(label, true); l.Location = new Point(10, y + 4); _editor.Controls.Add(l);
                ctl.Location = new Point(110, y); ctl.Width = 250; _editor.Controls.Add(ctl); y += 34; return ctl;
            };
            _eLabel = (TextBox)row("Label", new TextBox { BackColor = BgSolid, ForeColor = ForeColor, BorderStyle = BorderStyle.FixedSingle });
            _eIcon = (ComboBox)row("Icon", new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = BgSolid, ForeColor = ForeColor });
            _eIcon.Items.Add("Automatic"); foreach (var ic in Draw.SysIcons) _eIcon.Items.Add(ic[1]);
            _eMax = (TextBox)row("Ring full at", new TextBox { BackColor = BgSolid, ForeColor = ForeColor, BorderStyle = BorderStyle.FixedSingle });
            _eWarn = (TextBox)row("Warn at", new TextBox { BackColor = BgSolid, ForeColor = ForeColor, BorderStyle = BorderStyle.FixedSingle });
            _eCrit = (TextBox)row("Critical at", new TextBox { BackColor = BgSolid, ForeColor = ForeColor, BorderStyle = BorderStyle.FixedSingle });
            _eColor = Btn("Color…"); _eColor.Location = new Point(110, y); _eColor.MouseUp += (s, e) => PickColor(e.Button == MouseButtons.Right);
            new ToolTip().SetToolTip(_eColor, "Icon and ring color · right-click for automatic");
            _eRemove = Btn("Remove"); _eRemove.Location = new Point(270, y); _eRemove.Click += (s, e) => RemoveCurrent();
            _editor.Controls.Add(_eColor); _editor.Controls.Add(_eRemove);
            foreach (var tb in new[] { _eLabel, _eMax, _eWarn, _eCrit }) tb.Leave += (s, e) => SaveEditor();
            _eIcon.SelectedIndexChanged += (s, e) => SaveEditor();
            right.Controls.Add(_editor);
            split.Panel2.Controls.Add(right);
            root.Controls.Add(split);

            var foot = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
            var sug = Btn("Suggested"); sug.Click += (s, e) =>
            {
                var keep = W.Items.ToDictionary(i => Cfg.S(i, "key"), i => i);
                W.C["items"] = Fmt.Suggest(_ms).Select(k => (object)(keep.ContainsKey(k) ? keep[k] : new Dictionary<string, object> { { "key", k } })).ToList();
                Commit(); _treeKeys = ""; RebuildTree(); RefreshList(null);
            };
            var clear = Btn("Clear"); clear.Click += (s, e) => { W.C["items"] = new List<object>(); Commit(); _treeKeys = ""; RebuildTree(); RefreshList(null); };
            var done = Btn("Done", true); done.Click += (s, e) => Close();
            foot.Controls.AddRange(new Control[] { sug, clear, done });
            root.Controls.Add(foot);
            Controls.Add(root);
            RefreshList(null);
        }

        public void OnMetrics(Dictionary<string, Metric> ms)
        {
            _ms = ms;
            string keys = string.Join("|", ms.Keys.OrderBy(k => k));
            if (keys != _treeKeys) { _treeKeys = keys; RebuildTree(); RefreshList(CurrentKey); }
            else
            {
                foreach (var kv in _leaf) { Metric m; if (ms.TryGetValue(kv.Key, out m)) kv.Value.Text = NodeText(m); }
                _list.Invalidate();
            }
            UpdateStatus();
        }

        static string Fmtv(Metric m) { if (m == null || !m.Value.HasValue) return "—"; var v = Fmt.Value(m); return (v[0] + " " + v[1]).Trim(); }
        static string NodeText(Metric m) => $"{m.Name}    {Fmtv(m)}";

        void UpdateStatus()
        {
            var st = W.Sampler?.Hub.Status ?? new Dictionary<string, string>();
            string lhm; st.TryGetValue("lhm", out lhm); lhm = lhm ?? "";
            string act = "", msg;
            if (lhm == "") msg = "Reading Windows' own values.";
            else if (lhm == "missing") msg = "The sensor library isn't included in this build, so only Windows' own values (load, memory, drives, network) are shown.";
            else if (lhm.Contains("no admin")) { msg = "Running without administrator rights: graphics card values work, but CPU temperature, power and the motherboard's fans need them."; act = "admin"; }
            else if (lhm.Contains("PawnIO")) { msg = "CPU temperatures, power and clocks, and the motherboard's fans and voltages, need the free PawnIO driver – signed and compatible with Windows' memory integrity, also used by LibreHardwareMonitor and Fan Control. Graphics card values work without it."; act = "pawnio"; }
            else if (lhm.Contains("not responding")) { msg = "PawnIO is installed, but the processor's sensors still don't answer. Restart Windows once (needed right after installing PawnIO). If it still says this, reinstall PawnIO."; act = "pawnio"; }
            else if (lhm.StartsWith("error")) { msg = "The sensor library didn't start: " + lhm; act = "reload"; }
            else if (lhm.StartsWith("on")) { var parts = lhm.Split('·'); msg = "All sensors available – " + (parts.Length > 1 ? parts[1].Trim() : "") + " from this PC, plus Windows' own values."; }
            else msg = "Starting the sensors…";
            if (_act == "pawnio-started" && act == "pawnio") { act = "reload"; msg = "When the PawnIO installer has finished, click Reload sensors."; }
            _status.Text = msg;
            var labels = new Dictionary<string, string> { { "admin", "Restart as administrator" }, { "pawnio", "Install PawnIO…" }, { "reload", "Reload sensors" } };
            _action.Visible = act != "";
            if (act != "") _action.Text = labels[act];
            if (!(_act == "pawnio-started" && act == "reload")) _act = act;
        }

        void DoAction()
        {
            if (_act == "admin") { Suite.EnableAdmin(W.Suite); return; }
            if (_act == "reload" || _act == "pawnio-started")
            {
                var hub = W.Sampler?.Hub;
                if (hub != null) new Thread(() => { lock (hub.Lock) hub.Lhm.Restart(); }) { IsBackground = true }.Start();
                _act = ""; _status.Text = "Reloading the sensors…"; W.RefreshNow(); return;
            }
            if (_act == "pawnio")
            {
                _action.Enabled = false; _status.Text = "Downloading the PawnIO installer…";
                new Thread(() =>
                {
                    string err = null;
                    try { Pawn.Install(); } catch (Exception ex) { err = ex.Message; }
                    BeginInvoke((Action)(() =>
                    {
                        _action.Enabled = true;
                        if (err != null) { _status.Text = "That didn't work: " + err; return; }
                        _act = "pawnio-started"; _action.Text = "Reload sensors";
                        _status.Text = "Follow the PawnIO installer. When it's done, click Reload sensors.";
                    }));
                }) { IsBackground = true }.Start();
            }
        }

        void RebuildTree()
        {
            _busy = true;
            _tree.BeginUpdate();
            _tree.Nodes.Clear(); _leaf.Clear();
            var chosen = new HashSet<string>(W.Items.Select(i => Cfg.S(i, "key")));
            string[] words = _search.Text.ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var groups = _ms.Values.GroupBy(m => m.Group).OrderBy(g => g.Key.EndsWith("(Windows)")).ToList();
            foreach (var g in groups)
            {
                var leaves = g.Where(m => words.All(w => $"{g.Key} {m.Name} {m.Kind} {m.Unit}".ToLowerInvariant().Contains(w))).ToList();
                if (leaves.Count == 0) continue;
                var gn = new TreeNode(g.Key) { NodeFont = new Font(Fonts.Ui, 9.75f, FontStyle.Bold), ForeColor = ForeColor };
                gn.ImageKey = gn.SelectedImageKey = Fmt.Icon(leaves[0]);
                foreach (var m in leaves)
                {
                    var ln = new TreeNode(NodeText(m)) { Tag = m.Key, Checked = chosen.Contains(m.Key), ImageKey = Fmt.Icon(m), SelectedImageKey = Fmt.Icon(m) };
                    gn.Nodes.Add(ln); _leaf[m.Key] = ln;
                }
                _tree.Nodes.Add(gn);
                if (words.Length > 0 || leaves.Any(m => chosen.Contains(m.Key)) || groups.Count <= 6 || g.Key.EndsWith("(Windows)")) gn.Expand();
            }
            _tree.EndUpdate();
            _busy = false;
        }

        void OnTick(object sender, TreeViewEventArgs e)
        {
            if (_busy || e.Node.Tag == null) return;
            string key = (string)e.Node.Tag;
            var items = W.Items;
            if (e.Node.Checked) { if (!items.Any(i => Cfg.S(i, "key") == key)) items.Add(new Dictionary<string, object> { { "key", key } }); }
            else items.RemoveAll(i => Cfg.S(i, "key") == key);
            W.C["items"] = items.Cast<object>().ToList();
            Commit();
            RefreshList(e.Node.Checked ? key : null);
        }

        void Commit() { W.C["first_run"] = false; W.Save(); W.Rebuild(); }

        string CurrentKey => _list.SelectedIndex >= 0 && _list.SelectedIndex < W.Items.Count ? Cfg.S(W.Items[_list.SelectedIndex], "key") : null;
        Dictionary<string, object> Current => _list.SelectedIndex >= 0 && _list.SelectedIndex < W.Items.Count ? W.Items[_list.SelectedIndex] : null;

        void RefreshList(string select)
        {
            _busy = true;
            _list.Items.Clear();
            var items = W.Items;
            foreach (var it in items) _list.Items.Add(Cfg.S(it, "key"));
            int idx = select != null ? items.FindIndex(i => Cfg.S(i, "key") == select) : -1;
            _list.SelectedIndex = idx >= 0 ? idx : items.Count > 0 ? 0 : -1;
            _busy = false;
            LoadEditor();
        }

        void DrawListItem(object s, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= W.Items.Count) return;
            var it = W.Items[e.Index];
            string key = Cfg.S(it, "key");
            Metric m; _ms.TryGetValue(key, out m);
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (var b = new SolidBrush(sel ? Color.FromArgb(255, T.Accent) : CardSolid)) e.Graphics.FillRectangle(b, e.Bounds);
            Draw.Hq(e.Graphics);
            string icon = Cfg.S(it, "icon") ?? (m != null ? Fmt.Icon(m) : "dot");
            Color ic = Cfg.S(it, "color") is string c ? Theme.C(c) : (sel ? Color.White : Color.FromArgb(255, T.Text));
            Draw.SysIcon(e.Graphics, icon, new RectangleF(e.Bounds.X + 6, e.Bounds.Y + 4, e.Bounds.Height - 8, e.Bounds.Height - 8), ic);
            string label = Cfg.S(it, "label") ?? (m != null ? Fmt.Label(m) : key.Split('|').Last());
            TextRenderer.DrawText(e.Graphics, label + "    " + Fmtv(m), Font, new Point(e.Bounds.X + e.Bounds.Height + 4, e.Bounds.Y + 4),
                sel ? (T.Accent.GetBrightness() > 0.6f ? Color.Black : Color.White) : ForeColor);
        }

        void LoadEditor()
        {
            if (_busy) return;
            var it = Current;
            _editor.Enabled = it != null;
            _busy = true;
            try
            {
                if (it == null) { foreach (var tb in new[] { _eLabel, _eMax, _eWarn, _eCrit }) tb.Text = ""; return; }
                Metric m; _ms.TryGetValue(Cfg.S(it, "key"), out m);
                _eLabel.Text = Cfg.S(it, "label") ?? "";
                SetCue(_eLabel, m != null ? Fmt.Label(m) : "");
                string icon = Cfg.S(it, "icon");
                int idx = icon == null ? 0 : Array.FindIndex(Draw.SysIcons, x => x[0] == icon) + 1;
                _eIcon.Items[0] = m != null ? $"Automatic ({Draw.SysIcons.First(x => x[0] == Fmt.Icon(m))[1]})" : "Automatic";
                _eIcon.SelectedIndex = Math.Max(0, idx);
                Tuple<double?, double?, double?, bool> lim = m != null && m.Value.HasValue ? Fmt.Limits(m, new Dictionary<string, object>(), W.Peaks.ContainsKey(m.Key) ? W.Peaks[m.Key] : 0) : null;
                string unit = m?.Unit ?? "";
                Func<double?, string> f = v => v == null ? "off" : (Math.Abs(v.Value) >= 10 ? v.Value.ToString("F0", CultureInfo.InvariantCulture) : v.Value.ToString("F2", CultureInfo.InvariantCulture)) + " " + unit;
                _eMax.Text = Cfg.N(it, "max")?.ToString(CultureInfo.InvariantCulture) ?? ""; SetCue(_eMax, "auto" + (lim?.Item1 != null ? $" ({f(lim.Item1)})" : ""));
                _eWarn.Text = Cfg.N(it, "warn")?.ToString(CultureInfo.InvariantCulture) ?? ""; SetCue(_eWarn, lim == null ? "off" : f(lim.Item2));
                _eCrit.Text = Cfg.N(it, "crit")?.ToString(CultureInfo.InvariantCulture) ?? ""; SetCue(_eCrit, lim == null ? "off" : f(lim.Item3));
                string col = Cfg.S(it, "color");
                _eColor.Image = Draw.Swatch(col != null ? Theme.C(col) : Color.FromArgb(255, T.Accent));
                _eColor.Text = col != null ? "Color…" : "Color… (automatic)";
                _eColor.TextImageRelation = TextImageRelation.ImageBeforeText;
            }
            finally { _busy = false; }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);
        static void SetCue(TextBox tb, string cue) { try { if (Native.OnWindows) SendMessage(tb.Handle, 0x1501, new IntPtr(1), cue); } catch { } }

        void SaveEditor()
        {
            if (_busy) return;
            var it = Current;
            if (it == null) return;
            Func<TextBox, double?> num = tb => { double v; return double.TryParse(tb.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : (double?)null; };
            Action<string, object> put = (k, v) => { if (v == null) it.Remove(k); else it[k] = v; };
            put("label", _eLabel.Text.Trim() == "" ? null : _eLabel.Text.Trim());
            put("icon", _eIcon.SelectedIndex <= 0 ? null : Draw.SysIcons[_eIcon.SelectedIndex - 1][0]);
            put("max", num(_eMax)); put("warn", num(_eWarn)); put("crit", num(_eCrit));
            Commit();
            _list.Invalidate();
        }

        void PickColor(bool reset)
        {
            var it = Current;
            if (it == null) return;
            if (reset) it.Remove("color");
            else
            {
                string cur = Cfg.S(it, "color");
                using (var dlg = new ColorDialog { Color = cur != null ? Theme.C(cur) : Color.FromArgb(255, T.Accent), FullOpen = true })
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
                    it["color"] = Theme.Hex(dlg.Color);
                }
            }
            Commit(); LoadEditor(); _list.Invalidate();
        }

        void RemoveCurrent()
        {
            string key = CurrentKey;
            if (key == null) return;
            var items = W.Items; items.RemoveAll(i => Cfg.S(i, "key") == key);
            W.C["items"] = items.Cast<object>().ToList();
            Commit();
            TreeNode n;
            if (_leaf.TryGetValue(key, out n)) { _busy = true; n.Checked = false; _busy = false; }
            RefreshList(null);
        }
    }
}
