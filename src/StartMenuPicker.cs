// "Add from Start menu": lists every program shortcut in the Start menu with its
// icon, so apps can be added by ticking boxes instead of hunting for files.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace AppLauncher
{
    internal sealed class StartMenuPicker : Form
    {
        private sealed class Entry
        {
            public string Name, Path;
            public int Icon = -1;
        }

        private readonly List<Entry> all = new List<Entry>();
        private readonly HashSet<string> chosen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<Entry, ListViewItem> shown = new Dictionary<Entry, ListViewItem>();
        private readonly Queue<Entry> iconQueue = new Queue<Entry>();

        private readonly float scale;
        private readonly int iconPx;
        private readonly ListView list;
        private readonly ImageList images;
        private readonly TextBox search;
        private readonly IconButton addButton;
        private readonly Label countLabel;
        private readonly Timer iconTimer;
        private bool filling;

        public readonly List<AppItem> Result = new List<AppItem>();

        public StartMenuPicker(Settings settings)
        {
            scale = Theme.ScaleOf(this);
            iconPx = S(32);

            AutoScaleMode = AutoScaleMode.None;
            Text = "Add apps from the Start menu";
            Font = new Font("Segoe UI", 9f);
            BackColor = Theme.PageBack;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(S(520), S(600));

            var title = new Label
            {
                Text = "Tick the apps you want in your list",
                Font = new Font("Segoe UI Semibold", 13f),
                ForeColor = Theme.Text,
                AutoSize = false,
                Bounds = new Rectangle(S(20), S(16), S(480), S(30)),
            };
            var hint = new Label
            {
                Text = "Apps already in your list are not shown.",
                ForeColor = Theme.Muted,
                AutoSize = false,
                Bounds = new Rectangle(S(20), S(46), S(480), S(20)),
            };

            search = new TextBox
            {
                Font = new Font("Segoe UI", 10.5f),
                Bounds = new Rectangle(S(20), S(78), S(480), S(28)),
            };
            search.TextChanged += delegate { Fill(); };

            search.HandleCreated += delegate { NativeMethods.SendMessage(search.Handle, 0x1501, (IntPtr)1, "Search apps..."); };
            Controls.Add(search);

            images = new ImageList { ColorDepth = ColorDepth.Depth32Bit, ImageSize = new Size(iconPx, iconPx) };

            var card = new Card { Bounds = new Rectangle(S(20), S(118), S(480), S(410)) };
            list = new ListView
            {
                View = View.Details,
                HeaderStyle = ColumnHeaderStyle.None,
                CheckBoxes = true,
                FullRowSelect = true,
                HideSelection = false,
                MultiSelect = false,
                BorderStyle = BorderStyle.None,
                Font = new Font("Segoe UI", 10.5f),
                SmallImageList = images,
                Bounds = new Rectangle(S(2), S(2), S(476), S(406)),
            };
            list.Columns.Add("Name", S(440));
            list.ItemChecked += OnItemChecked;
            list.Resize += delegate { list.Columns[0].Width = Math.Max(50, list.ClientSize.Width - 4); };
            card.Controls.Add(list);

            countLabel = new Label
            {
                ForeColor = Theme.Muted,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Bounds = new Rectangle(S(20), S(546), S(200), S(34)),
            };
            addButton = new IconButton
            {
                Glyph = IconFont.Add,
                Primary = true,
                Bounds = new Rectangle(S(232), S(546), S(170), S(34)),
            };
            addButton.Click += delegate { Accept(); };
            var cancel = new IconButton
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Bounds = new Rectangle(S(412), S(546), S(88), S(34)),
            };
            CancelButton = cancel;

            Controls.AddRange(new Control[] { title, hint, card, countLabel, addButton, cancel });

            iconTimer = new Timer { Interval = 15 };
            iconTimer.Tick += delegate { LoadSomeIcons(); };

            FindShortcuts(settings);
            Fill();
            UpdateCount();
            iconTimer.Start();
        }

        private int S(int v)
        {
            return (int)Math.Round(v * scale);
        }

        // ---- Finding the shortcuts ------------------------------------------

        private void FindShortcuts(Settings settings)
        {
            var found = new List<Entry>();
            var names = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);

            foreach (Environment.SpecialFolder folder in new[] { Environment.SpecialFolder.CommonPrograms, Environment.SpecialFolder.Programs })
            {
                string root = Environment.GetFolderPath(folder);
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;
                Walk(root, delegate (string file)
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    if (name.IndexOf("uninstall", StringComparison.OrdinalIgnoreCase) >= 0) return;
                    if (settings.ContainsPath(file)) return;
                    if (!names.Add(name)) return;
                    found.Add(new Entry { Name = name, Path = file });
                });
            }

            found.Sort(delegate (Entry a, Entry b) { return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase); });
            all.AddRange(found);
            foreach (Entry e in all) iconQueue.Enqueue(e);
        }

        private static void Walk(string dir, Action<string> onShortcut)
        {
            try
            {
                foreach (string file in Directory.GetFiles(dir, "*.lnk"))
                    onShortcut(file);
                foreach (string sub in Directory.GetDirectories(dir))
                    Walk(sub, onShortcut);
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }

        // ---- The list --------------------------------------------------------

        private void Fill()
        {
            string filter = search == null ? "" : search.Text.Trim();
            filling = true;
            list.BeginUpdate();
            list.Items.Clear();
            shown.Clear();
            foreach (Entry e in all)
            {
                if (filter.Length > 0 && e.Name.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) < 0) continue;
                var item = new ListViewItem(e.Name, e.Icon) { Tag = e, Checked = chosen.Contains(e.Path) };
                shown[e] = item;
                list.Items.Add(item);
            }
            list.EndUpdate();
            filling = false;
        }

        private void OnItemChecked(object sender, ItemCheckedEventArgs e)
        {
            if (filling) return;
            var entry = (Entry)e.Item.Tag;
            if (e.Item.Checked) chosen.Add(entry.Path); else chosen.Remove(entry.Path);
            UpdateCount();
        }

        private void UpdateCount()
        {
            int n = chosen.Count;
            countLabel.Text = n == 0 ? "Nothing selected yet" : n + (n == 1 ? " app selected" : " apps selected");
            addButton.Text = n == 0 ? "Add" : n == 1 ? "Add 1 app" : "Add " + n + " apps";
            addButton.Enabled = n > 0;
        }

        // Icons are extracted a few at a time so the window opens instantly.
        private void LoadSomeIcons()
        {
            for (int i = 0; i < 4 && iconQueue.Count > 0; i++)
            {
                Entry e = iconQueue.Dequeue();
                e.Icon = images.Images.Count;
                images.Images.Add(Shell.GetAppIcon(e.Path, iconPx));

                ListViewItem item;
                if (shown.TryGetValue(e, out item)) item.ImageIndex = e.Icon;
            }
            if (iconQueue.Count == 0) iconTimer.Stop();
        }

        private void Accept()
        {
            foreach (Entry e in all)
                if (chosen.Contains(e.Path))
                    Result.Add(new AppItem { Name = e.Name, Path = e.Path });
            DialogResult = DialogResult.OK;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                iconTimer.Dispose();
                images.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
