using JIE剪切板.Controls;
using JIE剪切板.Models;
using JIE剪切板.Services;

namespace JIE剪切板.Dialogs;

/// <summary>
/// 记录编辑对话框。
/// 允许用户编辑剪贴板记录的：
/// - 内容（文本类型可编辑，其他类型只读）
/// - 过期时间
/// - 最大复制次数
/// - 加密/解密（含密码设置、提示文字）
/// - 安全策略（最大尝试次数、锁定时长、自动删除）
/// 
/// 对于加密记录，支持解密后显示真实内容并编辑，保存时自动重新加密。
/// </summary>
public class EditRecordDialog : Form
{
    private readonly ClipboardRecord _record;           // 正在编辑的记录引用
    private readonly AppConfig _config;                 // 应用配置
    private readonly string? _decryptedContent;         // 解密后的原始内容（加密记录时传入）
    private readonly ClipboardContentType? _decryptedType; // 解密后的原始类型
    private readonly string? _existingPassword;         // 已验证的密码（用于重新加密）

    // UI 控件
    private TextBox _txtContent = null!;                // 内容编辑框
    private DateTimePicker _dtpExpire = null!;          // 过期时间选择器
    private CheckBox _chkExpire = null!, _chkEncrypt = null!, _chkUseGlobal = null!;
    private NumericUpDown _numMaxCopy = null!, _numMaxAttempts = null!, _numBaseLock = null!;
    private ToggleSwitch _swAutoDelete = null!;         // 超限自动删除开关
    private TextBox _txtPassword = null!, _txtPasswordConfirm = null!, _txtEncryptedHint = null!;
    private Panel _encryptPanel = null!, _securityPanel = null!; // 加密和安全设置面板
    private ToggleSwitch _swAllowSearchEncryptedContent = null!;
    private ToggleSwitch _swAllowSearchEncryptedHint = null!;

    /// <summary>
    /// 构造函数。
    /// </summary>
    /// <param name="record">要编辑的记录</param>
    /// <param name="config">应用配置</param>
    /// <param name="decryptedContent">解密后的内容（可选）</param>
    /// <param name="decryptedType">解密后的类型（可选）</param>
    /// <param name="existingPassword">已验证的密码（可选，用于重新加密）</param>
    public EditRecordDialog(ClipboardRecord record, AppConfig config,
        string? decryptedContent = null, ClipboardContentType? decryptedType = null, string? existingPassword = null)
    {
        _record = record;
        _config = config;
        _decryptedContent = decryptedContent;
        _decryptedType = decryptedType;
        _existingPassword = existingPassword;
        InitializeForm();
        InitializeControls();
        LoadRecord();
    }

    /// <summary>初始化窗口属性（大小、标题、样式等）</summary>
    private void InitializeForm()
    {
        Text = "编辑记录";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = ThemeService.WindowBackground;
        ForeColor = ThemeService.TextColor;
        Font = ThemeService.GlobalFont;

        var w = DpiHelper.Scale(550);
        var h = DpiHelper.Scale(660);
        Size = new Size(w, h);
        MinimumSize = new Size(DpiHelper.Scale(480), DpiHelper.Scale(500));
    }

    /// <summary>
    /// 初始化所有 UI 控件：内容编辑、过期时间、复制次数、加密设置、安全策略、保存/取消按钮。
    /// 布局：底部按钮栏（Dock.Bottom）+ 可滚动内容区域（Dock.Fill）。
    /// </summary>
    private void InitializeControls()
    {
        int pad = DpiHelper.Scale(15);

        // ───── 底部按钮栏（固定在底部） ─────
        var buttonBar = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = DpiHelper.Scale(55)
        };

