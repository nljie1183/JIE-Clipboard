using JIE剪切板.Controls;
using JIE剪切板.Models;
using JIE剪切板.Native;
using JIE剪切板.Pages;
using JIE剪切板.Services;

namespace JIE剪切板;

public class MainForm : Form
{
    // Data
    public AppConfig Config { get; private set; } = null!;
    public List<ClipboardRecord> Records { get; private set; } = null!;

    // Services
    private HotkeyService _hotkeyService = null!;

    // UI
    private NavigationListBox _navList = null!;
    private Panel _contentPanel = null!;
    private NotifyIcon _trayIcon = null!;
    private ContextMenuStrip _trayMenu = null!;
    private SplitContainer _splitContainer = null!;
    private Button _btnResetAll = null!;

    // Pages
    private UserControl?[] _pages = new UserControl?[7];
    private int _currentPageIndex = -1;

    // State
    private IntPtr _previousForegroundWindow;
    private bool _isMonitoring;
    private bool _isExiting;

    public MainForm()
    {
        InitializeData();
        InitializeForm();
        InitializeUI();
        InitializeTrayIcon();
    }

    #region Initialization

    private void InitializeData()
    {
        FileService.EnsureDirectories();
        LogService.Initialize();
        Config = FileService.LoadConfig();
        if (!string.IsNullOrEmpty(Config.CustomDataFolder))
            FileService.SetCustomDataFolder(Config.CustomDataFolder);
        Records = FileService.LoadRecords();
        ThemeService.Initialize(Config);
        _hotkeyService = new HotkeyService();
    }

    private void InitializeForm()
    {
        Text = "JIE 剪切板";
        var workArea = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        int w = Math.Max(800, (int)(workArea.Width * 0.55));
        int h = Math.Max(550, (int)(workArea.Height * 0.7));
        Size = new Size(w, h);
        MinimumSize = new Size(750, 500);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        ShowInTaskbar = true;
        BackColor = ThemeService.WindowBackground;
        Font = ThemeService.GlobalFont;
    }

    private void InitializeUI()
    {
        _splitContainer = new SplitContainer
        {
            Dock = DockStyle.Fill,
            SplitterDistance = 200,
            FixedPanel = FixedPanel.Panel1,
            IsSplitterFixed = true,
            SplitterWidth = 1
        };
        _splitContainer.Panel1.BackColor = ThemeService.SidebarBackground;
        _splitContainer.Panel2.BackColor = ThemeService.WindowBackground;

        // Navigation
        _navList = new NavigationListBox
        {
            Dock = DockStyle.Fill,
            BackColor = ThemeService.SidebarBackground
        };
        _navList.SelectedIndexChanged += NavList_SelectedIndexChanged;

        // Reset all defaults button
        _btnResetAll = new Button
        {
            Text = "恢复所有默认设置",
            Dock = DockStyle.Bottom,
            Height = 40,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(220, 53, 69),
            ForeColor = Color.White,
            Font = new Font(ThemeService.GlobalFont.FontFamily, 9f)
        };
        _btnResetAll.FlatAppearance.BorderSize = 0;
        _btnResetAll.Click += BtnResetAll_Click;

        _splitContainer.Panel1.Controls.Add(_navList);
        _splitContainer.Panel1.Controls.Add(_btnResetAll);

        // Content panel
        _contentPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = ThemeService.WindowBackground
        };
        _splitContainer.Panel2.Controls.Add(_contentPanel);

        Controls.Add(_splitContainer);

