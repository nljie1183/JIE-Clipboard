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
    private bool _pasteMode;
    private System.Windows.Forms.Timer? _clipboardWatchdog;
    private System.Windows.Forms.Timer? _saveThrottleTimer;
    private bool _saveDataPending;
    private DateTime _lastClipboardUpdate = DateTime.UtcNow;

    // Prevent window activation when in paste mode (like Win+V behavior)
    protected override bool ShowWithoutActivation => _pasteMode;

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
        MinimumSize = new Size(DpiHelper.Scale(750), DpiHelper.Scale(500));
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
            FixedPanel = FixedPanel.Panel1,
            IsSplitterFixed = false,
            SplitterWidth = DpiHelper.Scale(4)
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
            Height = DpiHelper.Scale(40),
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
        menuShow.Click += (_, _) => ShowMainWindow(forPaste: false);

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

        // Load icon from embedded resource
        try
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            var pngStream = assembly.GetManifestResourceStream("icon.png");
            var icoStream = assembly.GetManifestResourceStream("icon.ico");

            if (pngStream != null)
            {
                using (pngStream)
                {
                    using var original = new Bitmap(pngStream);
                    using var resized = new Bitmap(original, 64, 64);
                    var hIcon = resized.GetHicon();
                    _trayIcon.Icon = (Icon)Icon.FromHandle(hIcon).Clone();
                    Win32Api.DestroyIcon(hIcon);
                }
            }
            else if (icoStream != null)
            {
                using (icoStream)
                    _trayIcon.Icon = new Icon(icoStream);
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
                ShowMainWindow(forPaste: false);
        };
    }

    #endregion

    #region Form Events

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        // Set splitter constraints after form has its real size
        try
        {
            _splitContainer.Panel1MinSize = DpiHelper.Scale(140);
            _splitContainer.Panel2MinSize = DpiHelper.Scale(300);
            _splitContainer.SplitterDistance = DpiHelper.Scale(220);
        }
        catch { }

        // Register clipboard listener
        Win32Api.AddClipboardFormatListener(Handle);

        // Initialize hotkey service and register wake hotkey
        _hotkeyService.Initialize(Handle);
        RegisterWakeHotkey();

        // Always start monitoring on launch
        _isMonitoring = true;
        UpdateMonitoringUI();

        // Start clipboard watchdog timer to ensure monitoring stays active on Win11
        _clipboardWatchdog = new System.Windows.Forms.Timer { Interval = 30000 }; // 30s
        _clipboardWatchdog.Tick += ClipboardWatchdog_Tick;
        _clipboardWatchdog.Start();

        // Save throttle timer to batch rapid saves
        _saveThrottleTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _saveThrottleTimer.Tick += (_, _) => { _saveThrottleTimer.Stop(); FlushSaveData(); };

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
        FlushSaveData();
        _saveThrottleTimer?.Stop();
        _saveThrottleTimer?.Dispose();
        _clipboardWatchdog?.Stop();
        _clipboardWatchdog?.Dispose();
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
        if (_pasteMode) return;
        if (Config.HideOnLostFocus && Visible && !_isExiting)
        {
            BeginInvoke(() =>
            {
                if (!Visible || _isExiting) return;
                // Don't hide if an owned dialog (edit/password) is active
                var active = Form.ActiveForm;
                if (active != null && active != this) return;
                Hide();
            });
        }
    }

    protected override void WndProc(ref Message m)
    {
        // In paste mode, prevent window activation on mouse click
        // so the original app keeps keyboard focus (like Win+V)
        if (m.Msg == Win32Api.WM_MOUSEACTIVATE && _pasteMode)
        {
            m.Result = (IntPtr)Win32Api.MA_NOACTIVATE;
            return;
        }

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
        _lastClipboardUpdate = DateTime.UtcNow;

        try
        {
            var record = ClipboardService.ReadFromClipboard();
            if (record == null) return;

            // Check record type filter
            if (!IsTypeAllowed(record.ContentType)) return;

            // Check extension filter for file-based types
            if (record.ContentType is ClipboardContentType.FileDrop or ClipboardContentType.Video or ClipboardContentType.Folder)
            {
                var paths = record.Content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                if (!AreExtensionsAllowed(paths)) return;
            }

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
                    // Move to top by updating time, reset copy count so it's usable again
                    existing.CreateTime = DateTime.UtcNow;
                    existing.CurrentCopyCount = 0;
                    SaveData();
                    RefreshCurrentPage();
                    return;
                }
            }

            Records.Insert(0, record);

            // Apply persistent encrypted storage for binary types if enabled
            if (Config.PersistentBinaryStorage)
                ApplyPersistentStorage(record);

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

    private void ClipboardWatchdog_Tick(object? sender, EventArgs e)
    {
        try
        {
            // Only re-register if no clipboard update received for >60s (may indicate dropped listener)
            if (_isMonitoring && IsHandleCreated && !_isExiting
                && (DateTime.UtcNow - _lastClipboardUpdate).TotalSeconds > 60)
            {
                Win32Api.RemoveClipboardFormatListener(Handle);
                if (!Win32Api.AddClipboardFormatListener(Handle))
                    LogService.Log("Clipboard watchdog: failed to re-register listener");
            }

            // Periodically cleanup expired records
            CleanupExpiredRecords();
        }
        catch (Exception ex)
        {
            LogService.Log("Clipboard watchdog failed", ex);
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

    public async void HideAndPaste()
    {
        try
        {
            var targetWindow = _previousForegroundWindow;
            bool wasPasteMode = _pasteMode;
            _pasteMode = false;
            TopMost = false;
            Hide();

            if (wasPasteMode)
            {
                await Task.Delay(50);
                Win32Api.SendCtrlV();
            }
            else if (targetWindow != IntPtr.Zero)
            {
                await Task.Delay(50);
                Win32Api.SetForegroundWindow(targetWindow);
                await Task.Delay(100);
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
        _saveDataPending = true;
        if (_saveThrottleTimer != null && !_saveThrottleTimer.Enabled)
            _saveThrottleTimer.Start();
    }

    private void FlushSaveData()
    {
        if (!_saveDataPending) return;
        _saveDataPending = false;
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
        ShowMainWindow(forPaste: true);
    }

    private void ShowMainWindow(bool forPaste)
    {
        // Toggle: if already visible, hide
        if (Visible && !_isExiting)
        {
            _pasteMode = false;
            TopMost = false;
            Hide();
            return;
        }

        // Save the current foreground window for auto-paste
        _previousForegroundWindow = Win32Api.GetForegroundWindow();

        if (WindowState == FormWindowState.Minimized)
            WindowState = FormWindowState.Normal;

        _pasteMode = forPaste;

        if (forPaste)
        {
            // Paste mode: show without stealing focus (like Win+V)
            // Original app keeps keyboard focus, mouse can click records
            TopMost = true;
            Show();
        }
        else
        {
            // Settings mode: normal window with full activation
            TopMost = false;
            Show();
            Activate();
            BringToFront();
        }

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
                    try { _pages[i]!.Dispose(); } catch { }
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

    #region Record Type Filtering

    private bool IsTypeAllowed(ClipboardContentType type) => type switch
    {
        ClipboardContentType.PlainText => Config.RecordPlainText,
        ClipboardContentType.RichText => Config.RecordRichText,
        ClipboardContentType.Image => Config.RecordImage,
        ClipboardContentType.FileDrop => Config.RecordFileDrop,
        ClipboardContentType.Video => Config.RecordVideo,
        ClipboardContentType.Folder => Config.RecordFolder,
        _ => true
    };

    private bool AreExtensionsAllowed(string[] paths)
    {
        var include = ParseExtensions(Config.IncludeExtensions);
        var exclude = ParseExtensions(Config.ExcludeExtensions);

        if (include.Count > 0)
            return paths.Any(p => include.Contains(Path.GetExtension(p).ToLowerInvariant()));
        if (exclude.Count > 0)
            return !paths.All(p => exclude.Contains(Path.GetExtension(p).ToLowerInvariant()));
        return true;
    }

    private static HashSet<string> ParseExtensions(string ext)
    {
        if (string.IsNullOrWhiteSpace(ext)) return new();
        return ext.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                  .Select(e => e.StartsWith('.') ? e.ToLowerInvariant() : "." + e.ToLowerInvariant())
                  .ToHashSet();
    }

    #endregion

    #region Persistent Encrypted Storage

    private void ApplyPersistentStorage(ClipboardRecord record)
    {
        try
        {
            switch (record.ContentType)
            {
                case ClipboardContentType.Image:
                    // Image is already saved as PNG by ClipboardService, encrypt it in-place
                    if (File.Exists(record.Content) && !record.Content.EndsWith(".enc", StringComparison.OrdinalIgnoreCase))
                    {
                        var encPath = FileService.EncryptExistingFile(record.Content);
                        if (!string.IsNullOrEmpty(encPath))
                        {
                            try { File.Delete(record.Content); } catch { }
                            record.Content = encPath;
                            record.ContentHash = EncryptionService.ComputeContentHash(record.Content);
                        }
                    }
                    break;

                case ClipboardContentType.FileDrop:
                case ClipboardContentType.Video:
                    var paths = record.Content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    var encPaths = new List<string>();
                    long maxBytes = Config.MaxPersistFileSizeMB * 1024L * 1024;
                    foreach (var path in paths)
                    {
                        if (File.Exists(path))
                        {
                            var fi = new FileInfo(path);
                            if (fi.Length <= maxBytes)
                            {
                                var enc = FileService.SaveAndEncryptFile(path);
                                encPaths.Add(!string.IsNullOrEmpty(enc) ? enc : path);
                            }
                            else
                            {
                                encPaths.Add(path); // Too large, just record path
                            }
                        }
                        else
                        {
                            encPaths.Add(path);
                        }
                    }
                    record.Content = string.Join("\n", encPaths);
                    record.ContentHash = EncryptionService.ComputeContentHash(record.Content);
                    break;

                // Folders: just record path (too many files to copy)
            }
        }
        catch (Exception ex)
        {
            LogService.Log("Failed to apply persistent storage", ex);
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
