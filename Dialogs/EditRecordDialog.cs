using JIE剪切板.Controls;
using JIE剪切板.Models;
using JIE剪切板.Services;

namespace JIE剪切板.Dialogs;

public class EditRecordDialog : Form
{
    private readonly ClipboardRecord _record;
    private readonly AppConfig _config;
    private readonly string? _decryptedContent;
    private readonly ClipboardContentType? _decryptedType;
    private readonly string? _existingPassword;

    private TextBox _txtContent = null!;
    private DateTimePicker _dtpExpire = null!;
    private CheckBox _chkExpire = null!, _chkEncrypt = null!, _chkUseGlobal = null!;
    private NumericUpDown _numMaxCopy = null!, _numMaxAttempts = null!, _numBaseLock = null!;
    private ToggleSwitch _swAutoDelete = null!;
    private TextBox _txtPassword = null!, _txtPasswordConfirm = null!, _txtEncryptedHint = null!;
    private Panel _encryptPanel = null!, _securityPanel = null!;

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

    private void InitializeForm()
    {
        Text = "编辑记录";
        Size = new Size(550, 660);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        AutoScroll = true;
        BackColor = ThemeService.WindowBackground;
        ForeColor = ThemeService.TextColor;
        Font = ThemeService.GlobalFont;
    }

    private void InitializeControls()
    {
        int y = 15;

        // Content section
        Controls.Add(CreateLabel("内容：", 15, y));
        y += 22;

        _txtContent = new TextBox
        {
            Location = new Point(15, y),
            Size = new Size(500, 80),
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = ThemeService.IsDarkMode ? Color.FromArgb(50, 50, 50) : Color.White,
            ForeColor = ThemeService.TextColor
        };
        Controls.Add(_txtContent);
        y += 90;

        // Expiration
        _chkExpire = new CheckBox
        {
            Text = "设置过期时间",
            Location = new Point(15, y),
            AutoSize = true,
            ForeColor = ThemeService.TextColor
        };
        _chkExpire.CheckedChanged += (_, _) => _dtpExpire.Enabled = _chkExpire.Checked;
        Controls.Add(_chkExpire);

        _dtpExpire = new DateTimePicker
        {
            Location = new Point(150, y - 2),
            Size = new Size(200, 25),
            Format = DateTimePickerFormat.Custom,
            CustomFormat = "yyyy-MM-dd HH:mm",
            Enabled = false,
            Value = DateTime.Now.AddDays(7)
        };
        Controls.Add(_dtpExpire);
        y += 35;

        // Max copy count
        Controls.Add(CreateLabel("最大复制次数（0=不限）：", 15, y + 3));
        _numMaxCopy = new NumericUpDown
        {
            Location = new Point(200, y),
            Size = new Size(80, 25),
            Minimum = 0,
            Maximum = 100000,
            Value = 0
        };
        Controls.Add(_numMaxCopy);
        y += 35;

        // Separator
        var sep1 = new Panel { Location = new Point(15, y), Size = new Size(500, 1), BackColor = ThemeService.BorderColor };
        Controls.Add(sep1);
        y += 10;

        // Encryption section
        _chkEncrypt = new CheckBox
        {
            Text = "加密此记录",
            Location = new Point(15, y),
            AutoSize = true,
            ForeColor = ThemeService.TextColor,
            Font = new Font(ThemeService.GlobalFont.FontFamily, 10f, FontStyle.Bold)
        };
        _chkEncrypt.CheckedChanged += (_, _) =>
        {
            _encryptPanel.Visible = _chkEncrypt.Checked;
            _securityPanel.Visible = _chkEncrypt.Checked && !_chkUseGlobal.Checked;
        };
        Controls.Add(_chkEncrypt);
        y += 30;

        // Encrypt panel (password inputs)
        _encryptPanel = new Panel
        {
            Location = new Point(15, y),
            Size = new Size(500, 80),
            Visible = false
        };

        _encryptPanel.Controls.Add(CreateLabel("密码：", 0, 5));
        _txtPassword = new TextBox
        {
            Location = new Point(100, 2),
            Size = new Size(200, 25),
            UseSystemPasswordChar = true,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = ThemeService.IsDarkMode ? Color.FromArgb(50, 50, 50) : Color.White,
            ForeColor = ThemeService.TextColor
        };
        _encryptPanel.Controls.Add(_txtPassword);

        _encryptPanel.Controls.Add(CreateLabel("确认密码：", 0, 38));
        _txtPasswordConfirm = new TextBox
        {
            Location = new Point(100, 35),
            Size = new Size(200, 25),
            UseSystemPasswordChar = true,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = ThemeService.IsDarkMode ? Color.FromArgb(50, 50, 50) : Color.White,
            ForeColor = ThemeService.TextColor
        };
        _encryptPanel.Controls.Add(_txtPasswordConfirm);

        _encryptPanel.Controls.Add(CreateLabel("提示文字：", 0, 71));
        _txtEncryptedHint = new TextBox
        {
            Location = new Point(100, 68),
            Size = new Size(300, 25),
            BorderStyle = BorderStyle.FixedSingle,
            PlaceholderText = "可选，加密后显示的提示信息",
            MaxLength = 100,
            BackColor = ThemeService.IsDarkMode ? Color.FromArgb(50, 50, 50) : Color.White,
            ForeColor = ThemeService.TextColor
        };
        _encryptPanel.Controls.Add(_txtEncryptedHint);

        // Use global security checkbox
        _chkUseGlobal = new CheckBox
        {
            Text = "使用全局安全设置",
            Checked = true,
            Location = new Point(0, 98),
            AutoSize = true,
            ForeColor = ThemeService.TextColor
        };
        _chkUseGlobal.CheckedChanged += (_, _) => _securityPanel.Visible = _chkEncrypt.Checked && !_chkUseGlobal.Checked;
        _encryptPanel.Controls.Add(_chkUseGlobal);
        _encryptPanel.Size = new Size(500, 123);

        Controls.Add(_encryptPanel);
        y += 128;

        // Per-record security panel
        _securityPanel = new Panel
        {
            Location = new Point(15, y),
            Size = new Size(500, 120),
            Visible = false
        };

        _securityPanel.Controls.Add(CreateLabel("最大尝试次数：", 0, 5));
        _numMaxAttempts = new NumericUpDown
        {
            Location = new Point(130, 2),
            Size = new Size(70, 25),
            Minimum = 1,
            Maximum = 100,
            Value = 3
        };
        _securityPanel.Controls.Add(_numMaxAttempts);

        _securityPanel.Controls.Add(CreateLabel("基础锁定(分钟)：", 0, 38));
        _numBaseLock = new NumericUpDown
        {
            Location = new Point(130, 35),
            Size = new Size(70, 25),
            Minimum = 1,
            Maximum = 10080,
            Value = 60
        };
        _securityPanel.Controls.Add(_numBaseLock);

        _securityPanel.Controls.Add(CreateLabel("超限自动删除：", 0, 72));
        _swAutoDelete = new ToggleSwitch { Location = new Point(130, 70) };
        _securityPanel.Controls.Add(_swAutoDelete);

        Controls.Add(_securityPanel);
        y += 130;

        // Buttons
        var btnSave = new Button
        {
            Text = "保存",
            Size = new Size(90, 35),
            Location = new Point(320, y),
            FlatStyle = FlatStyle.Flat,
            BackColor = ThemeService.ThemeColor,
            ForeColor = Color.White
        };
        btnSave.FlatAppearance.BorderSize = 0;
        btnSave.Click += BtnSave_Click;

        var btnCancel = new Button
        {
            Text = "取消",
            Size = new Size(90, 35),
            Location = new Point(420, y),
            FlatStyle = FlatStyle.Flat,
            ForeColor = ThemeService.TextColor,
            BackColor = ThemeService.WindowBackground,
            DialogResult = DialogResult.Cancel
        };
        btnCancel.FlatAppearance.BorderColor = ThemeService.BorderColor;

        Controls.AddRange(new Control[] { btnSave, btnCancel });
        CancelButton = btnCancel;
    }

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

