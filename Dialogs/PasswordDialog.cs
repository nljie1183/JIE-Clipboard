using JIE剪切板.Services;

namespace JIE剪切板.Dialogs;

/// <summary>
/// 密码输入对话框。
/// 用于解密加密记录或导入加密备份文件时提示用户输入密码。
/// 支持显示/隐藏密码切换，回车确认、ESC 取消。
/// </summary>
public class PasswordDialog : Form
{
    private TextBox _txtPassword = null!;
    private Button _btnOk = null!, _btnCancel = null!;
    private CheckBox _chkShow = null!;

    /// <summary>用户输入的密码（确认后读取）</summary>
    public string Password => _txtPassword.Text;

    public PasswordDialog()
    {
        InitializeForm();
        InitializeControls();
    }

    private void InitializeForm()
    {
        Text = "输入密码";
        var w = DpiHelper.Scale(400);
        var h = DpiHelper.Scale(200);
        ClientSize = new Size(w, h);
        MinimumSize = new Size(DpiHelper.Scale(340), DpiHelper.Scale(180));
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = ThemeService.WindowBackground;
        ForeColor = ThemeService.TextColor;
        Font = ThemeService.GlobalFont;
    }

    private void InitializeControls()
    {
        int pad = DpiHelper.Scale(20);
        int btnW = DpiHelper.Scale(80);
        int btnH = DpiHelper.Scale(32);
        int gap = DpiHelper.Scale(8);

        var lblPrompt = new Label
        {
            Text = "请输入该记录的解密密码：",
            Location = new Point(pad, DpiHelper.Scale(15)),
            AutoSize = true,
            ForeColor = ThemeService.TextColor
        };

        _txtPassword = new TextBox
        {
            Location = new Point(pad, DpiHelper.Scale(42)),
            Size = new Size(ClientSize.Width - pad * 2, DpiHelper.Scale(25)),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            UseSystemPasswordChar = true,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = ThemeService.IsDarkMode ? Color.FromArgb(50, 50, 50) : Color.White,
            ForeColor = ThemeService.TextColor,
            Font = new Font(ThemeService.GlobalFont.FontFamily, 10f)
        };

        _chkShow = new CheckBox
        {
            Text = "显示密码",
            Location = new Point(pad, DpiHelper.Scale(74)),
            AutoSize = true,
            ForeColor = ThemeService.SecondaryTextColor
        };
        _chkShow.CheckedChanged += (_, _) => _txtPassword.UseSystemPasswordChar = !_chkShow.Checked;

        _btnCancel = new Button
        {
            Text = "取消",
            Size = new Size(btnW, btnH),
            Location = new Point(ClientSize.Width - pad - btnW, ClientSize.Height - pad - btnH),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            FlatStyle = FlatStyle.Flat,
            ForeColor = ThemeService.TextColor,
            BackColor = ThemeService.WindowBackground,
            DialogResult = DialogResult.Cancel
        };
        _btnCancel.FlatAppearance.BorderColor = ThemeService.BorderColor;

        _btnOk = new Button
        {
            Text = "确定",
            Size = new Size(btnW, btnH),
            Location = new Point(_btnCancel.Left - gap - btnW, _btnCancel.Top),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            FlatStyle = FlatStyle.Flat,
            BackColor = ThemeService.ThemeColor,
            ForeColor = Color.White,
            DialogResult = DialogResult.OK
        };
        _btnOk.FlatAppearance.BorderSize = 0;

        AcceptButton = _btnOk;
        CancelButton = _btnCancel;

        Controls.AddRange(new Control[] { lblPrompt, _txtPassword, _chkShow, _btnOk, _btnCancel });
    }

    /// <summary>窗口显示后自动聚焦密码输入框</summary>
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _txtPassword.Focus();
    }
}
