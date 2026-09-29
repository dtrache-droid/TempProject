// The settings window (opened by right-clicking the taskbar button).
//
// Four pages: your apps, the look of the list (with a live preview), the button
// icon, and general options. Every change is saved and applied immediately.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace AppLauncher
{
    internal sealed class SettingsForm : Form
    {
        private static readonly string[] AppExtensions = { ".exe", ".lnk", ".bat", ".cmd", ".com" };

        private static readonly Color[] Presets =
        {
            Color.FromArgb(0, 0, 0),       Color.FromArgb(32, 32, 32),    Color.FromArgb(43, 52, 64),
            Color.FromArgb(20, 33, 61),    Color.FromArgb(15, 76, 129),   Color.FromArgb(11, 93, 93),
            Color.FromArgb(31, 77, 46),    Color.FromArgb(59, 42, 94),    Color.FromArgb(90, 26, 43),
            Color.FromArgb(74, 55, 40),    Color.FromArgb(243, 243, 243), Color.FromArgb(255, 255, 255),
        };

        public event EventHandler ButtonIconChanged;
        public event EventHandler ExitRequested;

        private readonly Settings settings;
        private readonly float scale;
        private readonly Timer saveTimer;
        private readonly Font titleFont = new Font("Segoe UI Semibold", 17f);
        private readonly Font headFont = new Font("Segoe UI Semibold", 10.5f);
        private readonly Font bodyFont = new Font("Segoe UI", 9.5f);
        private readonly Font smallFont = new Font("Segoe UI", 9f);

        private readonly Panel[] pages = new Panel[4];
        private NavList nav;

        // Your apps
        private ListView appList;
        private ImageList appIcons;
        private Label emptyHint;
        private IconButton renameButton, removeButton;

        // Look of the list
        private readonly List<Swatch> swatches = new List<Swatch>();
        private Swatch customSwatch;
        private TrackBar transparencyBar, sizeBar;
        private Label transparencyValue, sizeValue, sampleNote;
        private PreviewBox preview;
        private bool loading = true;

        // Button icon
        private ListView gallery;
        private ImageList galleryIcons;
        private bool buildingGallery;

        // General
        private ToggleSwitch startToggle;
        private bool settingToggle;

        public SettingsForm(Settings settings)
        {
            this.settings = settings;
            scale = Theme.ScaleOf(this);

            AutoScaleMode = AutoScaleMode.None;
            Text = "AppLauncher settings";
            Font = new Font("Segoe UI", 9f);
            BackColor = Theme.PageBack;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(S(900), S(600));
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }

            saveTimer = new Timer { Interval = 400 };
            saveTimer.Tick += delegate { saveTimer.Stop(); settings.Save(); };

            BuildNav();
            for (int i = 0; i < pages.Length; i++)
            {
                pages[i] = new Panel { Bounds = R(210, 0, 690, 600), BackColor = Theme.PageBack, Visible = false };
                Controls.Add(pages[i]);
            }
            BuildAppsPage(pages[0]);
            BuildLookPage(pages[1]);
            BuildIconPage(pages[2]);
            BuildGeneralPage(pages[3]);

            loading = false;
            ShowPage(0);
        }

        private int S(int v)
        {
            return (int)Math.Round(v * scale);
        }

        private Rectangle R(int x, int y, int w, int h)
        {
            return new Rectangle(S(x), S(y), S(w), S(h));
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            UpdatePreview();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            saveTimer.Stop();
            settings.Save();
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                saveTimer.Dispose();
                titleFont.Dispose();
                headFont.Dispose();
                bodyFont.Dispose();
                smallFont.Dispose();
            }
            base.Dispose(disposing);
        }

        private void SaveSoon()
        {
            saveTimer.Stop();
            saveTimer.Start();
        }

        // ---- Frame and navigation -------------------------------------------

        private Label AddLabel(Control parent, string text, Rectangle bounds, Font font, Color color, ContentAlignment align)
        {
            var label = new Label
            {
                Text = text,
                Font = font,
                ForeColor = color,
                AutoSize = false,
                Bounds = bounds,
                TextAlign = align,
                BackColor = parent is Card ? Color.White : parent.BackColor,
            };
            parent.Controls.Add(label);
            return label;
        }

        private void PageTitle(Panel page, string title, string subtitle)
        {
            AddLabel(page, title, R(28, 22, 630, 34), titleFont, Theme.Text, ContentAlignment.MiddleLeft);
            AddLabel(page, subtitle, R(28, 58, 630, 22), bodyFont, Theme.Muted, ContentAlignment.MiddleLeft);
        }

        private void BuildNav()
        {
            var panel = new Panel { Bounds = R(0, 0, 210, 600), BackColor = Theme.NavBack };
            Controls.Add(panel);

            var logo = new PictureBox
            {
                Image = ButtonIcons.Tile("savy", S(46)),
                Bounds = R(20, 24, 46, 46),
                BackColor = Theme.NavBack,
            };
            panel.Controls.Add(logo);
            AddLabel(panel, "AppLauncher", R(76, 24, 130, 24), headFont, Theme.Text, ContentAlignment.MiddleLeft);
            AddLabel(panel, "Settings", R(76, 46, 130, 22), bodyFont, Theme.Muted, ContentAlignment.MiddleLeft);

            nav = new NavList
            {
                Labels = new[] { "Your apps", "Look of the list", "Button icon", "General" },
                Glyphs = new[] { IconFont.Apps, IconFont.Color, IconFont.Picture, IconFont.Settings },
                Bounds = R(6, 96, 198, 4 * 44),
                BackColor = Theme.NavBack,
            };
            nav.SelectedChanged += delegate { ShowPage(nav.SelectedIndex); };
            panel.Controls.Add(nav);

            AddLabel(panel, "Changes are saved automatically.", R(20, 552, 176, 32), smallFont, Theme.Muted, ContentAlignment.TopLeft);
        }

        private void ShowPage(int index)
        {
            for (int i = 0; i < pages.Length; i++)
                pages[i].Visible = i == index;
            if (index == 1 && IsHandleCreated) UpdatePreview();
        }

        // ---- Page 1: your apps ------------------------------------------------

        private void BuildAppsPage(Panel page)
        {
            PageTitle(page, "Your apps", "Only the apps you add here appear in the list, always sorted A-Z.");
            page.AllowDrop = true;
            page.DragEnter += OnDragEnterFiles;
            page.DragDrop += OnDropFiles;

            var card = new Card { Bounds = R(28, 96, 634, 292), AllowDrop = true };
            card.DragEnter += OnDragEnterFiles;
            card.DragDrop += OnDropFiles;
            page.Controls.Add(card);

            appIcons = new ImageList { ColorDepth = ColorDepth.Depth32Bit, ImageSize = new Size(S(32), S(32)) };
            appList = new ListView
            {
                View = View.Details,
                HeaderStyle = ColumnHeaderStyle.None,
                FullRowSelect = true,
                MultiSelect = true,
                LabelEdit = true,
                HideSelection = false,
                BorderStyle = BorderStyle.None,
                ShowItemToolTips = true,
                AllowDrop = true,
                Font = new Font("Segoe UI", 10.5f),
                SmallImageList = appIcons,
                Bounds = R(2, 2, 630, 288),
            };
            appList.Columns.Add("Name", S(260));
            appList.Columns.Add("Location", S(360));
            appList.Resize += delegate { FitAppColumns(); };
            appList.DragEnter += OnDragEnterFiles;
            appList.DragDrop += OnDropFiles;
            appList.SelectedIndexChanged += delegate { UpdateAppButtons(); };
            appList.AfterLabelEdit += OnAfterLabelEdit;
            appList.KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Delete) { RemoveSelected(); e.Handled = true; }
                else if (e.KeyCode == Keys.F2) { StartRename(); e.Handled = true; }
            };
            card.Controls.Add(appList);

            emptyHint = AddLabel(card, "Drag programs or shortcuts here,\nor click \"Add from Start menu\" below.",
                R(60, 100, 514, 90), new Font("Segoe UI", 11f), Theme.Muted, ContentAlignment.MiddleCenter);
            emptyHint.AllowDrop = true;
            emptyHint.DragEnter += OnDragEnterFiles;
            emptyHint.DragDrop += OnDropFiles;
            emptyHint.BringToFront();

            var addStart = new IconButton { Text = "Add from Start menu...", Glyph = IconFont.Add, Primary = true, Bounds = R(28, 404, 236, 38) };
            addStart.Click += delegate { AddFromStartMenu(); };
            var browse = new IconButton { Text = "Browse for a file...", Glyph = IconFont.Folder, Bounds = R(274, 404, 196, 38) };
            browse.Click += delegate { BrowseForApps(); };
            renameButton = new IconButton { Text = "Rename", Glyph = IconFont.Edit, Bounds = R(28, 452, 120, 38) };
            renameButton.Click += delegate { StartRename(); };
            removeButton = new IconButton { Text = "Remove", Glyph = IconFont.Delete, Bounds = R(158, 452, 120, 38) };
            removeButton.Click += delegate { RemoveSelected(); };
            page.Controls.AddRange(new Control[] { addStart, browse, renameButton, removeButton });

            AddLabel(page, "Tip: drag an app or shortcut from the desktop or a folder onto this window to add it. F2 renames, Delete removes.",
                R(296, 448, 366, 46), smallFont, Theme.Muted, ContentAlignment.TopLeft);

            RefreshApps();
        }

        private void FitAppColumns()
        {
            int total = appList.ClientSize.Width;
            int first = (int)(total * 0.42);
            appList.Columns[0].Width = first;
            appList.Columns[1].Width = Math.Max(50, total - first - 2);
        }

        private void RefreshApps()
        {
            appIcons.Images.Clear();
            appList.BeginUpdate();
            appList.Items.Clear();
            foreach (AppItem app in settings.SortedApps())
            {
                int index = appIcons.Images.Count;
                appIcons.Images.Add(Shell.GetAppIcon(app.Path, appIcons.ImageSize.Width));

                bool exists = File.Exists(app.Path) || Directory.Exists(app.Path);
                var item = new ListViewItem(app.Name, index) { Tag = app, UseItemStyleForSubItems = false, ToolTipText = app.Path };
                var location = new ListViewItem.ListViewSubItem(item, exists ? app.Path : app.Path + "  (file not found)")
                {
                    ForeColor = exists ? Theme.Muted : Color.FromArgb(190, 50, 50),
                };
                item.SubItems.Add(location);
                appList.Items.Add(item);
            }
            appList.EndUpdate();
            FitAppColumns();
            emptyHint.Visible = appList.Items.Count == 0;
            UpdateAppButtons();
        }

        private void UpdateAppButtons()
        {
            renameButton.Enabled = appList.SelectedItems.Count == 1;
            removeButton.Enabled = appList.SelectedItems.Count > 0;
        }

        private void AddItems(IEnumerable<AppItem> items)
        {
            var added = new List<string>();
            foreach (AppItem item in items)
            {
                if (settings.ContainsPath(item.Path)) continue;
                settings.Apps.Add(item);
                added.Add(item.Path);
            }
            if (added.Count == 0) return;

            SaveSoon();
            RefreshApps();
            foreach (ListViewItem li in appList.Items)
            {
                bool isNew = added.Contains(((AppItem)li.Tag).Path);
                li.Selected = isNew;
                if (isNew) li.EnsureVisible();
            }
            UpdatePreview();
        }

        private void AddFromStartMenu()
        {
            using (var dialog = new StartMenuPicker(settings))
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    AddItems(dialog.Result);
            }
        }

        private void BrowseForApps()
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "Choose the apps to add";
                dialog.Filter = "Apps and shortcuts (*.exe;*.lnk)|*.exe;*.lnk|All files (*.*)|*.*";
                dialog.Multiselect = true;
                dialog.DereferenceLinks = false;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                var items = new List<AppItem>();
                foreach (string file in dialog.FileNames)
                    items.Add(new AppItem { Name = Shell.DefaultName(file), Path = file });
                AddItems(items);
            }
        }

        private void OnDragEnterFiles(object sender, DragEventArgs e)
        {
            e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private void OnDropFiles(object sender, DragEventArgs e)
        {
            string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null) return;

            var items = new List<AppItem>();
            foreach (string file in files)
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (File.Exists(file) && Array.IndexOf(AppExtensions, ext) >= 0)
                    items.Add(new AppItem { Name = Shell.DefaultName(file), Path = file });
            }

            if (items.Count == 0)
                MessageBox.Show(this, "Only programs (.exe) and shortcuts (.lnk) can be added.", "AppLauncher",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            else
                AddItems(items);
        }

        private void StartRename()
        {
            if (appList.SelectedItems.Count == 1) appList.SelectedItems[0].BeginEdit();
        }

        private void OnAfterLabelEdit(object sender, LabelEditEventArgs e)
        {
            string name = (e.Label ?? "").Trim();
            if (e.Label == null || name.Length == 0)
            {
                e.CancelEdit = true;
                return;
            }
            ((AppItem)appList.Items[e.Item].Tag).Name = name;
            SaveSoon();
            BeginInvoke((MethodInvoker)delegate { RefreshApps(); UpdatePreview(); });
        }

        private void RemoveSelected()
        {
            var selected = new List<AppItem>();
            foreach (ListViewItem li in appList.SelectedItems)
                selected.Add((AppItem)li.Tag);
            if (selected.Count == 0) return;

            foreach (AppItem app in selected) settings.Apps.Remove(app);
            SaveSoon();
            RefreshApps();
            UpdatePreview();
        }

        // ---- Page 2: look of the list -----------------------------------------

        private void BuildLookPage(Panel page)
        {
            PageTitle(page, "Look of the list", "Colors and sizes. The preview on the right updates as you change them.");

            AddLabel(page, "Background color", R(28, 96, 300, 22), headFont, Theme.Text, ContentAlignment.MiddleLeft);
            for (int i = 0; i <= Presets.Length; i++)
            {
                bool custom = i == Presets.Length;
                var swatch = new Swatch
                {
                    IsCustom = custom,
                    Value = custom ? Color.Empty : Presets[i],
                    Bounds = R(24 + (i % 7) * 40, 122 + (i / 7) * 40, 36, 36),
                    BackColor = Theme.PageBack,
                };
                swatch.Click += OnSwatchClick;
                page.Controls.Add(swatch);
                swatches.Add(swatch);
                if (custom) customSwatch = swatch;
            }
            UpdateSwatches();

            AddLabel(page, "Transparency", R(28, 216, 160, 22), headFont, Theme.Text, ContentAlignment.MiddleLeft);
            transparencyValue = AddLabel(page, "", R(170, 216, 150, 22), bodyFont, Theme.Muted, ContentAlignment.MiddleRight);
            transparencyBar = new TrackBar
            {
                Minimum = 0, Maximum = 100, SmallChange = 5, LargeChange = 10, TickStyle = TickStyle.None,
                AutoSize = false, Bounds = R(20, 240, 304, 32), BackColor = Theme.PageBack,
                Value = settings.TransparencyPercent,
            };
            transparencyBar.ValueChanged += delegate
            {
                UpdateTransparencyLabel();
                if (loading) return;
                settings.TransparencyPercent = transparencyBar.Value;
                SaveSoon();
                UpdatePreview();
            };
            page.Controls.Add(transparencyBar);
            AddLabel(page, "Solid", R(28, 272, 100, 18), smallFont, Theme.Muted, ContentAlignment.MiddleLeft);
            AddLabel(page, "No background", R(200, 272, 120, 18), smallFont, Theme.Muted, ContentAlignment.MiddleRight);
            UpdateTransparencyLabel();

            AddLabel(page, "Icon and text size", R(28, 312, 160, 22), headFont, Theme.Text, ContentAlignment.MiddleLeft);
            sizeValue = AddLabel(page, "", R(170, 312, 150, 22), bodyFont, Theme.Muted, ContentAlignment.MiddleRight);
            sizeBar = new TrackBar
            {
                Minimum = 16, Maximum = 64, SmallChange = 2, LargeChange = 8, TickStyle = TickStyle.None,
                AutoSize = false, Bounds = R(20, 336, 304, 32), BackColor = Theme.PageBack,
                Value = Math.Max(16, Math.Min(64, settings.IconSize)),
            };
            sizeBar.ValueChanged += delegate
            {
                UpdateSizeLabel();
                if (loading) return;
                settings.IconSize = sizeBar.Value;
                SaveSoon();
                UpdatePreview();
            };
            page.Controls.Add(sizeBar);
            AddLabel(page, "Small", R(28, 368, 100, 18), smallFont, Theme.Muted, ContentAlignment.MiddleLeft);
            AddLabel(page, "Large", R(200, 368, 120, 18), smallFont, Theme.Muted, ContentAlignment.MiddleRight);
            UpdateSizeLabel();

            var reset = new IconButton { Text = "Reset look", Glyph = IconFont.Refresh, Bounds = R(28, 412, 150, 38) };
            reset.Click += delegate { ResetLook(); };
            page.Controls.Add(reset);

            AddLabel(page, "Preview", R(356, 96, 300, 22), headFont, Theme.Text, ContentAlignment.MiddleLeft);
            preview = new PreviewBox { Bounds = R(356, 124, 306, 380), BackColor = Theme.PageBack };
            page.Controls.Add(preview);
            sampleNote = AddLabel(page, "Sample apps are shown until you add your own.", R(356, 508, 306, 20), smallFont, Theme.Muted, ContentAlignment.MiddleLeft);
        }

        private void UpdateTransparencyLabel()
        {
            int v = transparencyBar.Value;
            transparencyValue.Text = v == 0 ? "0% (solid)" : v == 100 ? "100% (no background)" : v + "%";
        }

        private void UpdateSizeLabel()
        {
            sizeValue.Text = sizeBar.Value + " px";
        }

        private static bool SameRgb(Color a, Color b)
        {
            return a.R == b.R && a.G == b.G && a.B == b.B;
        }

        private void UpdateSwatches()
        {
            bool matched = false;
            foreach (Swatch sw in swatches)
            {
                if (sw.IsCustom) continue;
                sw.Selected = SameRgb(sw.Value, settings.BackColor);
                matched |= sw.Selected;
                sw.Invalidate();
            }
            customSwatch.Selected = !matched;
            customSwatch.Invalidate();
        }

        private void OnSwatchClick(object sender, EventArgs e)
        {
            var swatch = (Swatch)sender;
            if (swatch.IsCustom)
            {
                using (var dialog = new ColorDialog())
                {
                    dialog.AnyColor = true;
                    dialog.FullOpen = true;
                    dialog.Color = settings.BackColor;
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    settings.BackColor = dialog.Color;
                }
            }
            else
            {
                settings.BackColor = swatch.Value;
            }
            UpdateSwatches();
            SaveSoon();
            UpdatePreview();
        }

        private void ResetLook()
        {
            settings.BackColor = Color.FromArgb(32, 32, 32);
            loading = true;
            transparencyBar.Value = settings.TransparencyPercent = 10;
            sizeBar.Value = settings.IconSize = 24;
            loading = false;
            UpdateTransparencyLabel();
            UpdateSizeLabel();
            UpdateSwatches();
            SaveSoon();
            UpdatePreview();
        }

        private static List<AppItem> SampleApps()
        {
            string sys = Environment.SystemDirectory;
            return new List<AppItem>
            {
                new AppItem { Name = "Calculator", Path = Path.Combine(sys, "calc.exe") },
                new AppItem { Name = "Notepad", Path = Path.Combine(sys, "notepad.exe") },
                new AppItem { Name = "Paint", Path = Path.Combine(sys, "mspaint.exe") },
            };
        }

        private void UpdatePreview()
        {
            if (preview == null || !IsHandleCreated || preview.Width <= 0) return;

            List<AppItem> items = settings.SortedApps();
            sampleNote.Visible = items.Count == 0;
            if (items.Count == 0) items = SampleApps();

            int maxHeight = preview.Height - PreviewBox.StripHeight(scale) - S(20);
            Bitmap panel;
            using (var painter = new PopupPainter(settings, scale, items, preview.Width - S(20), maxHeight))
                panel = painter.Render(items.Count > 1 ? 1 : 0, 0);

            bool mask;
            Image icon = ButtonIcons.LoadCurrent(settings, out mask);
            preview.SetContent(panel, icon, mask);
        }

        // ---- Page 3: button icon ----------------------------------------------

        private void BuildIconPage(Panel page)
        {
            PageTitle(page, "Button icon", "The icon shown in the far-left corner of the taskbar.");

            var card = new Card { Bounds = R(28, 96, 634, 230) };
            page.Controls.Add(card);

            galleryIcons = new ImageList { ColorDepth = ColorDepth.Depth32Bit, ImageSize = new Size(S(64), S(64)) };
            gallery = new ListView
            {
                View = View.LargeIcon,
                MultiSelect = false,
                HideSelection = false,
                BorderStyle = BorderStyle.None,
                Font = new Font("Segoe UI", 10f),
                LargeImageList = galleryIcons,
                Bounds = R(10, 10, 614, 210),
            };
            gallery.ItemSelectionChanged += OnGallerySelected;
            card.Controls.Add(gallery);
            BuildGallery();

            var choose = new IconButton { Text = "Choose an image...", Glyph = IconFont.Picture, Primary = true, Bounds = R(28, 342, 210, 38) };
            choose.Click += delegate { ChooseImage(); };
            page.Controls.Add(choose);

            AddLabel(page, "Use your own picture: a square PNG with a transparent background works best (256 x 256).\nJPG, BMP, GIF and ICO files also work. SVG files are not supported - export them as PNG first.",
                R(28, 392, 634, 48), smallFont, Theme.Muted, ContentAlignment.TopLeft);
        }

        private void BuildGallery()
        {
            buildingGallery = true;
            galleryIcons.Images.Clear();
            gallery.BeginUpdate();
            gallery.Items.Clear();

            foreach (ButtonIcons.Entry entry in ButtonIcons.BuiltIn)
                AddGalleryItem(entry.Id, entry.Label);
            if (ButtonIcons.CustomExists)
                AddGalleryItem("custom", "My own icon");

            ListViewItem current = null;
            foreach (ListViewItem item in gallery.Items)
                if ((string)item.Tag == settings.ButtonIcon) current = item;
            if (current == null) current = gallery.Items[0];
            current.Selected = true;
            current.Focused = true;

            gallery.EndUpdate();
            buildingGallery = false;
        }

        private void AddGalleryItem(string id, string label)
        {
            galleryIcons.Images.Add(ButtonIcons.Tile(id, galleryIcons.ImageSize.Width));
            gallery.Items.Add(new ListViewItem(label, galleryIcons.Images.Count - 1) { Tag = id });
        }

        private void OnGallerySelected(object sender, ListViewItemSelectionChangedEventArgs e)
        {
            if (buildingGallery || !e.IsSelected) return;
            settings.ButtonIcon = (string)e.Item.Tag;
            settings.Save();
            RaiseButtonIconChanged();
            UpdatePreview();
        }

        private void ChooseImage()
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "Choose an icon for the taskbar button";
                dialog.Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    ButtonIcons.ImportCustom(dialog.FileName);
                    settings.ButtonIcon = "custom";
                    settings.Save();
                    BuildGallery();
                    RaiseButtonIconChanged();
                    UpdatePreview();
                }
                catch (Exception ex)
                {
                    Shell.ShowError("Could not use that image.", ex);
                }
            }
        }

        private void RaiseButtonIconChanged()
        {
            EventHandler handler = ButtonIconChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        // ---- Page 4: general --------------------------------------------------

        private void BuildGeneralPage(Panel page)
        {
            PageTitle(page, "General", "Startup and housekeeping.");

            // Start with Windows
            var startCard = new Card { Bounds = R(28, 96, 634, 88) };
            page.Controls.Add(startCard);
            AddLabel(startCard, "Start with Windows", R(20, 16, 420, 24), headFont, Theme.Text, ContentAlignment.MiddleLeft);
            AddLabel(startCard, "The button appears on the taskbar every time you sign in.", R(20, 42, 470, 24), bodyFont, Theme.Muted, ContentAlignment.MiddleLeft);
            startToggle = new ToggleSwitch { Bounds = R(634 - 20 - 46, 32, 46, 24), BackColor = Color.White };
            startToggle.Checked = Autostart.IsEnabled;
            startToggle.CheckedChanged += delegate { OnStartToggled(); };
            startCard.Controls.Add(startToggle);

            // Settings folder
            var folderCard = new Card { Bounds = R(28, 198, 634, 88) };
            page.Controls.Add(folderCard);
            AddLabel(folderCard, "Settings folder", R(20, 16, 420, 24), headFont, Theme.Text, ContentAlignment.MiddleLeft);
            AddLabel(folderCard, Settings.Folder, R(20, 42, 420, 24), smallFont, Theme.Muted, ContentAlignment.MiddleLeft);
            var openFolder = new IconButton { Text = "Open folder", Glyph = IconFont.Folder, Bounds = R(634 - 20 - 150, 26, 150, 36) };
            openFolder.Click += delegate { OpenSettingsFolder(); };
            folderCard.Controls.Add(openFolder);

            // Exit
            var exitCard = new Card { Bounds = R(28, 300, 634, 88) };
            page.Controls.Add(exitCard);
            AddLabel(exitCard, "Exit AppLauncher", R(20, 16, 420, 24), headFont, Theme.Text, ContentAlignment.MiddleLeft);
            AddLabel(exitCard, "Removes the button. It returns at the next sign-in or when you start the app again.", R(20, 42, 430, 40), bodyFont, Theme.Muted, ContentAlignment.TopLeft);
            var exit = new IconButton { Text = "Exit", Glyph = IconFont.Power, Bounds = R(634 - 20 - 150, 26, 150, 36) };
            exit.Click += delegate
            {
                EventHandler handler = ExitRequested;
                if (handler != null) handler(this, EventArgs.Empty);
            };
            exitCard.Controls.Add(exit);

            AddLabel(page, "AppLauncher 3.2 - left-click the taskbar button to open your apps, right-click it for these settings.",
                R(28, 408, 634, 40), smallFont, Theme.Muted, ContentAlignment.TopLeft);
        }

        private void OnStartToggled()
        {
            if (settingToggle) return;
            try
            {
                Autostart.Set(startToggle.Checked);
            }
            catch (Exception ex)
            {
                Shell.ShowError("Could not change the startup setting.", ex);
                settingToggle = true;
                startToggle.Checked = Autostart.IsEnabled;
                settingToggle = false;
            }
        }

        private static void OpenSettingsFolder()
        {
            try
            {
                Directory.CreateDirectory(Settings.Folder);
                Process.Start("explorer.exe", "\"" + Settings.Folder + "\"");
            }
            catch (Exception ex)
            {
                Shell.ShowError("Could not open the folder.", ex);
            }
        }
    }
}