        var btnCancel = new Button
        {
            Text = "取消",
            Size = new Size(DpiHelper.Scale(90), DpiHelper.Scale(35)),
            FlatStyle = FlatStyle.Flat,
            ForeColor = ThemeService.TextColor,
            BackColor = ThemeService.WindowBackground,
            DialogResult = DialogResult.Cancel,
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        btnCancel.FlatAppearance.BorderColor = ThemeService.BorderColor;
        btnCancel.Location = new Point(ClientSize.Width - pad - btnCancel.Width, DpiHelper.Scale(10));

        var btnSave = new Button
        {
            Text = "保存",
            Size = new Size(DpiHelper.Scale(90), DpiHelper.Scale(35)),
            FlatStyle = FlatStyle.Flat,
            BackColor = ThemeService.ThemeColor,
            ForeColor = Color.White,
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        btnSave.FlatAppearance.BorderSize = 0;
        btnSave.Click += BtnSave_Click;
        btnSave.Location = new Point(btnCancel.Left - DpiHelper.Scale(8) - btnSave.Width, DpiHelper.Scale(10));

        buttonBar.Controls.AddRange(new Control[] { btnSave, btnCancel });
        CancelButton = btnCancel;

        // ───── 可滚动内容区域 ─────
        var scroll = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true
        };

        // 先将面板加入 Form 让 Dock 布局生效，再添加子控件，
        // 否则 Anchor = Left|Right 基于 scroll 默认宽度计算右边距会出错
        Controls.Add(scroll);
        Controls.Add(buttonBar);

        // 现在 scroll 已有正确尺寸，基于它计算子控件宽度
        int ctrlW = scroll.ClientSize.Width - pad * 2;

        int y = pad;

        // 内容编辑区
        scroll.Controls.Add(CreateLabel("内容：", pad, y));
        y += DpiHelper.Scale(22);

        _txtContent = new TextBox
        {
            Location = new Point(pad, y),
            Size = new Size(ctrlW, DpiHelper.Scale(80)),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = ThemeService.IsDarkMode ? Color.FromArgb(50, 50, 50) : Color.White,
            ForeColor = ThemeService.TextColor
        };
        scroll.Controls.Add(_txtContent);
        y += DpiHelper.Scale(90);

        // 过期时间设置
        _chkExpire = new CheckBox
        {
            Text = "设置过期时间",
            Location = new Point(pad, y),
            AutoSize = true,
            ForeColor = ThemeService.TextColor
        };
        _chkExpire.CheckedChanged += (_, _) => _dtpExpire.Enabled = _chkExpire.Checked;
        scroll.Controls.Add(_chkExpire);

        _dtpExpire = new DateTimePicker
        {
            Location = new Point(DpiHelper.Scale(150), y - 2),
            Size = new Size(DpiHelper.Scale(200), DpiHelper.Scale(25)),
            Format = DateTimePickerFormat.Custom,
            CustomFormat = "yyyy-MM-dd HH:mm",
            Enabled = false,
            Value = DateTime.Now.AddDays(7)
        };
        scroll.Controls.Add(_dtpExpire);
        y += DpiHelper.Scale(35);

        // 最大复制次数
        scroll.Controls.Add(CreateLabel("最大复制次数（0=不限）：", pad, y + 3));
        _numMaxCopy = new NumericUpDown
        {
            Location = new Point(DpiHelper.Scale(200), y),
            Size = new Size(DpiHelper.Scale(80), DpiHelper.Scale(25)),
            Minimum = 0,
            Maximum = 100000,
            Value = 0
        };
        scroll.Controls.Add(_numMaxCopy);
        y += DpiHelper.Scale(35);

        // 分隔线
        scroll.Controls.Add(new Panel
        {
            Location = new Point(pad, y),
            Size = new Size(ctrlW, 1),
            BackColor = ThemeService.BorderColor,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        });
        y += DpiHelper.Scale(10);

        // 加密设置区
        _chkEncrypt = new CheckBox
        {
            Text = "加密此记录",
            Location = new Point(pad, y),
            AutoSize = true,
            ForeColor = ThemeService.TextColor,
            Font = new Font(ThemeService.GlobalFont.FontFamily, 10f, FontStyle.Bold)
        };
        _chkEncrypt.CheckedChanged += (_, _) =>
        {
            _encryptPanel.Visible = _chkEncrypt.Checked;
            _securityPanel.Visible = _chkEncrypt.Checked && !_chkUseGlobal.Checked;
        };
        scroll.Controls.Add(_chkEncrypt);
        y += DpiHelper.Scale(30);

        // 加密面板（密码输入、确认密码、提示文字）
        _encryptPanel = new Panel
        {
            Location = new Point(pad, y),
            Size = new Size(ctrlW, DpiHelper.Scale(123)),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Visible = false
        };

        _encryptPanel.Controls.Add(CreateLabel("密码：", 0, DpiHelper.Scale(5)));
        _txtPassword = new TextBox
        {
            Location = new Point(DpiHelper.Scale(100), DpiHelper.Scale(2)),
            Size = new Size(DpiHelper.Scale(200), DpiHelper.Scale(25)),
            UseSystemPasswordChar = true,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = ThemeService.IsDarkMode ? Color.FromArgb(50, 50, 50) : Color.White,
            ForeColor = ThemeService.TextColor
        };
        _encryptPanel.Controls.Add(_txtPassword);

        _encryptPanel.Controls.Add(CreateLabel("确认密码：", 0, DpiHelper.Scale(38)));
        _txtPasswordConfirm = new TextBox
        {
            Location = new Point(DpiHelper.Scale(100), DpiHelper.Scale(35)),
            Size = new Size(DpiHelper.Scale(200), DpiHelper.Scale(25)),
            UseSystemPasswordChar = true,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = ThemeService.IsDarkMode ? Color.FromArgb(50, 50, 50) : Color.White,
            ForeColor = ThemeService.TextColor
        };
        _encryptPanel.Controls.Add(_txtPasswordConfirm);

        _encryptPanel.Controls.Add(CreateLabel("提示文字：", 0, DpiHelper.Scale(71)));
        _txtEncryptedHint = new TextBox
        {
            Location = new Point(DpiHelper.Scale(100), DpiHelper.Scale(68)),
            Size = new Size(DpiHelper.Scale(300), DpiHelper.Scale(25)),
            BorderStyle = BorderStyle.FixedSingle,
            PlaceholderText = "可选，加密后显示的提示信息",
            MaxLength = 100,
            BackColor = ThemeService.IsDarkMode ? Color.FromArgb(50, 50, 50) : Color.White,
            ForeColor = ThemeService.TextColor
        };
        _encryptPanel.Controls.Add(_txtEncryptedHint);

        // 是否使用全局安全设置复选框
        _chkUseGlobal = new CheckBox
        {
            Text = "使用全局安全设置",
            Checked = true,
            Location = new Point(0, DpiHelper.Scale(98)),
            AutoSize = true,
            ForeColor = ThemeService.TextColor
        };
        _chkUseGlobal.CheckedChanged += (_, _) => _securityPanel.Visible = _chkEncrypt.Checked && !_chkUseGlobal.Checked;
        _encryptPanel.Controls.Add(_chkUseGlobal);

        scroll.Controls.Add(_encryptPanel);
        y += DpiHelper.Scale(128);

        // 单条记录独立安全策略面板（使用 FlowLayout 避免不同长度标签与控件重叠）
        _securityPanel = new FlowLayoutPanel
        {
            Location = new Point(pad, y),
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Visible = false
        };

        _numMaxAttempts = new NumericUpDown { Minimum = 1, Maximum = 100, Value = 3, Width = DpiHelper.Scale(70) };
        _securityPanel.Controls.Add(CreateSettingRow("最大尝试次数：", _numMaxAttempts));

        _numBaseLock = new NumericUpDown { Minimum = 1, Maximum = 10080, Value = 60, Width = DpiHelper.Scale(70) };
        _securityPanel.Controls.Add(CreateSettingRow("基础锁定(分钟)：", _numBaseLock));

        _swAutoDelete = new ToggleSwitch();
        _securityPanel.Controls.Add(CreateSettingRow("超限自动删除：", _swAutoDelete));

        _swAllowSearchEncryptedContent = new ToggleSwitch();
        _securityPanel.Controls.Add(CreateSettingRow("允许搜索加密内容：", _swAllowSearchEncryptedContent));

        _swAllowSearchEncryptedHint = new ToggleSwitch();
        _securityPanel.Controls.Add(CreateSettingRow("允许搜索加密提示：", _swAllowSearchEncryptedHint));

        scroll.Controls.Add(_securityPanel);
    }

