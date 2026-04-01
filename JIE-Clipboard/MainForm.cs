using JIE剪切板.Controls;
using JIE剪切板.Models;
using JIE剪切板.Native;
using JIE剪切板.Pages;
using JIE剪切板.Services;

namespace JIE剪切板;

/// <summary>
/// 应用程序主窗口。
/// 职责：
///  1. 数据初始化（加载配置、创建 RecordManager、注册热键）
///  2. UI 布局（左侧导航栏 + 右侧内容区 + 系统托盘图标）
///  3. 窗口管理（贴入模式 pasteMode —— 不抢焦点；设置模式 —— 正常激活）
///  4. WndProc 消息分发（剪贴板 → RecordManager、热键 → HotkeyService）
///  5. 主题切换（销毁并重建所有 Pages）
///
/// 记录管理（CRUD、过滤、去重、持久化、节流保存）由 RecordManager 负责。
/// </summary>
public class MainForm : Form
{
    // ==================== 数据 ====================
    /// <summary>全局配置对象，由 FileService.LoadConfig() 加载</summary>
    public AppConfig Config { get; private set; } = null!;
    /// <summary>剪贴板记录列表（委托给 RecordManager）</summary>
    public List<ClipboardRecord> Records => _recordManager.Records;

    // ==================== 服务 ====================
    private HotkeyService _hotkeyService = null!; // 全局热键注册/注销服务
    public HotkeyService HotkeyService => _hotkeyService;
    private RecordManager _recordManager = null!;  // 记录管理器（CRUD + 过滤 + 持久化）

    // ==================== UI 控件 ====================
    private NavigationListBox _navList = null!;    // 左侧导航列表
    private Panel _contentPanel = null!;           // 右侧内容面板
    private NotifyIcon _trayIcon = null!;          // 系统托盘图标
    private ContextMenuStrip _trayMenu = null!;    // 托盘右键菜单
    private SplitContainer _splitContainer = null!;// 左右分栏容器
    private Button _btnResetAll = null!;           // "恢复所有默认设置"按钮

    // ==================== 页面缓存 ====================
    private UserControl?[] _pages = new UserControl?[7]; // 7 个页面的缓存数组
    private int _currentPageIndex = -1;                  // 当前显示的页面索引

    // ==================== 运行时状态 ====================
    private IntPtr _previousForegroundWindow;        // 唤醒前的前台窗口句柄（用于自动粘贴）
    private bool _isMonitoring;                      // 是否正在监听剪贴板
    private bool _isExiting;                         // 是否正在退出（防止关闭时再次隐藏）
    private bool _pasteMode;                         // 贴入模式标记（不抢焦点，类似 Win+V）
    private readonly bool _startSilent;              // 静默启动标志（开机自启时直接托盘）
    private bool _suppressAutoHide;                  // 对话框交互期间阻止自动隐藏
    private System.Windows.Forms.Timer? _clipboardWatchdog;  // 看门狗定时器（30s 检查监听是否中断）
    private DateTime _lastClipboardUpdate = DateTime.UtcNow; // 上次收到剪贴板更新的时间（用于看门狗判断）
    private int _watchdogTickCount;                  // 看门狗计数器（每 5 次≈2.5 分钟清理临时文件）
    private DateTime _lastBalloonTime;               // 上次托盘气泡时间（节流用，防连续弹出）

    /// <summary>
    /// 贴入模式下阻止窗口被激活（ShowWithoutActivation = true），
    /// 类似 Windows 内置 Win+V 行为，原应用保持键盘焦点。
    /// </summary>
    protected override bool ShowWithoutActivation => _pasteMode;

    public MainForm(bool silent = false)
    {
        _startSilent = silent;
        InitializeData();
        InitializeForm();
        InitializeUI();
        InitializeTrayIcon();
    }

    #region Initialization

