using JIE剪切板.Controls;
using JIE剪切板.Services;

namespace JIE剪切板.Pages;

/// <summary>
/// “安全防护”设置页面。
/// 配置加密记录的默认安全策略（可在编辑记录时覆盖）：
/// - 最大密码尝试次数
/// - 基础锁定时长（指数退避策略：每次超限后锁定时间翻倍）
/// - 超限自动删除（替代锁定）
/// - 是否允许搜索加密内容
/// </summary>
public class SecurityPage : UserControl
{
    private readonly MainForm _mainForm;
    private bool _isLoading; // 加载设置时禁止触发保存
    private NumericUpDown _numMaxAttempts = null!, _numBaseLock = null!;
    private ToggleSwitch _swAutoDelete = null!, _swSearchEncrypted = null!;
    private ToggleSwitch _swSearchEncryptedHint = null!;

    public SecurityPage(MainForm mainForm)
    {
        _mainForm = mainForm;
        Dock = DockStyle.Fill;
        AutoScroll = true;
        BackColor = ThemeService.WindowBackground;
        Padding = new Padding(DpiHelper.Scale(30), DpiHelper.Scale(20), DpiHelper.Scale(30), DpiHelper.Scale(20));
        InitializeControls();
        LoadSettings();
    }

    private void InitializeControls()
    {
        var layout = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            WrapContents = false
        };