    /// <summary>窗口显示后继承主窗口图标</summary>
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        var icon = Owner?.Icon ?? Application.OpenForms.Cast<Form>().FirstOrDefault(f => f.Icon != null)?.Icon;
        if (icon != null) Icon = icon;
    }

    /// <summary>创建安全设置行（标签 + 控件），FlowLayout 自动排列避免重叠</summary>
    private FlowLayoutPanel CreateSettingRow(string labelText, Control ctrl)
    {
        var row = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = new Padding(0, DpiHelper.Scale(4), 0, DpiHelper.Scale(4))
        };
        row.Controls.Add(new Label
        {
            Text = labelText,
            AutoSize = true,
            ForeColor = ThemeService.TextColor,
            Margin = new Padding(0, DpiHelper.Scale(3), DpiHelper.Scale(8), 0)
        });
        row.Controls.Add(ctrl);
        return row;
    }

    /// <summary>创建统一样式的标签控件</summary>
    private Label CreateLabel(string text, int x, int y)
    {
        return new Label
        {
            Text = text,
            Location = new Point(x, y),
            AutoSize = true,
            ForeColor = ThemeService.TextColor
        };
    }

    /// <summary>
    /// 加载记录数据到 UI 控件。
    /// 对于加密记录，如果有解密内容则显示解密后的真实内容。
    /// </summary>
    private void LoadRecord()
    {
        // 加密记录且有解密内容时，显示解密后的真实内容
        if (_decryptedContent != null)
        {
            var effectiveType = _decryptedType ?? _record.ContentType;
            if (effectiveType is ClipboardContentType.PlainText or ClipboardContentType.RichText)
            {
                _txtContent.Text = _decryptedContent;
                _txtContent.ReadOnly = false;
            }
            else
            {
                _txtContent.Text = _decryptedContent;
                _txtContent.ReadOnly = true;
            }
        }
        else if (_record.ContentType is ClipboardContentType.PlainText or ClipboardContentType.RichText)
        {
            _txtContent.Text = _record.Content;
            _txtContent.ReadOnly = false;
        }
        else
        {
            _txtContent.Text = ClipboardService.GetContentPreview(_record, 500);
            _txtContent.ReadOnly = true;
        }

        // 过期时间
        if (_record.ExpireTime.HasValue)
        {
            _chkExpire.Checked = true;
            _dtpExpire.Value = _record.ExpireTime.Value.ToLocalTime();
        }

        // 复制次数
        _numMaxCopy.Value = Math.Max(0, Math.Min(_numMaxCopy.Maximum, _record.MaxCopyCount));

        // 加密状态
        _chkEncrypt.Checked = _record.IsEncrypted;
        _txtEncryptedHint.Text = _record.EncryptedHint ?? "";
        if (_record.IsEncrypted)
        {
            _txtPassword.Enabled = false;
            _txtPasswordConfirm.Enabled = false;
            _txtPassword.Text = "••••••••";
            _txtPasswordConfirm.Text = "••••••••";
        }

        // 安全策略设置
        _chkUseGlobal.Checked = _record.UseGlobalSecuritySettings;
        _numMaxAttempts.Value = Math.Max(1, Math.Min(100, _record.MaxPasswordAttempts));
        _numBaseLock.Value = Math.Max(1, Math.Min(10080, _record.BaseLockMinutes));
        _swAutoDelete.Checked = _record.AutoDeleteOnExceed;
        _swAllowSearchEncryptedContent.Checked = _record.AllowSearchEncryptedContent;
        _swAllowSearchEncryptedHint.Checked = _record.AllowSearchEncryptedHint;
    }

    /// <summary>
    /// 保存按钮点击处理。
    /// 处理复杂逻辑：内容更新、加密/解密切换、重新加密、安全策略更新。
    /// </summary>
    private void BtnSave_Click(object? sender, EventArgs e)
    {
        try
        {
            // 确定解密记录的实际内容类型
            var effectiveType = _decryptedType ?? _record.ContentType;

            // 更新文本类型的内容
            if (effectiveType is ClipboardContentType.PlainText or ClipboardContentType.RichText)
            {
                if (!_record.IsEncrypted)
                {
                    _record.Content = _txtContent.Text;
                    _record.ContentHash = EncryptionService.ComputeContentHash(_record.Content);
                }
                else if (_decryptedContent != null && _txtContent.Text != _decryptedContent)
                {
                    // 加密状态下编辑了内容 —— 需要用新内容重新加密
                    var password = _existingPassword;
                    if (string.IsNullOrEmpty(password))
                    {
                        MessageBox.Show(this, "无法保存修改：缺少加密密码", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                    // 临时恢复未加密状态以便重新加密
                    _record.IsEncrypted = false;
                    _record.Content = _txtContent.Text;
                    _record.ContentType = effectiveType;
                    if (!EncryptionService.EncryptRecord(_record, password))
                    {
                        MessageBox.Show(this, "重新加密失败", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                }
            }

            // 更新过期时间
            _record.ExpireTime = _chkExpire.Checked ? _dtpExpire.Value.ToUniversalTime() : null;

            // 更新最大复制次数
            _record.MaxCopyCount = (int)_numMaxCopy.Value;

            // 更新加密提示文字
            _record.EncryptedHint = string.IsNullOrWhiteSpace(_txtEncryptedHint.Text)
                ? null : _txtEncryptedHint.Text.Trim();

            // 处理加密状态变更
            if (_chkEncrypt.Checked && !_record.IsEncrypted)
            {
                // 新加密：验证密码并加密记录
                if (string.IsNullOrEmpty(_txtPassword.Text))
                {
                    MessageBox.Show(this, "请输入加密密码", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (_txtPassword.Text != _txtPasswordConfirm.Text)
                {
                    MessageBox.Show(this, "两次输入的密码不一致", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (_txtPassword.Text.Length < 4)
                {
                    MessageBox.Show(this, "密码长度至少4个字符", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (!EncryptionService.EncryptRecord(_record, _txtPassword.Text))
                {
                    MessageBox.Show(this, "加密失败", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }
            else if (!_chkEncrypt.Checked && _record.IsEncrypted)
            {
                // 请求密码以解密记录
                using var pwDialog = new PasswordDialog();
                if (pwDialog.ShowDialog(this) != DialogResult.OK) return;

                var result = EncryptionService.DecryptRecord(_record, pwDialog.Password);
                if (!result.HasValue)
                {
                    MessageBox.Show(this, "密码错误，无法解密", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                _record.IsEncrypted = false;
                _record.Content = result.Value.content;
                _record.ContentType = result.Value.type;
                _record.EncryptedData = null;
                _record.Salt = null;
                _record.IV = null;
                _record.PasswordHash = null;
                _record.PasswordSalt = null;
                _record.PasswordFailCount = 0;
                _record.LockUntil = null;
                _record.CumulativeLockCount = 0;
                _record.ContentHash = EncryptionService.ComputeContentHash(_record.Content);
            }

            // 更新安全策略设置
            _record.UseGlobalSecuritySettings = _chkUseGlobal.Checked;
            if (!_chkUseGlobal.Checked)
            {
                _record.MaxPasswordAttempts = (int)_numMaxAttempts.Value;
                _record.BaseLockMinutes = (int)_numBaseLock.Value;
                _record.AutoDeleteOnExceed = _swAutoDelete.Checked;
                _record.AllowSearchEncryptedContent = _swAllowSearchEncryptedContent.Checked;
                _record.AllowSearchEncryptedHint = _swAllowSearchEncryptedHint.Checked;
            }

            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            LogService.Log("Failed to save record edits", ex);
            MessageBox.Show(this, $"保存失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
