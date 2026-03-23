using JIE剪切板.Services;

namespace JIE剪切板.Pages;

public class AppearancePage : UserControl
{
    private readonly MainForm _mainForm;
    private RadioButton _rbFollowSystem = null!, _rbLight = null!, _rbDark = null!;
    private Panel _colorPreview = null!;
    private Button _btnChangeColor = null!;
    private ComboBox _cboFont = null!;
    private bool _isLoading;

    public AppearancePage(MainForm mainForm)
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
            WrapContents = false,
            Padding = new Padding(0)
        };

        // Title
        layout.Controls.Add(CreateTitle("外观设置"));
        layout.Controls.Add(CreatePadding(10));

        // Theme mode section
        layout.Controls.Add(CreateSectionLabel("主题模式"));
        var modePanel = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = false, Margin = new Padding(10, 5, 0, 10) };
        _rbFollowSystem = new RadioButton { Text = "跟随系统", AutoSize = true, ForeColor = ThemeService.TextColor, Margin = new Padding(0, 0, 20, 0) };
        _rbLight = new RadioButton { Text = "浅色", AutoSize = true, ForeColor = ThemeService.TextColor, Margin = new Padding(0, 0, 20, 0) };
        _rbDark = new RadioButton { Text = "深色", AutoSize = true, ForeColor = ThemeService.TextColor };
        _rbFollowSystem.CheckedChanged += ThemeMode_Changed;
        _rbLight.CheckedChanged += ThemeMode_Changed;
        _rbDark.CheckedChanged += ThemeMode_Changed;
        modePanel.Controls.AddRange(new Control[] { _rbFollowSystem, _rbLight, _rbDark });
        layout.Controls.Add(modePanel);

        // Theme color section
        layout.Controls.Add(CreateSectionLabel("主题颜色"));
        var colorPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = false, Margin = new Padding(10, 5, 0, 10) };

        _colorPreview = new Panel
        {
            Size = new Size(40, 25),
            BackColor = ThemeService.ThemeColor,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 0, 10, 0)
        };

        _btnChangeColor = new Button
        {
            Text = "选择颜色",
            FlatStyle = FlatStyle.Flat,
            Size = new Size(90, 28),
            ForeColor = ThemeService.ThemeColor,
            BackColor = ThemeService.WindowBackground
        };
        _btnChangeColor.FlatAppearance.BorderColor = ThemeService.ThemeColor;
        _btnChangeColor.Click += BtnChangeColor_Click;

        // Preset colors
        var presetColors = new[] {
            Color.FromArgb(0, 120, 215),   // Blue (default)
            Color.FromArgb(16, 137, 62),   // Green
            Color.FromArgb(232, 17, 35),   // Red
            Color.FromArgb(136, 23, 152),  // Purple
            Color.FromArgb(255, 140, 0),   // Orange
            Color.FromArgb(0, 153, 188),   // Teal
            Color.FromArgb(76, 74, 72)     // Gray
        };

        foreach (var pc in presetColors)
        {
            var swatch = new Panel
            {
                Size = new Size(25, 25),
                BackColor = pc,
                Cursor = Cursors.Hand,
                Margin = new Padding(2, 0, 2, 0)
            };
            var capturedColor = pc;
            swatch.Click += (_, _) => SetThemeColor(capturedColor);
            swatch.Paint += (s, pe) =>
            {
                if (((Panel)s!).BackColor == ThemeService.ThemeColor)
                {
                    using var pen = new Pen(Color.White, 2);
                    pe.Graphics.DrawRectangle(pen, 2, 2, 20, 20);
                }
            };
            colorPanel.Controls.Add(swatch);
        }

        colorPanel.Controls.Add(_colorPreview);
        colorPanel.Controls.Add(_btnChangeColor);
        layout.Controls.Add(colorPanel);

        // Font section
        layout.Controls.Add(CreateSectionLabel("字体"));
        var fontPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = false, Margin = new Padding(10, 5, 0, 10) };
        _cboFont = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 250,
            ForeColor = ThemeService.TextColor,
            BackColor = ThemeService.IsDarkMode ? Color.FromArgb(50, 50, 50) : Color.White
        };
        _cboFont.Items.Add("（系统默认）");
        foreach (var family in System.Drawing.FontFamily.Families)
        {
            _cboFont.Items.Add(family.Name);
        }
        _cboFont.SelectedIndexChanged += CboFont_SelectedIndexChanged;
        fontPanel.Controls.Add(_cboFont);
        layout.Controls.Add(fontPanel);

        Controls.Add(layout);
    }

    private Label CreateTitle(string text)
    {
        return new Label
        {
            Text = text,
            Font = new Font(ThemeService.GlobalFont.FontFamily, 14f, FontStyle.Bold),
            ForeColor = ThemeService.TextColor,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 5)
        };
    }

    private Label CreateSectionLabel(string text)
    {
        return new Label
        {
            Text = text,
            Font = new Font(ThemeService.GlobalFont.FontFamily, 11f, FontStyle.Bold),
            ForeColor = ThemeService.TextColor,
            AutoSize = true,
            Margin = new Padding(0, 10, 0, 0)
        };
    }

    private Panel CreatePadding(int height)
    {
        return new Panel { Height = height, Width = 1 };
    }

    private void LoadSettings()
    {
        _isLoading = true;
        try
        {
            var config = _mainForm.Config;
            switch (config.ThemeMode)
            {
                case "Dark": _rbDark.Checked = true; break;
                case "Light": _rbLight.Checked = true; break;
                default: _rbFollowSystem.Checked = true; break;
            }

            _colorPreview.BackColor = ThemeService.ThemeColor;

            if (string.IsNullOrEmpty(config.ThemeFont))
                _cboFont.SelectedIndex = 0;
            else
            {
                int idx = _cboFont.Items.IndexOf(config.ThemeFont);
                _cboFont.SelectedIndex = idx >= 0 ? idx : 0;
            }
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void ThemeMode_Changed(object? sender, EventArgs e)
    {
        if (_isLoading) return;
        if (sender is RadioButton rb && rb.Checked)
        {
            string mode = rb == _rbDark ? "Dark" : rb == _rbLight ? "Light" : "FollowSystem";
            _mainForm.Config.ThemeMode = mode;
            ThemeService.SetThemeMode(mode);
            FileService.SaveConfig(_mainForm.Config);
            _mainForm.ApplyTheme();
        }
    }

    private void BtnChangeColor_Click(object? sender, EventArgs e)
    {
        using var dialog = new ColorDialog
        {
            Color = ThemeService.ThemeColor,
            FullOpen = true
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            SetThemeColor(dialog.Color);
        }
    }

    private void SetThemeColor(Color color)
    {
        if (_isLoading) return;
        _colorPreview.BackColor = color;
        _mainForm.Config.ThemeColor = ColorTranslator.ToHtml(color);
        ThemeService.SetThemeColor(color);
        FileService.SaveConfig(_mainForm.Config);
        _mainForm.ApplyTheme();
    }

    private void CboFont_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_isLoading) return;
        var fontName = _cboFont.SelectedIndex == 0 ? "" : _cboFont.SelectedItem?.ToString() ?? "";
        _mainForm.Config.ThemeFont = fontName;
        ThemeService.SetFont(fontName);
        FileService.SaveConfig(_mainForm.Config);
        _mainForm.ApplyTheme();
    }
}
