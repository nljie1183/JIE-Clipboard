using JIE剪切板.Services;
using System.Drawing.Drawing2D;

namespace JIE剪切板.Controls;

/// <summary>
/// 自定义导航列表控件。
/// 显示在主窗口左侧的导航栏，每一项包含 Emoji 图标和文本。
/// 完全自绘（Owner-Draw），支持鼠标悬停高亮、点击选中、主题色背景。
/// </summary>
public class NavigationListBox : Control
{
    /// <summary>导航项数据模型：显示文本 + 图标（Unicode Emoji）</summary>
    public class NavItem
    {
        public string Text { get; set; } = "";
        public string Icon { get; set; } = "";
    }

    private readonly List<NavItem> _items = new();  // 导航项列表
    private int _selectedIndex = 0;                  // 当前选中索引
    private int _hoverIndex = -1;                    // 鼠标悬停索引

    // DPI 自适应的尺寸参数
    private int ItemHeight => DpiHelper.Scale(44);    // 每项高度
    private int IconSize => DpiHelper.Scale(24);      // 图标大小
    private int IconLeft => DpiHelper.Scale(16);      // 图标左侧边距
    private int TextLeft => DpiHelper.Scale(48);      // 文本左侧起始位置
    private float IconFontSize => DpiHelper.ScaleF(12f);
    private readonly Font _iconFont;                  // Emoji 图标字体

    /// <summary>选中项变化事件，参数为新索引</summary>
    public event EventHandler<int>? SelectedIndexChanged;
    /// <summary>当前选中索引，设置后自动重绘并触发事件</summary>
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

    /// <summary>
    /// 构造函数：初始化导航项列表、启用双缓冲绘制。
    /// 导航项为硬编码（全部记录、通用设置、快捷键、外观、安全、导出导入、关于）。
    /// </summary>
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

        _iconFont = new Font("Segoe UI Emoji", DpiHelper.ScaleF(12f));
    }

    /// <summary>自绘所有导航项：选中项用主题色背景+白色文字，悬停项用悬停色背景</summary>
    protected override void OnPaint(PaintEventArgs e)
    {
        try
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.Clear(ThemeService.SidebarBackground);

            var font = ThemeService.GlobalFont;
            int itemH = ItemHeight;

            for (int i = 0; i < _items.Count; i++)
            {
                var rect = new Rectangle(0, i * itemH, Width, itemH);
                var item = _items[i];

                if (i == _selectedIndex)
                {
                    using var brush = new SolidBrush(ThemeService.ThemeColor);
                    g.FillRectangle(brush, rect);
                    DrawItem(g, item, rect, Color.White, font, itemH);
                }
                else if (i == _hoverIndex)
                {
                    using var brush = new SolidBrush(ThemeService.HoverColor);
                    g.FillRectangle(brush, rect);
                    DrawItem(g, item, rect, ThemeService.TextColor, font, itemH);
                }
                else
                {
                    DrawItem(g, item, rect, ThemeService.TextColor, font, itemH);
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Log("Navigation paint failed", ex);
        }
    }

    /// <summary>绘制单个导航项：Emoji 图标 + 文本</summary>
    private void DrawItem(Graphics g, NavItem item, Rectangle rect, Color textColor, Font font, int itemH)
    {
        int iconSize = IconSize;
        int iconLeft = IconLeft;
        int textLeft = TextLeft;

        // 绘制 Emoji 图标
        var iconRect = new Rectangle(iconLeft, rect.Y + (itemH - iconSize) / 2, iconSize, iconSize);
        using (var iconBrush = new SolidBrush(textColor))
        using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
        {
            g.DrawString(item.Icon, _iconFont, iconBrush, iconRect, sf);
        }

        // 绘制文本
        var textRect = new Rectangle(textLeft, rect.Y, rect.Width - textLeft - DpiHelper.Scale(5), itemH);
        using var textBrush = new SolidBrush(textColor);
        using var textFormat = new StringFormat
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter
        };
        g.DrawString(item.Text, font, textBrush, textRect, textFormat);
    }

    /// <summary>鼠标移动时更新悬停高亮状态</summary>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        int itemH = ItemHeight;
        int index = e.Y / itemH;
        if (index != _hoverIndex && index >= 0 && index < _items.Count)
        {
            _hoverIndex = index;
            Invalidate();
        }
        base.OnMouseMove(e);
    }

    /// <summary>鼠标离开时清除悬停状态</summary>
    protected override void OnMouseLeave(EventArgs e)
    {
        _hoverIndex = -1;
        Invalidate();
        base.OnMouseLeave(e);
    }

    /// <summary>鼠标点击时更新选中项</summary>
    protected override void OnMouseClick(MouseEventArgs e)
    {
        int itemH = ItemHeight;
        int index = e.Y / itemH;
        if (index >= 0 && index < _items.Count)
            SelectedIndex = index;
        base.OnMouseClick(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _iconFont?.Dispose();
        base.Dispose(disposing);
    }
}
