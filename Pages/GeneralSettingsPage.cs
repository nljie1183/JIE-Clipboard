using JIE剪切板.Controls;
using JIE剪切板.Models;
using JIE剪切板.Services;
using System.Diagnostics;

namespace JIE剪切板.Pages;

/// <summary>
/// “通用设置”页面。
/// 包含以下设置分区：
/// - 存储限制：最大记录数、单条最大大小、内容去重
/// - 系统：开机自启动、自动监听、失焦隐藏
/// - 记录类型：按类型过滤、后缀名包含/排除
/// - 记录方式：按类型开启持久化加密存储
/// - 数据存储：自定义存储位置、数据迁移
/// </summary>
public class GeneralSettingsPage : UserControl
{
    private readonly MainForm _mainForm;

    // 存储限制控件
    private ToggleSwitch _swMaxCount = null!, _swMaxSize = null!, _swDedup = null!;
    // 系统设置控件
    private ToggleSwitch _swAutoStart = null!, _swAutoMonitor = null!, _swHideOnLostFocus = null!;
    private NumericUpDown _numMaxCount = null!, _numMaxSize = null!;
    private ComboBox _cboSizeUnit = null!;
    private Label _dataPathLabel = null!;

    // 记录类型过滤控件
    private CheckBox _chkPlainText = null!, _chkRichText = null!, _chkImage = null!,
                     _chkFileDrop = null!, _chkVideo = null!, _chkFolder = null!;
    private TextBox _txtIncludeExt = null!, _txtExcludeExt = null!;

    // 持久化加密存储控件（按类型）
    private CheckBox _chkPersistImage = null!, _chkPersistFile = null!,
                     _chkPersistVideo = null!, _chkPersistFolder = null!;
    private NumericUpDown _numMaxPersistSize = null!;

    public GeneralSettingsPage(MainForm mainForm)
    {
        _mainForm = mainForm;
        Dock = DockStyle.Fill;
        AutoScroll = true;
        BackColor = ThemeService.WindowBackground;
        Padding = new Padding(DpiHelper.Scale(30), DpiHelper.Scale(20), DpiHelper.Scale(30), DpiHelper.Scale(20));
        InitializeControls();
        LoadSettings();
    }