    /// <summary>初始化数据层：目录结构、日志、配置、记录管理器、主题、热键服务</summary>
    private void InitializeData()
    {
        FileService.EnsureDirectories();
        LogService.Initialize();
        Config = FileService.LoadConfig();
        if (!string.IsNullOrEmpty(Config.CustomDataFolder))
            FileService.SetCustomDataFolder(Config.CustomDataFolder);
        var records = FileService.LoadRecords();
        _recordManager = new RecordManager(() => Config, records);
        _recordManager.RecordsChanged += RefreshCurrentPage;
        ThemeService.Initialize(Config);
        _hotkeyService = new HotkeyService();
    }

    /// <summary>初始化窗体属性：标题、大小、位置、外观</summary>
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

    /// <summary>初始化 UI 布局：左侧导航 + 右侧内容区 + 恢复默认按钮</summary>
    private void InitializeUI()
    {
        _splitContainer = new SplitContainer
        {
            Dock = DockStyle.Fill,
            IsSplitterFixed = false,
            SplitterWidth = DpiHelper.Scale(4)
        };

        // 先设置面板最小尺寸和分栏位置，再设置 FixedPanel，避免 WinForms 内部约束检查异常
        try
        {
            _splitContainer.Panel1MinSize = DpiHelper.Scale(140);
            _splitContainer.Panel2MinSize = DpiHelper.Scale(300);
            _splitContainer.SplitterDistance = DpiHelper.Scale(220);
            _splitContainer.FixedPanel = FixedPanel.Panel1;
        }
        catch (InvalidOperationException)
        {
            // SplitContainer 尺寸尚未确定时约束可能无法满足，OnLoad 中会再次设置
            try { _splitContainer.FixedPanel = FixedPanel.Panel1; } catch { }
        }
        _splitContainer.Panel1.BackColor = ThemeService.SidebarBackground;
        _splitContainer.Panel2.BackColor = ThemeService.WindowBackground;

        // 导航栏控件
        _navList = new NavigationListBox
        {
            Dock = DockStyle.Fill,
            BackColor = ThemeService.SidebarBackground
        };
        _navList.SelectedIndexChanged += NavList_SelectedIndexChanged;

        // “恢复所有默认设置”按钮（放在导航栏底部）
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

        // 右侧内容面板
        _contentPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = ThemeService.WindowBackground
        };
        _splitContainer.Panel2.Controls.Add(_contentPanel);

        Controls.Add(_splitContainer);

