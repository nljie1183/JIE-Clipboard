using JIE剪切板.Controls;
using JIE剪切板.Services;

namespace JIE剪切板.Pages;

public class SecurityPage : UserControl
{
    private readonly MainForm _mainForm;
    private NumericUpDown _numMaxAttempts = null!, _numBaseLock = null!;
    private ToggleSwitch _swAutoDelete = null!, _swSearchEncrypted = null!;

    public SecurityPage(MainForm mainForm)
    {
        _mainForm = mainForm;
        Dock = DockStyle.Fill;
        AutoScroll = true;
        BackColor = ThemeService.WindowBackground;
        Padding = new Padding(30, 20, 30, 20);
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

        // Title
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

        // Max password attempts
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

        // Base lock duration
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

        // Lock formula explanation
        var formulaLabel = new Label
        {
            Text = "锁定公式: 实际锁定时长 = 基础锁定时长 × 2^(累计锁定次数-1)",
            ForeColor = ThemeService.SecondaryTextColor,
            Font = new Font(ThemeService.GlobalFont.FontFamily, 8.5f, FontStyle.Italic),
            AutoSize = true,
            Margin = new Padding(10, 0, 0, 15)
        };
        layout.Controls.Add(formulaLabel);

        // Auto delete
        var deletePanel = CreateSettingPanel("超限自动删除：",
            "启用后，超过最大尝试次数将直接删除加密记录（而不是锁定）");
        _swAutoDelete = new ToggleSwitch { Margin = new Padding(10, 0, 0, 0) };
        _swAutoDelete.CheckedChanged += (_, _) => SaveSettings();
        var deleteRow = (FlowLayoutPanel)deletePanel.Controls[0];
        deleteRow.Controls.Add(_swAutoDelete);
        layout.Controls.Add(deletePanel);

        // Warning for auto-delete
        var warningLabel = new Label
        {
            Text = "⚠ 警告：启用自动删除后，超过尝试次数的加密记录将被永久删除，无法恢复！",
            ForeColor = Color.FromArgb(220, 53, 69),
            AutoSize = true,
            MaximumSize = new Size(550, 0),
            Margin = new Padding(10, 0, 0, 15)
        };
        layout.Controls.Add(warningLabel);

        // Encrypted search
        var searchPanel = CreateSettingPanel("允许搜索加密内容：",
            "关闭时，搜索只匹配\"加密内容\"关键字");
        _swSearchEncrypted = new ToggleSwitch { Margin = new Padding(10, 0, 0, 0) };
        _swSearchEncrypted.CheckedChanged += SwSearchEncrypted_Changed;
        var searchRow = (FlowLayoutPanel)searchPanel.Controls[0];
        searchRow.Controls.Add(_swSearchEncrypted);
        layout.Controls.Add(searchPanel);

        var searchWarning = new Label
        {
            Text = "⚠ 启用搜索加密内容可能需要临时解密数据，存在安全风险。请谨慎使用。",
            ForeColor = Color.FromArgb(255, 140, 0),
            AutoSize = true,
            MaximumSize = new Size(550, 0),
            Margin = new Padding(10, 0, 0, 15)
        };
        layout.Controls.Add(searchWarning);

        Controls.Add(layout);
    }

    private Panel CreateSettingPanel(string labelText, string descText)
    {
        var container = new Panel { AutoSize = true, Width = 600, Margin = new Padding(0, 0, 0, 5) };

        var row = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(0, 5, 0, 0)
        };
        row.Controls.Add(new Label
        {
            Text = labelText,
            AutoSize = true,
            ForeColor = ThemeService.TextColor,
            Font = new Font(ThemeService.GlobalFont.FontFamily, 10f, FontStyle.Bold),
            Margin = new Padding(10, 3, 0, 0)
        });
        container.Controls.Add(row);

        var desc = new Label
        {
            Text = descText,
            ForeColor = ThemeService.SecondaryTextColor,
            AutoSize = true,
            MaximumSize = new Size(550, 0),
            Margin = new Padding(10, 0, 0, 0),
            Location = new Point(10, row.Bottom + 2)
        };
        container.Controls.Add(desc);

        // Adjust container height
        container.Height = row.Height + desc.Height + 10;

        return container;
    }

    private void LoadSettings()
    {
        var config = _mainForm.Config;
        _numMaxAttempts.Value = Math.Max(_numMaxAttempts.Minimum, Math.Min(_numMaxAttempts.Maximum, config.DefaultMaxPasswordAttempts));
        _numBaseLock.Value = Math.Max(_numBaseLock.Minimum, Math.Min(_numBaseLock.Maximum, config.DefaultBaseLockMinutes));
        _swAutoDelete.Checked = config.DefaultAutoDeleteOnExceed;
        _swSearchEncrypted.Checked = config.AllowSearchEncryptedContent;
    }

    private void SaveSettings()
    {
        try
        {
            var config = _mainForm.Config;
            config.DefaultMaxPasswordAttempts = (int)_numMaxAttempts.Value;
            config.DefaultBaseLockMinutes = (int)_numBaseLock.Value;
            config.DefaultAutoDeleteOnExceed = _swAutoDelete.Checked;
            config.AllowSearchEncryptedContent = _swSearchEncrypted.Checked;
            FileService.SaveConfig(config);
        }
        catch (Exception ex)
        {
            LogService.Log("Failed to save security settings", ex);
        }
    }

    private void SwSearchEncrypted_Changed(object? sender, EventArgs e)
    {
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
