using JIE剪切板.Services;
using System.Drawing.Drawing2D;

namespace JIE剪切板.Controls;

public class NavigationListBox : Control
{
    public class NavItem
    {
        public string Text { get; set; } = "";
        public string Icon { get; set; } = ""; // Unicode emoji
    }

    private readonly List<NavItem> _items = new();
    private int _selectedIndex = 0;
    private int _hoverIndex = -1;
    private const int ItemHeight = 40;
    private const int IconSize = 20;

    public event EventHandler<int>? SelectedIndexChanged;
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (_selectedIndex != value && value >= 0 && value < _items.Count)
            {
                _selectedIndex = value;
                Invalidate();
                SelectedIndexChanged?.Invoke(this, _selectedIndex);
            }
        }
    }

    public NavigationListBox()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Color.FromArgb(249, 249, 249);

        _items.AddRange(new NavItem[]
        {
            new() { Text = "全部记录", Icon = "📋" },
            new() { Text = "通用设置", Icon = "⚙" },
            new() { Text = "快捷键", Icon = "⌨" },
            new() { Text = "外观", Icon = "🎨" },
            new() { Text = "安全防护", Icon = "🔒" },
            new() { Text = "导出导入", Icon = "📁" },
            new() { Text = "关于", Icon = "ℹ" }
        });
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        try
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.Clear(ThemeService.SidebarBackground);

            var font = ThemeService.GlobalFont;

            for (int i = 0; i < _items.Count; i++)
            {
                var rect = new Rectangle(0, i * ItemHeight, Width, ItemHeight);
                var item = _items[i];

                if (i == _selectedIndex)
                {
                    using var brush = new SolidBrush(ThemeService.ThemeColor);
                    g.FillRectangle(brush, rect);
                    DrawItem(g, item, rect, Color.White, font);
                }
                else if (i == _hoverIndex)
                {
                    using var brush = new SolidBrush(ThemeService.HoverColor);
                    g.FillRectangle(brush, rect);
                    DrawItem(g, item, rect, ThemeService.TextColor, font);
                }
                else
                {
                    DrawItem(g, item, rect, ThemeService.TextColor, font);
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Log("Navigation paint failed", ex);
        }
    }

    private void DrawItem(Graphics g, NavItem item, Rectangle rect, Color textColor, Font font)
    {
        // Icon
        var iconRect = new Rectangle(15, rect.Y + (ItemHeight - IconSize) / 2, IconSize, IconSize);
        using (var iconFont = new Font("Segoe UI Emoji", 11f))
        using (var iconBrush = new SolidBrush(textColor))
        {
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(item.Icon, iconFont, iconBrush, iconRect, sf);
        }

        // Text
        var textRect = new Rectangle(45, rect.Y, rect.Width - 50, ItemHeight);
        using var textBrush = new SolidBrush(textColor);
        var textFormat = new StringFormat
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter
        };
        g.DrawString(item.Text, font, textBrush, textRect, textFormat);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int index = e.Y / ItemHeight;
        if (index != _hoverIndex && index >= 0 && index < _items.Count)
        {
            _hoverIndex = index;
            Invalidate();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hoverIndex = -1;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        int index = e.Y / ItemHeight;
        if (index >= 0 && index < _items.Count)
            SelectedIndex = index;
        base.OnMouseClick(e);
    }
}