        // 显示第一个页面（全部记录）
        SwitchPage(0);
    }

    /// <summary>初始化系统托盘图标及右键菜单（显示/监听/退出）</summary>
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

        // 从嵌入资源加载应用图标（优先 PNG，其次 ICO，最后用系统默认图标）
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
                    // 高质量缩放并保留透明通道（PNG 已预处理为透明背景）
                    using var resized = new Bitmap(64, 64, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    using (var g = Graphics.FromImage(resized))
                    {
                        g.Clear(Color.Transparent);
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                        g.DrawImage(original, 0, 0, 64, 64);
                    }
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

    /// <summary>
    /// 窗口首次加载：
    /// 1. 设置分栏约束
    /// 2. 注册剪贴板监听器（AddClipboardFormatListener）
    /// 3. 注册全局唤醒热键
    /// 4. 启动看门狗和保存节流定时器
    /// 5. 清理过期记录和残留临时文件
    /// </summary>
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        // 窗口已有实际大小后再设置分栏约束
        try
        {
            _splitContainer.Panel1MinSize = DpiHelper.Scale(140);
            _splitContainer.Panel2MinSize = DpiHelper.Scale(300);
            _splitContainer.SplitterDistance = DpiHelper.Scale(220);
        }
        catch { }

        // 注册系统剪贴板监听器（内容变化时收到 WM_CLIPBOARDUPDATE）
        Win32Api.AddClipboardFormatListener(Handle);

        // 初始化热键服务并注册唤醒快捷键
        _hotkeyService.Initialize(Handle);
        RegisterWakeHotkey();

        // 启动时始终自动开启剪贴板监听
        _isMonitoring = true;
        UpdateMonitoringUI();

        // 启动剪贴板看门狗定时器（每 30 秒检查监听器是否仍活跃，Win11 下可能丢失）
        _clipboardWatchdog = new System.Windows.Forms.Timer { Interval = 30000 }; // 30秒
        _clipboardWatchdog.Tick += ClipboardWatchdog_Tick;
        _clipboardWatchdog.Start();

        // 初始化记录管理器的节流保存定时器
        _recordManager.InitializeSaveTimer();

        // 清理已过期的记录
        _recordManager.CleanupExpiredRecords();

        // 清理上次崩溃残留的临时文件（崩溃恢复）
        ClipboardService.CleanupStaleTempFiles();

        // 静默启动时直接隐藏到托盘（开机自启场景）
        if (_startSilent)
        {
            BeginInvoke(() => Hide());
        }
    }

    /// <summary>
    /// 窗口关闭处理：
    /// - 用户点 X → 隐藏到托盘而非真正退出
    /// - 程序退出 → 刷新保存、注销监听、清理临时文件
    /// </summary>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_isExiting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        // 清理资源：保存数据、停止定时器、注销监听、准备退出
        _recordManager.FlushSaveData();
        _recordManager.Dispose();
        _clipboardWatchdog?.Stop();
        _clipboardWatchdog?.Dispose();
        Win32Api.RemoveClipboardFormatListener(Handle);
        _hotkeyService.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();

        SaveData();
        FileService.CleanupOrphanedImages(Records);
        ClipboardService.CleanupPendingTempFiles();

        base.OnFormClosing(e);
    }

    /// <summary>失去焦点处理：设置模式下若开启 HideOnLostFocus 则自动隐藏；贴入模式不受影响</summary>
    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        if (_pasteMode || _suppressAutoHide) return; // 贴入模式或对话框交互期间不隐藏
        if (Config.HideOnLostFocus && Visible && !_isExiting)
        {
            BeginInvoke(() =>
            {
                if (!Visible || _isExiting || _suppressAutoHide) return;
                // 如果当前有子对话框（编辑/密码框）活动，不隐藏主窗口
                var active = Form.ActiveForm;
                if (active != null && active != this) return;
                Hide();
            });
        }
    }

    /// <summary>对话框交互期间阻止自动隐藏（密码验证、MessageBox 等场景）</summary>
    public void SuppressAutoHide(bool suppress) => _suppressAutoHide = suppress;

    /// <summary>
    /// 消息循环处理：
    /// - WM_MOUSEACTIVATE + 贴入模式 → 返回 MA_NOACTIVATE 不激活窗口
    /// - WM_CLIPBOARDUPDATE → 触发剪贴板变化处理
    /// - 热键消息 → 交给 HotkeyService 处理
    /// </summary>
    protected override void WndProc(ref Message m)
    {
        // 贴入模式下点击窗口不激活，保持原应用键盘焦点
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

    /// <summary>导航列表选中项变化 → 切换页面</summary>
    private void NavList_SelectedIndexChanged(object? sender, int index)
    {
        SwitchPage(index);
    }

    /// <summary>切换到指定索引的页面（懒加载 + 缓存）</summary>
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

            // 切换到“全部记录”页时自动刷新列表
            if (index == 0 && page is AllRecordsPage recordsPage)
                recordsPage.RefreshRecords();
        }

        _contentPanel.ResumeLayout();
    }

    /// <summary>按索引获取或创建页面实例（0=全部记录, 1=通用, 2=快捷键, 3=外观, 4=安全, 5=导入导出, 6=关于）</summary>
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

    /// <summary>
    /// 剪贴板内容变化回调。
    /// 前置检查（监听状态、自写过滤）后委托给 RecordManager 处理。
    /// </summary>
    private void OnClipboardUpdate()
    {
        if (!_isMonitoring || ClipboardService.IsSelfWriting) return;
        _lastClipboardUpdate = DateTime.UtcNow;
        _recordManager.ProcessClipboardUpdate();
    }

    /// <summary>
    /// 看门狗定时回调（每30秒）。
    /// Win11 下剪贴板监听器可能被意外注销，超过60秒无更新则自动重新注册。
    /// 同时定期清理过期记录。
    /// </summary>
    private void ClipboardWatchdog_Tick(object? sender, EventArgs e)
    {
        try
        {
            if (_isMonitoring && IsHandleCreated && !_isExiting
                && (DateTime.UtcNow - _lastClipboardUpdate).TotalSeconds > 60)
            {
                Win32Api.RemoveClipboardFormatListener(Handle);
                if (!Win32Api.AddClipboardFormatListener(Handle))
                    LogService.Log("Clipboard watchdog: failed to re-register listener");
            }

            _recordManager.CleanupExpiredRecords();

            // 每 5 次看门狗周期（≈2.5 分钟）清理一次残留临时文件（仅删超过 2 分钟的）
            if (++_watchdogTickCount % 5 == 0)
                ClipboardService.CleanupStaleTempFiles(TimeSpan.FromMinutes(2));
        }
        catch (Exception ex)
        {
            LogService.Log("Clipboard watchdog failed", ex);
        }
    }

    #endregion

    #region Record Operations

    /// <summary>复制记录到剪贴板并自动粘贴到原应用</summary>
    public void CopyAndPasteRecord(ClipboardRecord record)
    {
        try
        {
            // 更新复制次数计数器
            record.CurrentCopyCount++;
            SaveData();

            // 写入系统剪贴板（失败则不执行粘贴，避免粘贴旧内容）
            if (!ClipboardService.WriteToClipboard(record))
            {
                LogService.Log($"Failed to write record to clipboard: {record.ContentType}");
                return;
            }

            // 隐藏窗口并自动粘贴
            HideAndPaste();
        }
        catch (Exception ex)
        {
            LogService.Log("Copy and paste failed", ex);
        }
    }

    /// <summary>
    /// 隐藏窗口并向目标窗口发送 Ctrl+V。
    /// - 贴入模式：原应用仍有焦点，直接发送 Ctrl+V
    /// - 设置模式：先激活目标窗口再发送
    /// async void 用于 await 延迟（无反订阅検异常，已用 try-catch 保护）
    /// </summary>
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

                // 轮询等待目标窗口激活（最多 500ms），比固定延时更可靠
                bool activated = false;
                for (int i = 0; i < 10; i++)
                {
                    await Task.Delay(50);
                    if (Win32Api.GetForegroundWindow() == targetWindow)
                    {
                        activated = true;
                        break;
                    }
                }

                if (activated)
                    Win32Api.SendCtrlV();
                else
                {
                    LogService.Log("HideAndPaste: target window activation timed out");
                    // 节流：5 秒内不重复弹气泡（防止连续粘贴失败刷屏）
                    var now = DateTime.UtcNow;
                    if ((now - _lastBalloonTime).TotalSeconds >= 5)
                    {
                        _lastBalloonTime = now;
                        _trayIcon.ShowBalloonTip(2000, "JIE 剪切板",
                            "已复制到剪贴板，但自动粘贴失败（目标窗口未响应）。请手动 Ctrl+V。",
                            ToolTipIcon.Warning);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Log("Hide and paste failed", ex);
        }
    }

    /// <summary>删除单条记录（委托给 RecordManager）</summary>
    public void DeleteRecord(ClipboardRecord record) => _recordManager.DeleteRecord(record);

    /// <summary>清空所有记录（委托给 RecordManager）</summary>
    public void ClearAllRecords() => _recordManager.ClearAllRecords();

    /// <summary>请求保存数据（委托给 RecordManager，节流式）</summary>
    public void SaveData() => _recordManager.SaveData();

    /// <summary>立即刷盘保存（程序退出时调用）</summary>
    private void FlushSaveData() => _recordManager.FlushSaveData();

    #endregion

    #region Window Management

    /// <summary>热键唤醒入口（默认贴入模式）</summary>
    private void ShowMainWindow()
    {
        ShowMainWindow(forPaste: true);
    }

    /// <summary>
    /// 显示主窗口。
    /// - forPaste=true: 贴入模式，TopMost + ShowWithoutActivation，不抢焦点
    /// - forPaste=false: 设置模式，正常激活窗口
    /// - 若已可见则切换为隐藏（Toggle 行为）
    /// </summary>
    private void ShowMainWindow(bool forPaste)
    {
        // 切换行为：如果已显示则隐藏
        if (Visible && !_isExiting)
        {
            _pasteMode = false;
            TopMost = false;
            Hide();
            return;
        }

        // 保存当前前台窗口句柄，用于粘贴时切换回原应用
        _previousForegroundWindow = Win32Api.GetForegroundWindow();

        if (WindowState == FormWindowState.Minimized)
            WindowState = FormWindowState.Normal;

        _pasteMode = forPaste;

        if (forPaste)
        {
            // 贴入模式：显示窗口但不抢焦点（类似 Win+V 行为）
            // 原应用保持键盘焦点，用户可用鼠标点击记录
            TopMost = true;
            Show();
        }
        else
        {
            // 设置模式：正常显示并完全激活窗口
            TopMost = false;
            Show();
            Activate();
            BringToFront();
        }

        // 刷新记录页面
        RefreshCurrentPage();
    }

    /// <summary>刷新当前页面（仅当 AllRecordsPage 已创建时）</summary>
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

    /// <summary>注册全局唤醒热键（来自配置的 Modifiers+Key）</summary>
    private bool RegisterWakeHotkey()
    {
        var hotkey = Config.WakeHotkey;
        bool result = _hotkeyService.RegisterHotkey(
            HotkeyService.HOTKEY_WAKE,
            hotkey.Modifiers,
            hotkey.Key,
            () => ShowMainWindow());

        if (!result)
            LogService.Log($"Failed to register wake hotkey: {hotkey.DisplayText}");
        return result;
    }

    /// <summary>重新注册热键（用户修改快捷键后调用）</summary>
    /// <returns>注册是否成功（false 表示快捷键被其他程序占用）</returns>
    public bool ReregisterHotkey()
    {
        _hotkeyService.UnregisterHotkey(HotkeyService.HOTKEY_WAKE);
        return RegisterWakeHotkey();
    }

    #endregion

    #region Monitoring

    /// <summary>切换剪贴板监听状态</summary>
    private void ToggleMonitoring()
    {
        _isMonitoring = !_isMonitoring;
        UpdateMonitoringUI();
    }

    /// <summary>同步托盘菜单勾选状态和 Tooltip 文本</summary>
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

    /// <summary>
    /// 应用主题更改。
    /// 步骤：更新窗口背景色 → 删除所有缓存页面 → 重建当前页面。
    /// 粗暴但可靠，确保所有控件重新应用新主题色。
    /// </summary>
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

            // 重建所有页面以应用新主题
            for (int i = 0; i < _pages.Length; i++)
            {
                if (_pages[i] != null)
                {
                    try { _pages[i]!.Dispose(); } catch { }
                    _pages[i] = null;
                }
            }

            // 重新创建当前页面
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

    /// <summary>导入配置后应用：替换配置 + 保存 + 重新初始化主题和热键</summary>
    public void ApplyImportedConfig(AppConfig importedConfig)
    {
        Config = importedConfig;
        FileService.SaveConfig(Config);
        ThemeService.Initialize(Config);
        ApplyTheme();
        ReregisterHotkey();
    }

    /// <summary>恢复所有默认设置（不删除记录）</summary>
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

    /// <summary>完全退出应用（设置 _isExiting 以跳过"隐藏"逻辑）</summary>
    private void ExitApplication()
    {
        _isExiting = true;
        Close();
        Application.Exit();
    }

    #endregion
}