        // Show first page
        SwitchPage(0);
    }

    private void InitializeTrayIcon()
    {
        _trayMenu = new ContextMenuStrip();

        var menuShow = new ToolStripMenuItem("显示主窗口");
        menuShow.Click += (_, _) => ShowMainWindow();

        var menuMonitor = new ToolStripMenuItem("监听剪贴板");
        menuMonitor.CheckOnClick = true;
        menuMonitor.Checked = _isMonitoring;
        menuMonitor.Click += (_, _) => ToggleMonitoring();

        var menuExit = new ToolStripMenuItem("退出");
        menuExit.Click += (_, _) => ExitApplication();

        _trayMenu.Items.AddRange(new ToolStripItem[] { menuShow, menuMonitor, new ToolStripSeparator(), menuExit });

        _trayIcon = new NotifyIcon
        {
            Text = "JIE 剪切板",
            ContextMenuStrip = _trayMenu,
            Visible = true
        };

        // Load icon (prefer PNG for better quality)
        try
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var pngPath = Path.Combine(baseDir, "icon.png");
            var icoPath = Path.Combine(baseDir, "icon.ico");

            if (File.Exists(pngPath))
            {
                using var original = new Bitmap(pngPath);
                using var resized = new Bitmap(original, 64, 64);
                var hIcon = resized.GetHicon();
                _trayIcon.Icon = Icon.FromHandle(hIcon);
            }
            else if (File.Exists(icoPath))
            {
                _trayIcon.Icon = new Icon(icoPath);
            }
            else
            {
                _trayIcon.Icon = SystemIcons.Application;
            }
        }
        catch
        {
            _trayIcon.Icon = SystemIcons.Application;
        }
        Icon = _trayIcon.Icon;

        _trayIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                ShowMainWindow();
        };
    }

    #endregion

    #region Form Events

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        // Register clipboard listener
        Win32Api.AddClipboardFormatListener(Handle);

        // Initialize hotkey service and register wake hotkey
        _hotkeyService.Initialize(Handle);
        RegisterWakeHotkey();

        // Start monitoring if configured
        if (Config.AutoStartMonitoring)
        {
            _isMonitoring = true;
            UpdateMonitoringUI();
        }

        // Cleanup expired records
        CleanupExpiredRecords();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_isExiting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        // Cleanup
        Win32Api.RemoveClipboardFormatListener(Handle);
        _hotkeyService.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();

        SaveData();
        FileService.CleanupOrphanedImages(Records);

        base.OnFormClosing(e);
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        if (Config.HideOnLostFocus && Visible && !_isExiting)
        {
            Hide();
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Win32Api.WM_CLIPBOARDUPDATE)
        {
            OnClipboardUpdate();
            return;
        }

        if (_hotkeyService.ProcessHotkeyMessage(m))
            return;

        base.WndProc(ref m);
    }

    #endregion

    #region Navigation

    private void NavList_SelectedIndexChanged(object? sender, int index)
    {
        SwitchPage(index);
    }

    private void SwitchPage(int index)
    {
        if (index == _currentPageIndex) return;
        _currentPageIndex = index;

        _contentPanel.SuspendLayout();
        _contentPanel.Controls.Clear();

        var page = GetOrCreatePage(index);
        if (page != null)
        {
            page.Dock = DockStyle.Fill;
            _contentPanel.Controls.Add(page);

            // Refresh records page when switching to it
            if (index == 0 && page is AllRecordsPage recordsPage)
                recordsPage.RefreshRecords();
        }

        _contentPanel.ResumeLayout();
    }

    private UserControl? GetOrCreatePage(int index)
    {
        if (_pages[index] != null) return _pages[index];

        _pages[index] = index switch
        {
            0 => new AllRecordsPage(this),
            1 => new GeneralSettingsPage(this),
            2 => new HotkeyPage(this),
            3 => new AppearancePage(this),
            4 => new SecurityPage(this),
            5 => new ExportImportPage(this),
            6 => new AboutPage(),
            _ => null
        };

        return _pages[index];
    }

    #endregion

    #region Clipboard Monitoring

    private void OnClipboardUpdate()
    {
        if (!_isMonitoring || ClipboardService.IsSelfWriting) return;

        try
        {
            var record = ClipboardService.ReadFromClipboard();
            if (record == null) return;

            // Check content size limit
            if (Config.MaxContentSizeEnabled && !string.IsNullOrEmpty(record.Content))
            {
                long sizeKB = System.Text.Encoding.UTF8.GetByteCount(record.Content) / 1024;
                if (sizeKB > Config.MaxContentSizeKB) return;
            }

            // Deduplication
            if (Config.EnableDuplicateRemoval && !string.IsNullOrEmpty(record.ContentHash))
            {
                var existing = Records.FirstOrDefault(r =>
                    !r.IsEncrypted && r.ContentHash == record.ContentHash);
                if (existing != null)
                {
                    // Move to top by updating time
                    existing.CreateTime = DateTime.UtcNow;
                    SaveData();
                    RefreshCurrentPage();
                    return;
                }
            }

            Records.Insert(0, record);

            // Enforce max record count
            if (Config.MaxRecordCountEnabled && Records.Count > Config.MaxRecordCount)
            {
                // Remove oldest non-pinned records
                var toRemove = Records
                    .Where(r => !r.IsPinned)
                    .OrderBy(r => r.CreateTime)
                    .Take(Records.Count - Config.MaxRecordCount)
                    .ToList();
                foreach (var r in toRemove)
                {
                    FileService.DeleteRecordFiles(r);
                    Records.Remove(r);
                }
            }

            SaveData();
            RefreshCurrentPage();
        }
        catch (Exception ex)
        {
            LogService.Log("Clipboard update handler failed", ex);
        }
    }

    #endregion

    #region Record Operations

    public void CopyAndPasteRecord(ClipboardRecord record)
    {
        try
        {
            // Update copy count
            record.CurrentCopyCount++;
            SaveData();

            // Write to clipboard
            ClipboardService.WriteToClipboard(record);

            // Hide and paste
            HideAndPaste();
        }
        catch (Exception ex)
        {
            LogService.Log("Copy and paste failed", ex);
        }
    }

    public void HideAndPaste()
    {
        try
        {
            var targetWindow = _previousForegroundWindow;
            Hide();

            if (targetWindow != IntPtr.Zero)
            {
                // Wait for our window to fully hide
                Thread.Sleep(50);

                // Restore the previous foreground window
                Win32Api.SetForegroundWindow(targetWindow);

                // Wait for focus restoration
                Thread.Sleep(100);

                // Send Ctrl+V
                Win32Api.SendCtrlV();
            }
        }
        catch (Exception ex)
        {
            LogService.Log("Hide and paste failed", ex);
        }
    }

    public void DeleteRecord(ClipboardRecord record)
    {
        FileService.DeleteRecordFiles(record);
        Records.Remove(record);
    }

    public void ClearAllRecords()
    {
        foreach (var r in Records.ToList())
            FileService.DeleteRecordFiles(r);
        Records.Clear();
        SaveData();
    }

    public void SaveData()
    {
        try
        {
            FileService.SaveRecords(Records);
        }
        catch (Exception ex)
        {
            LogService.Log("Failed to save data", ex);
        }
    }

    #endregion

    #region Window Management

    private void ShowMainWindow()
    {
        // Save the current foreground window for auto-paste
        _previousForegroundWindow = Win32Api.GetForegroundWindow();

        if (WindowState == FormWindowState.Minimized)
            WindowState = FormWindowState.Normal;

        Show();
        Activate();
        BringToFront();

        // Refresh records page
        RefreshCurrentPage();
    }

    public void RefreshCurrentPage()
    {
        if (_currentPageIndex == 0 && _pages[0] is AllRecordsPage recordsPage)
        {
            if (recordsPage.IsHandleCreated)
                recordsPage.RefreshRecords();
        }
    }

    #endregion

    #region Hotkey

    private void RegisterWakeHotkey()
    {
        var hotkey = Config.WakeHotkey;
        bool result = _hotkeyService.RegisterHotkey(
            HotkeyService.HOTKEY_WAKE,
            hotkey.Modifiers,
            hotkey.Key,
            () => ShowMainWindow());

        if (!result)
            LogService.Log($"Failed to register wake hotkey: {hotkey.DisplayText}");
    }

    public void ReregisterHotkey()
    {
        _hotkeyService.UnregisterHotkey(HotkeyService.HOTKEY_WAKE);
        RegisterWakeHotkey();
    }

    #endregion

    #region Monitoring

    private void ToggleMonitoring()
    {
        _isMonitoring = !_isMonitoring;
        UpdateMonitoringUI();
    }

    private void UpdateMonitoringUI()
    {
        if (_trayMenu.Items.Count > 1 && _trayMenu.Items[1] is ToolStripMenuItem menuItem)
        {
            menuItem.Checked = _isMonitoring;
        }
        _trayIcon.Text = _isMonitoring ? "JIE 剪切板 - 监听中" : "JIE 剪切板 - 已暂停";
    }

    #endregion

    #region Theme

    public void ApplyTheme()
    {
        try
        {
            BackColor = ThemeService.WindowBackground;
            ForeColor = ThemeService.TextColor;
            _splitContainer.Panel1.BackColor = ThemeService.SidebarBackground;
            _splitContainer.Panel2.BackColor = ThemeService.WindowBackground;
            _contentPanel.BackColor = ThemeService.WindowBackground;
            _navList.BackColor = ThemeService.SidebarBackground;
            _navList.Invalidate();

            // Recreate pages to apply theme
            for (int i = 0; i < _pages.Length; i++)
            {
                if (_pages[i] != null)
                {
                    _pages[i]!.Dispose();
                    _pages[i] = null;
                }
            }

            // Recreate current page
            var pageIndex = _currentPageIndex;
            _currentPageIndex = -1;
            SwitchPage(pageIndex);
        }
        catch (Exception ex)
        {
            LogService.Log("Apply theme failed", ex);
        }
    }

    #endregion

    #region Import / Reset

    public void ApplyImportedConfig(AppConfig importedConfig)
    {
        Config = importedConfig;
        FileService.SaveConfig(Config);
        ThemeService.Initialize(Config);
        ApplyTheme();
        ReregisterHotkey();
    }

    private void BtnResetAll_Click(object? sender, EventArgs e)
    {
        var result = MessageBox.Show(this,
            "确定要恢复所有默认设置吗？\n这将重置所有配置（不会删除剪贴板记录）。",
            "确认重置",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (result == DialogResult.Yes)
        {
            Config = new AppConfig();
            FileService.SaveConfig(Config);
            ThemeService.Initialize(Config);
            ApplyTheme();
            ReregisterHotkey();
            MessageBox.Show(this, "已恢复所有默认设置。", "重置完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    #endregion

    #region Cleanup

    private void CleanupExpiredRecords()
    {
        try
        {
            var now = DateTime.UtcNow;
            var expired = Records.Where(r => r.ExpireTime.HasValue && r.ExpireTime.Value <= now && !r.IsPinned).ToList();
            foreach (var r in expired)
            {
                FileService.DeleteRecordFiles(r);
                Records.Remove(r);
            }
            if (expired.Count > 0)
            {
                SaveData();
                LogService.Log($"Cleaned up {expired.Count} expired records");
            }
        }
        catch (Exception ex)
        {
            LogService.Log("Expired record cleanup failed", ex);
        }
    }

    #endregion

    private void ExitApplication()
    {
        _isExiting = true;
        Close();
        Application.Exit();
    }
}
