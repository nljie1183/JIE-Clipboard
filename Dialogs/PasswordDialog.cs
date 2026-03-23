using JIE剪切板.Services;

namespace JIE剪切板.Dialogs;

public class PasswordDialog : Form
{
    private TextBox _txtPassword = null!;
    private Button _btnOk = null!, _btnCancel = null!;
    private CheckBox _chkShow = null!;
    public string Password => _txtPassword.Text;

    public PasswordDialog()
    {
        InitializeForm();
        InitializeControls();
    }

    private void InitializeForm()
    {
        Text = "输入密码";
        Size = new Size(380, 180);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
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
        var lblPrompt = new Label
        {
            Text = "请输入该记录的解密密码：",
            Location = new Point(20, 15),
            AutoSize = true,
            ForeColor = ThemeService.TextColor
        };

        _txtPassword = new TextBox
        {
            Location = new Point(20, 42),
            Size = new Size(320, 25),
            UseSystemPasswordChar = true,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = ThemeService.IsDarkMode ? Color.FromArgb(50, 50, 50) : Color.White,
            ForeColor = ThemeService.TextColor,
            Font = new Font(ThemeService.GlobalFont.FontFamily, 10f)
        };

        _chkShow = new CheckBox
        {
            Text = "显示密码",
            Location = new Point(20, 74),
            AutoSize = true,
            ForeColor = ThemeService.SecondaryTextColor
        };
        _chkShow.CheckedChanged += (_, _) => _txtPassword.UseSystemPasswordChar = !_chkShow.Checked;

        _btnOk = new Button
        {
            Text = "确定",
            Size = new Size(80, 32),
            Location = new Point(170, 100),
            FlatStyle = FlatStyle.Flat,
            BackColor = ThemeService.ThemeColor,
            ForeColor = Color.White,
            DialogResult = DialogResult.OK
        };
        _btnOk.FlatAppearance.BorderSize = 0;

        _btnCancel = new Button
        {
            Text = "取消",
            Size = new Size(80, 32),
            Location = new Point(260, 100),
            FlatStyle = FlatStyle.Flat,
            ForeColor = ThemeService.TextColor,
            BackColor = ThemeService.WindowBackground,
            DialogResult = DialogResult.Cancel
        };
        _btnCancel.FlatAppearance.BorderColor = ThemeService.BorderColor;

        AcceptButton = _btnOk;
        CancelButton = _btnCancel;

        Controls.AddRange(new Control[] { lblPrompt, _txtPassword, _chkShow, _btnOk, _btnCancel });
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _txtPassword.Focus();
    }
}