    /// <summary>初始化所有设置控件，使用 TableLayoutPanel 布局</summary>
    private void InitializeControls()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 3,
            Padding = new Padding(0)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, DpiHelper.Scale(200)));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, DpiHelper.Scale(250)));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        int row = 0;

        // 分区：存储限制
        AddSectionHeader(layout, "存储限制", ref row);

        // 最大记录数
        _swMaxCount = new ToggleSwitch();
                _numMaxCount = new NumericUpDown { Minimum = 1, Maximum = 10000000, Value = 1000, Width = DpiHelper.Scale(120) };
        _swMaxCount.CheckedChanged += (_, _) => { _numMaxCount.Enabled = _swMaxCount.Checked; SaveSettings(); };
        AddSettingRow(layout, "限制最大记录数", _swMaxCount, _numMaxCount, ref row);

        // 单条最大大小限制
        _swMaxSize = new ToggleSwitch();
        var sizePanel = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = false };
        _numMaxSize = new NumericUpDown { Minimum = 1, Maximum = 102400, Value = 100, Width = DpiHelper.Scale(100) };
        _cboSizeUnit = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = DpiHelper.Scale(60), Items = { "KB", "MB", "GB" } };
        _cboSizeUnit.SelectedItem = "MB";
        sizePanel.Controls.Add(_numMaxSize);
        sizePanel.Controls.Add(_cboSizeUnit);
        _swMaxSize.CheckedChanged += (_, _) => { _numMaxSize.Enabled = _swMaxSize.Checked; _cboSizeUnit.Enabled = _swMaxSize.Checked; SaveSettings(); };
        AddSettingRow(layout, "限制单条最大大小", _swMaxSize, sizePanel, ref row);

        // 去重开关
        _swDedup = new ToggleSwitch();
        _swDedup.CheckedChanged += (_, _) => SaveSettings();
        AddSettingRow(layout, "内容去重", _swDedup, new Label { Text = "自动去除重复的剪贴板内容", AutoSize = true, ForeColor = ThemeService.SecondaryTextColor }, ref row);

        // 分区：系统设置
        AddSectionHeader(layout, "系统", ref row);

        // 开机自启动
        _swAutoStart = new ToggleSwitch();
        _swAutoStart.CheckedChanged += (_, _) => { UpdateAutoStart(); SaveSettings(); };
        AddSettingRow(layout, "开机自启动", _swAutoStart, null, ref row);

        // 启动时自动监听
        _swAutoMonitor = new ToggleSwitch();
        _swAutoMonitor.CheckedChanged += (_, _) => SaveSettings();
        AddSettingRow(layout, "启动时自动监听", _swAutoMonitor, null, ref row);

        // 失焦自动隐藏
        _swHideOnLostFocus = new ToggleSwitch();
        _swHideOnLostFocus.CheckedChanged += (_, _) => SaveSettings();
        AddSettingRow(layout, "失焦自动隐藏", _swHideOnLostFocus, new Label { Text = "窗口失去焦点时自动隐藏到托盘", AutoSize = true, ForeColor = ThemeService.SecondaryTextColor }, ref row);

        // 分区：记录类型过滤
        AddSectionHeader(layout, "记录类型", ref row);

        // 类型复选框（纯文本、富文本、图片、文件、视频、文件夹）
        layout.RowCount = row + 1;
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, DpiHelper.Scale(40)));
        var typeLbl = new Label
        {
            Text = "记录以下类型",
            AutoSize = true,
            ForeColor = ThemeService.TextColor,
            Anchor = AnchorStyles.Left,
            Padding = new Padding(DpiHelper.Scale(10), DpiHelper.Scale(8), 0, 0)
        };
        layout.Controls.Add(typeLbl, 0, row);

        var typeFlow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            WrapContents = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, DpiHelper.Scale(4), 0, 0)
        };
        _chkPlainText = new CheckBox { Text = "纯文本", AutoSize = true, ForeColor = ThemeService.TextColor, Margin = new Padding(0, 0, DpiHelper.Scale(12), 0) };
        _chkRichText = new CheckBox { Text = "富文本", AutoSize = true, ForeColor = ThemeService.TextColor, Margin = new Padding(0, 0, DpiHelper.Scale(12), 0) };
        _chkImage = new CheckBox { Text = "图片", AutoSize = true, ForeColor = ThemeService.TextColor, Margin = new Padding(0, 0, DpiHelper.Scale(12), 0) };
        _chkFileDrop = new CheckBox { Text = "文件", AutoSize = true, ForeColor = ThemeService.TextColor, Margin = new Padding(0, 0, DpiHelper.Scale(12), 0) };
        _chkVideo = new CheckBox { Text = "视频", AutoSize = true, ForeColor = ThemeService.TextColor, Margin = new Padding(0, 0, DpiHelper.Scale(12), 0) };
        _chkFolder = new CheckBox { Text = "文件夹", AutoSize = true, ForeColor = ThemeService.TextColor };
        foreach (var chk in new[] { _chkPlainText, _chkRichText, _chkImage, _chkFileDrop, _chkVideo, _chkFolder })
        {
            chk.CheckedChanged += (_, _) => SaveSettings();
            typeFlow.Controls.Add(chk);
        }
        layout.Controls.Add(typeFlow, 1, row);
        layout.SetColumnSpan(typeFlow, 2);
        row++;

        // 仅记录指定后缀名的文件
        layout.RowCount = row + 1;
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, DpiHelper.Scale(40)));
        var inclLbl = new Label
        {
            Text = "仅记录后缀",
            AutoSize = true,
            ForeColor = ThemeService.TextColor,
            Anchor = AnchorStyles.Left,
            Padding = new Padding(DpiHelper.Scale(10), DpiHelper.Scale(8), 0, 0)
        };
        layout.Controls.Add(inclLbl, 0, row);

        var inclPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Left, Margin = new Padding(0, DpiHelper.Scale(5), 0, 0) };
        _txtIncludeExt = new TextBox
        {
            Width = DpiHelper.Scale(250),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = ThemeService.IsDarkMode ? Color.FromArgb(50, 50, 50) : Color.White,
            ForeColor = ThemeService.TextColor,
            PlaceholderText = "留空=全部，如 .doc,.pdf,.xlsx"
        };
        _txtIncludeExt.Leave += (_, _) => SaveSettings();
        inclPanel.Controls.Add(_txtIncludeExt);
        var inclTip = new Label
        {
            Text = "多个后缀用逗号分隔，设置后仅记录匹配的文件",
            AutoSize = true,
            ForeColor = ThemeService.SecondaryTextColor,
            Font = new Font(ThemeService.GlobalFont.FontFamily, 8f),
            Margin = new Padding(DpiHelper.Scale(8), DpiHelper.Scale(5), 0, 0)
        };
        inclPanel.Controls.Add(inclTip);
        layout.Controls.Add(inclPanel, 1, row);
        layout.SetColumnSpan(inclPanel, 2);
        row++;

        // 排除指定后缀名的文件
        layout.RowCount = row + 1;
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, DpiHelper.Scale(40)));
        var exclLbl = new Label
        {
            Text = "不记录后缀",
            AutoSize = true,
            ForeColor = ThemeService.TextColor,
            Anchor = AnchorStyles.Left,
            Padding = new Padding(DpiHelper.Scale(10), DpiHelper.Scale(8), 0, 0)
        };
        layout.Controls.Add(exclLbl, 0, row);

        var exclPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Left, Margin = new Padding(0, DpiHelper.Scale(5), 0, 0) };
        _txtExcludeExt = new TextBox
        {
            Width = DpiHelper.Scale(250),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = ThemeService.IsDarkMode ? Color.FromArgb(50, 50, 50) : Color.White,
            ForeColor = ThemeService.TextColor,
            PlaceholderText = "如 .tmp,.log,.bak"
        };
        _txtExcludeExt.Leave += (_, _) => SaveSettings();
        exclPanel.Controls.Add(_txtExcludeExt);
        var exclTip = new Label
        {
            Text = "匹配的文件将被忽略不记录",
            AutoSize = true,
            ForeColor = ThemeService.SecondaryTextColor,
            Font = new Font(ThemeService.GlobalFont.FontFamily, 8f),
            Margin = new Padding(DpiHelper.Scale(8), DpiHelper.Scale(5), 0, 0)
        };
        exclPanel.Controls.Add(exclTip);
        layout.Controls.Add(exclPanel, 1, row);
        layout.SetColumnSpan(exclPanel, 2);
        row++;

        // 分区：记录方式（持久化加密存储）
        AddSectionHeader(layout, "记录方式", ref row);

        // 说明文本
        layout.RowCount = row + 1;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var persistExplain = new Label
        {
            Text = "当前存储方式说明：\n" +
                   "• 纯文本/富文本：内容直接存储在本地加密数据库（records.dat），使用 Windows DPAPI 加密，仅当前用户可解密，无需额外设置。\n" +
                   "• 图片/文件/视频/文件夹：默认仅加密记录路径，原始文件本身不加密。\n\n" +
                   "以下可按类型单独开启「持久化加密存储」：\n" +
                   "• 图片：图片文件将被 DPAPI 加密，无法被直接浏览。\n" +
                   "• 文件/视频：文件将被复制到本地并加密，原文件删除后记录仍可使用。\n" +
                   "• 文件夹：整个文件夹将被打包压缩并加密存储，原文件夹删除后记录仍可使用。",
            AutoSize = true,
            MaximumSize = new Size(DpiHelper.Scale(550), 0),
            ForeColor = ThemeService.SecondaryTextColor,
            Font = new Font(ThemeService.GlobalFont.FontFamily, 8.5f),
            Anchor = AnchorStyles.Left,
            Padding = new Padding(DpiHelper.Scale(10), 0, 0, 0)
        };
        layout.Controls.Add(persistExplain, 0, row);
        layout.SetColumnSpan(persistExplain, 3);
        row++;

        // 按类型开启持久化加密的复选框
        layout.RowCount = row + 1;
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, DpiHelper.Scale(40)));
        var persistTypeLbl = new Label
        {
            Text = "加密以下类型",
            AutoSize = true,
            ForeColor = ThemeService.TextColor,
            Anchor = AnchorStyles.Left,
            Padding = new Padding(DpiHelper.Scale(10), DpiHelper.Scale(8), 0, 0)
        };
        layout.Controls.Add(persistTypeLbl, 0, row);

        var persistTypeFlow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            WrapContents = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, DpiHelper.Scale(4), 0, 0)
        };
        _chkPersistImage = new CheckBox { Text = "图片", AutoSize = true, ForeColor = ThemeService.TextColor, Margin = new Padding(0, 0, DpiHelper.Scale(12), 0) };
        _chkPersistFile = new CheckBox { Text = "文件", AutoSize = true, ForeColor = ThemeService.TextColor, Margin = new Padding(0, 0, DpiHelper.Scale(12), 0) };
        _chkPersistVideo = new CheckBox { Text = "视频", AutoSize = true, ForeColor = ThemeService.TextColor, Margin = new Padding(0, 0, DpiHelper.Scale(12), 0) };
        _chkPersistFolder = new CheckBox { Text = "文件夹", AutoSize = true, ForeColor = ThemeService.TextColor };
        foreach (var chk in new[] { _chkPersistImage, _chkPersistFile, _chkPersistVideo, _chkPersistFolder })
        {
            chk.CheckedChanged += (_, _) =>
            {
                _numMaxPersistSize.Enabled = _chkPersistImage.Checked || _chkPersistFile.Checked || _chkPersistVideo.Checked || _chkPersistFolder.Checked;
                SaveSettings();
            };
            persistTypeFlow.Controls.Add(chk);
        }
        layout.Controls.Add(persistTypeFlow, 1, row);
        layout.SetColumnSpan(persistTypeFlow, 2);
        row++;

        // 警告标签：持久化加密会增加存储空间占用
        layout.RowCount = row + 1;
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, DpiHelper.Scale(55)));
        var persistWarn = new Label
        {
            Text = "⚠ 注意：开启持久化加密会大幅增加存储空间占用（文件将被完整复制并加密）。\n   建议将下方「存储位置」设置为非 C 盘的大容量磁盘。",
            AutoSize = true,
            MaximumSize = new Size(DpiHelper.Scale(550), 0),
            ForeColor = Color.FromArgb(220, 53, 69),
            Font = new Font(ThemeService.GlobalFont.FontFamily, 8.5f),
            Anchor = AnchorStyles.Left,
            Padding = new Padding(DpiHelper.Scale(10), 0, 0, 0)
        };
        layout.Controls.Add(persistWarn, 0, row);
        layout.SetColumnSpan(persistWarn, 3);
        row++;

        // 最大持久化文件大小限制
        layout.RowCount = row + 1;
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, DpiHelper.Scale(40)));
        var persistSizeLbl = new Label
        {
            Text = "最大持久化文件",
            AutoSize = true,
            ForeColor = ThemeService.TextColor,
            Anchor = AnchorStyles.Left,
            Padding = new Padding(DpiHelper.Scale(10), DpiHelper.Scale(8), 0, 0)
        };
        layout.Controls.Add(persistSizeLbl, 0, row);

        var persistSizePanel = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Left, Margin = new Padding(0, DpiHelper.Scale(5), 0, 0) };
        _numMaxPersistSize = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 102400,
            Value = 50,
            Width = DpiHelper.Scale(80),
            Enabled = false
        };
        _numMaxPersistSize.ValueChanged += (_, _) => SaveSettings();
        persistSizePanel.Controls.Add(_numMaxPersistSize);
        var mbLabel = new Label { Text = "MB（超过此大小的文件/文件夹仅记录路径）", AutoSize = true, ForeColor = ThemeService.SecondaryTextColor, Font = new Font(ThemeService.GlobalFont.FontFamily, 8.5f), Margin = new Padding(DpiHelper.Scale(5), DpiHelper.Scale(4), 0, 0) };
        persistSizePanel.Controls.Add(mbLabel);
        layout.Controls.Add(persistSizePanel, 1, row);
        layout.SetColumnSpan(persistSizePanel, 2);
        row++;

        // 分区：数据存储位置
        AddSectionHeader(layout, "数据存储", ref row);

        // 数据存储路径行
        layout.RowCount = row + 1;
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, DpiHelper.Scale(40)));
        var pathTitleLabel = new Label
        {
            Text = "存储位置",
            AutoSize = true,
            ForeColor = ThemeService.TextColor,
            Anchor = AnchorStyles.Left,
            Padding = new Padding(DpiHelper.Scale(10), DpiHelper.Scale(8), 0, 0)
        };
        layout.Controls.Add(pathTitleLabel, 0, row);

        var dataPathPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            WrapContents = false,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 4, 0, 0)
        };

        _dataPathLabel = new Label
        {
            Text = FileService.DataFolder,
            ForeColor = ThemeService.ThemeColor,
            AutoSize = true,
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 4, 10, 0)
        };
        _dataPathLabel.Click += (_, _) =>
        {
            try
            {
                if (Directory.Exists(FileService.DataFolder))
                    Process.Start(new ProcessStartInfo("explorer.exe", FileService.DataFolder) { UseShellExecute = true });
            }
            catch { }
        };

        var btnChangePath = new Button
        {
            Text = "更改位置",
            FlatStyle = FlatStyle.Flat,
            Size = new Size(DpiHelper.Scale(80), DpiHelper.Scale(28)),
            ForeColor = ThemeService.ThemeColor,
            BackColor = ThemeService.WindowBackground
        };
        btnChangePath.FlatAppearance.BorderColor = ThemeService.ThemeColor;
        btnChangePath.Click += BtnChangePath_Click;

        var btnResetPath = new Button
        {
            Text = "恢复默认",
            FlatStyle = FlatStyle.Flat,
            Size = new Size(DpiHelper.Scale(80), DpiHelper.Scale(28)),
            ForeColor = ThemeService.SecondaryTextColor,
            BackColor = ThemeService.WindowBackground,
            Margin = new Padding(5, 0, 0, 0)
        };
        btnResetPath.FlatAppearance.BorderColor = ThemeService.BorderColor;
        btnResetPath.Click += BtnResetPath_Click;

        dataPathPanel.Controls.Add(_dataPathLabel);
        dataPathPanel.Controls.Add(btnChangePath);
        dataPathPanel.Controls.Add(btnResetPath);

        layout.Controls.Add(dataPathPanel, 1, row);
        layout.SetColumnSpan(dataPathPanel, 2);
        row++;

        Controls.Add(layout);
    }

    /// <summary>添加分区标题（加粗、跨列）</summary>
    private void AddSectionHeader(TableLayoutPanel layout, string text, ref int row)
    {
        layout.RowCount = row + 1;
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, DpiHelper.Scale(45)));
        var lbl = new Label
        {
            Text = text,
            Font = new Font(ThemeService.GlobalFont.FontFamily, 12f, FontStyle.Bold),
            ForeColor = ThemeService.TextColor,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Padding = new Padding(0, DpiHelper.Scale(10), 0, 0)
        };
        layout.Controls.Add(lbl, 0, row);
        layout.SetColumnSpan(lbl, 3);
        row++;
    }

    /// <summary>添加一行设置项：标签 + 开关 + 可选额外控件</summary>
    private void AddSettingRow(TableLayoutPanel layout, string label, ToggleSwitch toggle, Control? extra, ref int row)
    {
        layout.RowCount = row + 1;
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, DpiHelper.Scale(40)));

        var lbl = new Label
        {
            Text = label,
            AutoSize = true,
            ForeColor = ThemeService.TextColor,
            Anchor = AnchorStyles.Left,
            Padding = new Padding(DpiHelper.Scale(10), DpiHelper.Scale(8), 0, 0)
        };
        layout.Controls.Add(lbl, 0, row);
        layout.Controls.Add(toggle, 1, row);
        toggle.Anchor = AnchorStyles.Left;
        toggle.Margin = new Padding(0, DpiHelper.Scale(8), 0, 0);

        if (extra != null)
        {
            extra.Anchor = AnchorStyles.Left;
            extra.Margin = new Padding(DpiHelper.Scale(10), DpiHelper.Scale(5), 0, 0);
            layout.Controls.Add(extra, 2, row);
        }
        row++;
    }

    /// <summary>从 AppConfig 加载设置到 UI 控件</summary>
    private void LoadSettings()
    {
        var config = _mainForm.Config;
        _swMaxCount.Checked = config.MaxRecordCountEnabled;
        _numMaxCount.Value = Math.Max(_numMaxCount.Minimum, Math.Min(_numMaxCount.Maximum, config.MaxRecordCount));
        _numMaxCount.Enabled = config.MaxRecordCountEnabled;

        _swMaxSize.Checked = config.MaxContentSizeEnabled;
        _cboSizeUnit.SelectedItem = config.MaxContentSizeUnit;
        long displayValue = config.MaxContentSizeUnit switch
        {
            "GB" => config.MaxContentSizeKB / 1048576,
            "MB" => config.MaxContentSizeKB / 1024,
            _ => config.MaxContentSizeKB
        };
        _numMaxSize.Value = Math.Max(_numMaxSize.Minimum, Math.Min(_numMaxSize.Maximum, displayValue));
        _numMaxSize.Enabled = config.MaxContentSizeEnabled;
        _cboSizeUnit.Enabled = config.MaxContentSizeEnabled;

        _swDedup.Checked = config.EnableDuplicateRemoval;
        _swAutoStart.Checked = config.AutoStartOnBoot;
        _swAutoMonitor.Checked = config.AutoStartMonitoring;
        _swHideOnLostFocus.Checked = config.HideOnLostFocus;

        // 记录类型过滤设置
        _chkPlainText.Checked = config.RecordPlainText;
        _chkRichText.Checked = config.RecordRichText;
        _chkImage.Checked = config.RecordImage;
        _chkFileDrop.Checked = config.RecordFileDrop;
        _chkVideo.Checked = config.RecordVideo;
        _chkFolder.Checked = config.RecordFolder;
        _txtIncludeExt.Text = config.IncludeExtensions;
        _txtExcludeExt.Text = config.ExcludeExtensions;

        // 持久化加密存储设置（按类型）
        _chkPersistImage.Checked = config.PersistImage;
        _chkPersistFile.Checked = config.PersistFileDrop;
        _chkPersistVideo.Checked = config.PersistVideo;
        _chkPersistFolder.Checked = config.PersistFolder;
        _numMaxPersistSize.Value = Math.Max(_numMaxPersistSize.Minimum, Math.Min(_numMaxPersistSize.Maximum, config.MaxPersistFileSizeMB));
        _numMaxPersistSize.Enabled = config.PersistImage || config.PersistFileDrop || config.PersistVideo || config.PersistFolder;
    }

    /// <summary>将 UI 控件的值保存回 AppConfig 并写入磁盘</summary>
    private void SaveSettings()
    {
        try
        {
            var config = _mainForm.Config;
            config.MaxRecordCountEnabled = _swMaxCount.Checked;
            config.MaxRecordCount = (int)_numMaxCount.Value;

            config.MaxContentSizeEnabled = _swMaxSize.Checked;
            config.MaxContentSizeUnit = _cboSizeUnit.SelectedItem?.ToString() ?? "MB";
            long multiplier = config.MaxContentSizeUnit switch
            {
                "GB" => 1048576,
                "MB" => 1024,
                _ => 1
            };
            config.MaxContentSizeKB = (long)_numMaxSize.Value * multiplier;

            config.EnableDuplicateRemoval = _swDedup.Checked;
            config.AutoStartOnBoot = _swAutoStart.Checked;
            config.AutoStartMonitoring = _swAutoMonitor.Checked;
            config.HideOnLostFocus = _swHideOnLostFocus.Checked;

            // 记录类型过滤设置
            config.RecordPlainText = _chkPlainText.Checked;
            config.RecordRichText = _chkRichText.Checked;
            config.RecordImage = _chkImage.Checked;
            config.RecordFileDrop = _chkFileDrop.Checked;
            config.RecordVideo = _chkVideo.Checked;
            config.RecordFolder = _chkFolder.Checked;
            config.IncludeExtensions = _txtIncludeExt.Text.Trim();
            config.ExcludeExtensions = _txtExcludeExt.Text.Trim();

            // 持久化加密存储设置（按类型）
            config.PersistImage = _chkPersistImage.Checked;
            config.PersistFileDrop = _chkPersistFile.Checked;
            config.PersistVideo = _chkPersistVideo.Checked;
            config.PersistFolder = _chkPersistFolder.Checked;
            config.MaxPersistFileSizeMB = (int)_numMaxPersistSize.Value;

            FileService.SaveConfig(config);
        }
        catch (Exception ex)
        {
            LogService.Log("Failed to save general settings", ex);
        }
    }

    /// <summary>更新 Windows 开机自启动注册表项</summary>
    private void UpdateAutoStart()
    {
        try
        {
            var exePath = Application.ExecutablePath;
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
            if (key == null) return;

            if (_swAutoStart.Checked)
                key.SetValue("JIE剪切板", $"\"{exePath}\"");
            else
                key.DeleteValue("JIE剪切板", false);
        }
        catch (Exception ex)
        {
            LogService.Log("Failed to update auto-start", ex);
        }
    }

    /// <summary>更改数据存储位置，可选择是否迁移已有数据</summary>
    private void BtnChangePath_Click(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择数据存储位置",
            UseDescriptionForTitle = true,
            SelectedPath = FileService.DataFolder
        };

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        var newPath = dialog.SelectedPath;
        if (string.Equals(newPath, FileService.DataFolder, StringComparison.OrdinalIgnoreCase))
            return;

        var moveResult = MessageBox.Show(this,
            $"是否将现有数据迁移到新位置？\n\n新位置: {newPath}",
            "迁移数据",
            MessageBoxButtons.YesNoCancel,
            MessageBoxIcon.Question);

        if (moveResult == DialogResult.Cancel) return;

        if (moveResult == DialogResult.Yes)
        {
            if (!FileService.MoveDataToFolder(newPath))
            {
                MessageBox.Show(this, "数据迁移失败，请检查目标文件夹权限。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
        }
        else
        {
            FileService.SetCustomDataFolder(newPath);
        }

        _mainForm.Config.CustomDataFolder = newPath;
        FileService.SaveConfig(_mainForm.Config);
        _dataPathLabel.Text = FileService.DataFolder;

        // 从新位置重新加载记录
        _mainForm.Records.Clear();
        _mainForm.Records.AddRange(FileService.LoadRecords());
        _mainForm.RefreshCurrentPage();

        MessageBox.Show(this, "数据存储位置已更改。", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    /// <summary>恢复默认存储位置</summary>
    private void BtnResetPath_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrEmpty(_mainForm.Config.CustomDataFolder))
            return;

        var result = MessageBox.Show(this,
            "确定要恢复到默认存储位置吗？\n已有数据不会自动迁移回来。",
            "恢复默认",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (result != DialogResult.Yes) return;

        _mainForm.Config.CustomDataFolder = "";
        FileService.SetCustomDataFolder(null);
        FileService.SaveConfig(_mainForm.Config);
        _dataPathLabel.Text = FileService.DataFolder;

        // 从默认位置重新加载记录
        _mainForm.Records.Clear();
        _mainForm.Records.AddRange(FileService.LoadRecords());
        _mainForm.RefreshCurrentPage();

        MessageBox.Show(this, "已恢复到默认存储位置。", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
