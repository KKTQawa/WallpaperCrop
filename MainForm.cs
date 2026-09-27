namespace WallpaperPeek;

internal sealed class MainForm : Form
{
    private const int SelectHotkey = 100, ToggleHotkey = 101, SaveHotkey = 102;
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _clickThroughMenu;
    private readonly ToolStripMenuItem _startupMenu;
    private readonly AppConfig _config = ConfigStore.Load();
    private PreviewForm? _preview;
    private Rectangle? _lastRegion;
    private bool _exiting;
    private bool _changingStartup;

    public MainForm()
    {
        ShowInTaskbar = false; WindowState = FormWindowState.Minimized; Opacity = 0;
        _tray = new NotifyIcon { Icon = SystemIcons.Information, Text = "WallpaperPeek", Visible = true };
        var menu = new ContextMenuStrip();
        menu.Items.Add("框选壁纸区域 (Ctrl+Alt+Shift+A)", null, (_, _) => StartSelection());
        menu.Items.Add("隐藏/恢复浮窗 (Ctrl+Alt+Shift+Z)", null, (_, _) => TogglePreview());
        menu.Items.Add("保存当前区域 (Ctrl+Alt+Shift+S)", null, (_, _) => SaveLastRegion());
        _clickThroughMenu = new ToolStripMenuItem("开启鼠标点击穿透", null, (_, _) => ToggleClickThrough());
        menu.Items.Add(_clickThroughMenu);
        _startupMenu = new ToolStripMenuItem("开机自启动") { CheckOnClick = true, Checked = _config.StartWithWindows };
        _startupMenu.CheckedChanged += (_, _) => SetStartup(_startupMenu.Checked);
        menu.Items.Add(_startupMenu);
        menu.Items.Add("重置所有设置并清空预设", null, (_, _) => ResetAllSettings());
        menu.Items.Add("帮助", null, (_, _) => ShowHelp());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => { _exiting = true; Close(); });
        _tray.ContextMenuStrip = menu;
        menu.Opening += (_, _) => UpdateClickThroughMenuText();
        _tray.DoubleClick += (_, _) => StartSelection();
        const uint toolModifiers = NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_SHIFT;
        Register(SelectHotkey, Keys.A, toolModifiers);
        Register(ToggleHotkey, Keys.Z, toolModifiers);
        Register(SaveHotkey, Keys.S, toolModifiers);
        for (var i = 1; i <= 9; i++) Register(i, Keys.D0 + i, NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT);
    }
    private bool Register(int id, Keys key, uint modifiers) => NativeMethods.RegisterHotKey(Handle, id, modifiers, (uint)key);
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_HOTKEY) {
            HandleShortcut(m.WParam.ToInt32());
        }
        base.WndProc(ref m);
    }
    private void HandleShortcut(int id)
    {
        if (id == SelectHotkey) StartSelection(); else if (id == ToggleHotkey) TogglePreview(); else if (id == SaveHotkey) SaveLastRegion(); else if (id is >= 1 and <= 9) ShowSlot(id);
    }
    private void StartSelection()
    {
        var selector = new RegionSelector();
        selector.RegionSelected += ShowRegion;
        selector.Show(); selector.Activate();
    }
    private void ShowRegion(Rectangle region)
    {
        try { _lastRegion = region; ShowImage(WallpaperService.CaptureDesktopRegion(region)); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "WallpaperPeek", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }
    private void ShowImage(Bitmap image, Size? initialSize = null)
    {
        _preview?.Close();
        _preview = new PreviewForm(image, initialSize);
        _preview.Show();
    }
    private void ShowSlot(int slot)
    {
        var region = _config.Regions.FirstOrDefault(x => x.Slot == slot);
        if (region is null) { _tray.ShowBalloonTip(1500, "WallpaperPeek", $"编号 {slot} 还没有保存区域。", ToolTipIcon.Info); return; }
        try
        {
            _lastRegion = WallpaperService.Denormalize(region);
            var savedSize = region.DisplayWidth >= 80 && region.DisplayHeight >= 80 ? new Size(region.DisplayWidth, region.DisplayHeight) : (Size?)null;
            ShowImage(WallpaperService.CaptureDesktopRegion(_lastRegion.Value), savedSize);
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "WallpaperPeek", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }
    private void TogglePreview()
    {
        if (_preview is null) return;
        if (_preview.Visible) _preview.Hide(); else { _preview.Show(); _preview.Activate(); }
    }
    private void ToggleClickThrough()
    {
        if (_preview is null) { _tray.ShowBalloonTip(1500, "WallpaperPeek", "请先框选一个区域。", ToolTipIcon.Info); return; }
        _preview.ToggleClickThrough();
        UpdateClickThroughMenuText();
        _tray.ShowBalloonTip(1500, "WallpaperPeek", _preview.IsClickThrough ? "已开启点击穿透。" : "已关闭点击穿透。", ToolTipIcon.Info);
    }
    private void UpdateClickThroughMenuText() => _clickThroughMenu.Text = _preview?.IsClickThrough == true ? "关闭鼠标点击穿透" : "开启鼠标点击穿透";
    private void SetStartup(bool enabled)
    {
        if (_changingStartup) return;
        try
        {
            StartupService.SetEnabled(enabled);
            _config.StartWithWindows = enabled;
            ConfigStore.Save(_config);
        }
        catch (Exception ex)
        {
            _changingStartup = true;
            _startupMenu.Checked = !enabled;
            _changingStartup = false;
            MessageBox.Show(ex.Message, "WallpaperPeek", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
    private void ResetAllSettings()
    {
        var result = MessageBox.Show("将关闭当前预览、关闭开机自启动，并清空 Ctrl+Alt+1 至 9 的所有已保存预设。\n\n确定继续吗？", "重置 WallpaperPeek", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (result != DialogResult.Yes) return;
        try
        {
            StartupService.SetEnabled(false);
            _config.StartWithWindows = false;
            _config.Regions.Clear();
            ConfigStore.Save(_config);
            _changingStartup = true;
            _startupMenu.Checked = false;
            _changingStartup = false;
            _preview?.Close();
            _preview = null;
            _lastRegion = null;
            _tray.ShowBalloonTip(2000, "WallpaperPeek", "已重置所有设置并清空预设。", ToolTipIcon.Info);
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "WallpaperPeek", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }
    private static void ShowHelp() => MessageBox.Show("WallpaperPeek\n\nCtrl+Alt+Shift+A：框选壁纸区域\nCtrl+Alt+Shift+S：保存最后区域\nCtrl+Alt+Shift+Z：隐藏/恢复小窗\nCtrl+Alt+1..9：显示预设\n\n小窗：拖动中央移动；拖动边缘调整大小；滚轮调透明度；L 开关点击穿透；Esc 隐藏。\n\nWallpaper Engine 未运行时，程序会自动使用 Windows 的静态图片壁纸。所有功能也都可以从托盘右键菜单使用。", "帮助", MessageBoxButtons.OK, MessageBoxIcon.Information);
    private void SaveLastRegion()
    {
        if (_lastRegion is null) { _tray.ShowBalloonTip(1500, "WallpaperPeek", "请先框选一个壁纸区域。", ToolTipIcon.Info); return; }
        using var dialog = new SaveRegionDialog(_config.Regions.Select(x => x.Slot));
        if (dialog.ShowDialog() != DialogResult.OK) return;
        _config.Regions.RemoveAll(x => x.Slot == dialog.Slot);
        var region = WallpaperService.Normalize(_lastRegion.Value, dialog.Slot, dialog.RegionName);
        if (_preview is not null) { region.DisplayWidth = _preview.ClientSize.Width; region.DisplayHeight = _preview.ClientSize.Height; }
        _config.Regions.Add(region);
        ConfigStore.Save(_config);
        _tray.ShowBalloonTip(1500, "WallpaperPeek", $"已保存到 Ctrl+Alt+{dialog.Slot}。", ToolTipIcon.Info);
    }
    protected override void OnFormClosing(FormClosingEventArgs e) { if (!_exiting) { e.Cancel = true; Hide(); } else { for (var i = 1; i <= 9; i++) NativeMethods.UnregisterHotKey(Handle, i); NativeMethods.UnregisterHotKey(Handle, SelectHotkey); NativeMethods.UnregisterHotKey(Handle, ToggleHotkey); NativeMethods.UnregisterHotKey(Handle, SaveHotkey); _tray.Dispose(); } base.OnFormClosing(e); }
}

internal sealed class SaveRegionDialog : Form
{
    private readonly TextBox _name = new() { Text = "参考区域", Dock = DockStyle.Fill };
    private readonly NumericUpDown _slot = new() { Minimum = 1, Maximum = 9, Dock = DockStyle.Fill };
    public string RegionName => string.IsNullOrWhiteSpace(_name.Text) ? "参考区域" : _name.Text.Trim();
    public int Slot => (int)_slot.Value;
    public SaveRegionDialog(IEnumerable<int> used)
    {
        Text = "保存壁纸区域"; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false; StartPosition = FormStartPosition.CenterScreen; ClientSize = new Size(280, 120);
        _slot.Value = Enumerable.Range(1, 9).FirstOrDefault(x => !used.Contains(x), 1);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), ColumnCount = 2, RowCount = 3 };
        layout.Controls.Add(new Label { Text = "名称", AutoSize = true }, 0, 0); layout.Controls.Add(_name, 1, 0);
        layout.Controls.Add(new Label { Text = "快捷编号", AutoSize = true }, 0, 1); layout.Controls.Add(_slot, 1, 1);
        var ok = new Button { Text = "保存", DialogResult = DialogResult.OK }; var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft }; buttons.Controls.Add(cancel); buttons.Controls.Add(ok); layout.SetColumnSpan(buttons, 2); layout.Controls.Add(buttons, 0, 2);
        Controls.Add(layout); AcceptButton = ok; CancelButton = cancel;
    }
}