        // 页面标题
        layout.Controls.Add(new Label
        {
            Text = "安全防护",
            Font = new Font(ThemeService.GlobalFont.FontFamily, 14f, FontStyle.Bold),
            ForeColor = ThemeService.TextColor,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 5)
        });

        layout.Controls.Add(new Label
        {
            Text = "配置加密记录的默认安全策略（可在编辑记录时覆盖）",
            ForeColor = ThemeService.SecondaryTextColor,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 15)
        });

        // 最大密码尝试次数
        var attemptPanel = CreateSettingPanel("最大密码尝试次数：",
            "超过此次数后将触发锁定或删除操作");
        _numMaxAttempts = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 100,
            Value = 3,
            Width = 80,
            Margin = new Padding(10, 0, 0, 0)
        };
        _numMaxAttempts.ValueChanged += (_, _) => SaveSettings();
        var attemptRow = (FlowLayoutPanel)attemptPanel.Controls[0];
        attemptRow.Controls.Add(_numMaxAttempts);
        var attemptUnit = new Label { Text = "次", AutoSize = true, ForeColor = ThemeService.TextColor, Margin = new Padding(5, 3, 0, 0) };
        attemptRow.Controls.Add(attemptUnit);
        layout.Controls.Add(attemptPanel);

        // 基础锁定时长
        var lockPanel = CreateSettingPanel("基础锁定时长：",
            "每次超过尝试次数后锁定时间翻倍（累计锁定次数越多，等待越久）");
        _numBaseLock = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 10080,
            Value = 60,
            Width = 80,
            Margin = new Padding(10, 0, 0, 0)
        };
        _numBaseLock.ValueChanged += (_, _) => SaveSettings();
        var lockRow = (FlowLayoutPanel)lockPanel.Controls[0];
        lockRow.Controls.Add(_numBaseLock);
        var lockUnit = new Label { Text = "分钟", AutoSize = true, ForeColor = ThemeService.TextColor, Margin = new Padding(5, 3, 0, 0) };
        lockRow.Controls.Add(lockUnit);
        layout.Controls.Add(lockPanel);

        // 锁定公式说明
        var formulaLabel = new Label
        {
            Text = "锁定公式: 实际锁定时长 = 基础锁定时长 × 2^(累计锁定次数-1)",
            ForeColor = ThemeService.SecondaryTextColor,
            Font = new Font(ThemeService.GlobalFont.FontFamily, 8.5f, FontStyle.Italic),
            AutoSize = true,
            Margin = new Padding(10, 0, 0, 15)
        };
        layout.Controls.Add(formulaLabel);

        // 超限自动删除开关
        var deletePanel = CreateSettingPanel("超限自动删除：",
            "启用后，超过最大尝试次数将直接删除加密记录（而不是锁定）");
        _swAutoDelete = new ToggleSwitch { Margin = new Padding(10, 0, 0, 0) };
        _swAutoDelete.CheckedChanged += (_, _) => SaveSettings();
        var deleteRow = (FlowLayoutPanel)deletePanel.Controls[0];
        deleteRow.Controls.Add(_swAutoDelete);
        layout.Controls.Add(deletePanel);

        // 自动删除警告标签
        var warningLabel = new Label
        {
            Text = "⚠ 警告：启用自动删除后，超过尝试次数的加密记录将被永久删除，无法恢复！",
            ForeColor = Color.FromArgb(220, 53, 69),
            AutoSize = true,
            MaximumSize = new Size(DpiHelper.Scale(550), 0),
            Margin = new Padding(DpiHelper.Scale(10), 0, 0, DpiHelper.Scale(15))
        };
        layout.Controls.Add(warningLabel);

        // 加密搜索设置
        var searchPanel = CreateSettingPanel("允许搜索加密内容：",
            "关闭时，搜索只匹配\"加密内容\"关键字");
        _swSearchEncrypted = new ToggleSwitch { Margin = new Padding(DpiHelper.Scale(10), 0, 0, 0) };
        _swSearchEncrypted.CheckedChanged += SwSearchEncrypted_Changed;
        var searchRow = (FlowLayoutPanel)searchPanel.Controls[0];
        searchRow.Controls.Add(_swSearchEncrypted);
        layout.Controls.Add(searchPanel);

        // 加密提示搜索设置
        var hintPanel = CreateSettingPanel("允许搜索加密提示：",
            "关闭时，搜索不会匹配加密提示字段，仅支持内容或\"加密内容\"关键字。");
        _swSearchEncryptedHint = new ToggleSwitch { Margin = new Padding(DpiHelper.Scale(10), 0, 0, 0) };
        _swSearchEncryptedHint.CheckedChanged += (_, _) => SaveSettings();
        var hintRow = (FlowLayoutPanel)hintPanel.Controls[0];
        hintRow.Controls.Add(_swSearchEncryptedHint);
        layout.Controls.Add(hintPanel);

        var searchWarning = new Label
        {
            Text = "⚠ 启用搜索加密内容可能需要临时解密数据，存在安全风险。请谨慎使用。",
            ForeColor = Color.FromArgb(255, 140, 0),
            AutoSize = true,
            MaximumSize = new Size(DpiHelper.Scale(550), 0),
            Margin = new Padding(DpiHelper.Scale(10), 0, 0, DpiHelper.Scale(15))
        };
        layout.Controls.Add(searchWarning);

        Controls.Add(layout);
    }

    /// <summary>创建设置项容器（标题行 + 描述文本），使用垂直 FlowLayout 确保间距一致</summary>
    private Panel CreateSettingPanel(string labelText, string descText)
    {
        // 外层垂直 FlowLayout：标题行在上，描述在下，自动管理高度
        var container = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Width = DpiHelper.Scale(600),
            Margin = new Padding(0, DpiHelper.Scale(14), 0, DpiHelper.Scale(6))
        };

        // 标题行（水平排列：标签 + 控件，控件由调用方后续添加）
        var row = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, DpiHelper.Scale(2))
        };
        row.Controls.Add(new Label
        {
            Text = labelText,
            AutoSize = true,
            ForeColor = ThemeService.TextColor,
            Font = new Font(ThemeService.GlobalFont.FontFamily, 10f, FontStyle.Bold),
            Margin = new Padding(DpiHelper.Scale(10), DpiHelper.Scale(3), 0, 0)
        });
        container.Controls.Add(row);

        // 描述文本（紧跟标题行下方，灰色小字）
        var desc = new Label
        {
            Text = descText,
            ForeColor = ThemeService.SecondaryTextColor,
            AutoSize = true,
            MaximumSize = new Size(DpiHelper.Scale(550), 0),
            Margin = new Padding(DpiHelper.Scale(10), 0, 0, DpiHelper.Scale(4))
        };
        container.Controls.Add(desc);

        return container;
    }

    private void LoadSettings()
    {
        _isLoading = true;
        try
        {
        var config = _mainForm.Config;
        _numMaxAttempts.Value = Math.Max(_numMaxAttempts.Minimum, Math.Min(_numMaxAttempts.Maximum, config.DefaultMaxPasswordAttempts));
        _numBaseLock.Value = Math.Max(_numBaseLock.Minimum, Math.Min(_numBaseLock.Maximum, config.DefaultBaseLockMinutes));
        _swAutoDelete.Checked = config.DefaultAutoDeleteOnExceed;
        _swSearchEncrypted.Checked = config.AllowSearchEncryptedContent;
        _swSearchEncryptedHint.Checked = config.AllowSearchEncryptedHint;
        }
        finally { _isLoading = false; }
    }

    private void SaveSettings()
    {
        if (_isLoading) return;
        try
        {
            var config = _mainForm.Config;
            config.DefaultMaxPasswordAttempts = (int)_numMaxAttempts.Value;
            config.DefaultBaseLockMinutes = (int)_numBaseLock.Value;
            config.DefaultAutoDeleteOnExceed = _swAutoDelete.Checked;
            config.AllowSearchEncryptedContent = _swSearchEncrypted.Checked;
            config.AllowSearchEncryptedHint = _swSearchEncryptedHint.Checked;
            FileService.SaveConfig(config);
        }
        catch (Exception ex)
        {
            LogService.Log("Failed to save security settings", ex);
        }
    }

    /// <summary>“允许搜索加密内容”开关变化：开启时显示安全警告确认框</summary>
    private void SwSearchEncrypted_Changed(object? sender, EventArgs e)
    {
        if (_isLoading) return;
        if (_swSearchEncrypted.Checked)
        {
            var result = MessageBox.Show(this,
                "启用搜索加密内容需要临时解密数据进行匹配，这可能带来以下风险：\n\n" +
                "• 解密后的内容会临时存在内存中\n" +
                "• 程序异常时可能导致数据残留\n\n" +
                "确定要启用吗？",
                "安全提示",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (result != DialogResult.Yes)
            {
                _swSearchEncrypted.Checked = false;
                return;
            }
        }
        SaveSettings();
    }
}