    private void LoadRecord()
    {
        // Content - for encrypted records with decrypted content, show the real content
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

        // Expiration
        if (_record.ExpireTime.HasValue)
        {
            _chkExpire.Checked = true;
            _dtpExpire.Value = _record.ExpireTime.Value.ToLocalTime();
        }

        // Copy count
        _numMaxCopy.Value = Math.Max(0, Math.Min(_numMaxCopy.Maximum, _record.MaxCopyCount));

        // Encryption
        _chkEncrypt.Checked = _record.IsEncrypted;
        _txtEncryptedHint.Text = _record.EncryptedHint ?? "";
        if (_record.IsEncrypted)
        {
            _txtPassword.Enabled = false;
            _txtPasswordConfirm.Enabled = false;
            _txtPassword.Text = "••••••••";
            _txtPasswordConfirm.Text = "••••••••";
        }

        // Security settings
        _chkUseGlobal.Checked = _record.UseGlobalSecuritySettings;
        _numMaxAttempts.Value = Math.Max(1, Math.Min(100, _record.MaxPasswordAttempts));
        _numBaseLock.Value = Math.Max(1, Math.Min(10080, _record.BaseLockMinutes));
        _swAutoDelete.Checked = _record.AutoDeleteOnExceed;
    }

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        try
        {
            // Determine the effective content type for decrypted records
            var effectiveType = _decryptedType ?? _record.ContentType;

            // Update content for text types
            if (effectiveType is ClipboardContentType.PlainText or ClipboardContentType.RichText)
            {
                if (!_record.IsEncrypted)
                {
                    _record.Content = _txtContent.Text;
                    _record.ContentHash = EncryptionService.ComputeContentHash(_record.Content);
                }
                else if (_decryptedContent != null && _txtContent.Text != _decryptedContent)
                {
                    // Content was edited while encrypted — need to re-encrypt with updated content
                    var password = _existingPassword;
                    if (string.IsNullOrEmpty(password))
                    {
                        MessageBox.Show(this, "无法保存修改：缺少加密密码", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                    // Temporarily restore unencrypted state to re-encrypt
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

            // Update expiration
            _record.ExpireTime = _chkExpire.Checked ? _dtpExpire.Value.ToUniversalTime() : null;

            // Update copy count
            _record.MaxCopyCount = (int)_numMaxCopy.Value;

            // Update encrypted hint
            _record.EncryptedHint = string.IsNullOrWhiteSpace(_txtEncryptedHint.Text) ? null : _txtEncryptedHint.Text.Trim();

            // Handle encryption change
            if (_chkEncrypt.Checked && !_record.IsEncrypted)
            {
                // New encryption
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
                // Request password to decrypt
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

            // Update security settings
            _record.UseGlobalSecuritySettings = _chkUseGlobal.Checked;
            if (!_chkUseGlobal.Checked)
            {
                _record.MaxPasswordAttempts = (int)_numMaxAttempts.Value;
                _record.BaseLockMinutes = (int)_numBaseLock.Value;
                _record.AutoDeleteOnExceed = _swAutoDelete.Checked;
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
