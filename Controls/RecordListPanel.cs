using JIE剪切板.Models;
using JIE剪切板.Services;
using System.Drawing.Drawing2D;

namespace JIE剪切板.Controls;

public class RecordListPanel : Panel
{
    private List<ClipboardRecord> _records = new();
    private int _hoverIndex = -1;
    private int _scrollOffset = 0;
    private int _itemHeight = 60;
    private readonly VScrollBar _scrollBar;
    private readonly Dictionary<string, Image?> _thumbnailCache = new();

    public event EventHandler<ClipboardRecord>? RecordClicked;
    public event EventHandler<(ClipboardRecord record, Point location)>? RecordRightClicked;

    public RecordListPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Color.White;

        _scrollBar = new VScrollBar { Dock = DockStyle.Right, Visible = false };
        _scrollBar.Scroll += (_, _) => { _scrollOffset = _scrollBar.Value; Invalidate(); };
        Controls.Add(_scrollBar);
    }

    public void SetRecords(List<ClipboardRecord> records)
    {
        _records = records;
        _hoverIndex = -1;
        _scrollOffset = 0;
        UpdateScrollBar();
        Invalidate();
    }

    public void SetItemHeight(int height)
    {
        _itemHeight = Math.Max(40, height);
        UpdateScrollBar();
        Invalidate();
    }

    private void UpdateScrollBar()
    {
        int totalHeight = _records.Count * _itemHeight;
        int clientWidth = Width - (_scrollBar.Visible ? _scrollBar.Width : 0);
        if (totalHeight > Height)
        {
            _scrollBar.Visible = true;
            _scrollBar.Maximum = totalHeight - Height + _scrollBar.LargeChange;
            _scrollBar.Value = Math.Min(_scrollBar.Value, _scrollBar.Maximum);
        }
        else
        {
            _scrollBar.Visible = false;
            _scrollOffset = 0;
        }
    }

    protected override void OnResize(EventArgs e)
    {
        UpdateScrollBar();
        base.OnResize(e);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (_scrollBar.Visible)
        {
            _scrollOffset = Math.Clamp(_scrollOffset - e.Delta / 3, 0,
                Math.Max(0, _records.Count * _itemHeight - Height));
            _scrollBar.Value = Math.Min(_scrollOffset, _scrollBar.Maximum);
            Invalidate();
        }
        base.OnMouseWheel(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        try
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.Clear(ThemeService.WindowBackground);

            int clientWidth = Width - (_scrollBar.Visible ? _scrollBar.Width : 0);

            if (_records.Count == 0)
            {
                using var brush = new SolidBrush(ThemeService.SecondaryTextColor);
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString("暂无记录", ThemeService.GlobalFont, brush, new RectangleF(0, 0, clientWidth, Height), sf);
                return;
            }

            int startIdx = Math.Max(0, _scrollOffset / _itemHeight);
            int endIdx = Math.Min(_records.Count, startIdx + (Height / _itemHeight) + 2);

            for (int i = startIdx; i < endIdx; i++)
            {
                int y = i * _itemHeight - _scrollOffset;
                if (y > Height) break;
                if (y + _itemHeight < 0) continue;

                var rect = new Rectangle(0, y, clientWidth, _itemHeight);
                DrawRecord(g, _records[i], rect, i == _hoverIndex);
            }
        }
        catch (Exception ex)
        {
            LogService.Log("Record list paint failed", ex);
        }
    }

    private void DrawRecord(Graphics g, ClipboardRecord record, Rectangle rect, bool isHover)
    {
        // Background
        if (isHover)
        {
            using var brush = new SolidBrush(ThemeService.HoverColor);
            g.FillRectangle(brush, rect);
        }

        // Pin indicator
        if (record.IsPinned)
        {
            using var pinFont = new Font("Segoe UI Emoji", 9f);
            using var pinBrush = new SolidBrush(ThemeService.ThemeColor);
            g.DrawString("📌", pinFont, pinBrush, rect.X + 5, rect.Y + 4);
        }

        int leftMargin = record.IsPinned ? 25 : 10;
        int rightMargin = 100;
        int contentWidth = rect.Width - leftMargin - rightMargin;

        // Content preview
        if (record.ContentType == ClipboardContentType.Image && !record.IsEncrypted)
        {
            DrawImageThumbnail(g, record, new Rectangle(leftMargin, rect.Y + 4, _itemHeight - 8, _itemHeight - 8));
            leftMargin += _itemHeight - 4;
            contentWidth -= _itemHeight + 4;
        }

        var preview = ClipboardService.GetContentPreview(record, 120);
        var textRect = new Rectangle(leftMargin, rect.Y + 8, contentWidth, _itemHeight - 16);
        using (var textBrush = new SolidBrush(ThemeService.TextColor))
        {
            var sf = new StringFormat
            {
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap,
                LineAlignment = StringAlignment.Center
            };
            g.DrawString(preview, ThemeService.GlobalFont, textBrush, textRect, sf);
        }

        // Time
        var timeStr = FormatTime(record.CreateTime);
        var timeRect = new Rectangle(rect.Right - rightMargin, rect.Y + 8, rightMargin - 10, _itemHeight - 16);
        using (var timeBrush = new SolidBrush(ThemeService.SecondaryTextColor))
        {
            var sf = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };
            using var smallFont = new Font(ThemeService.GlobalFont.FontFamily, ThemeService.GlobalFont.Size - 1);
            g.DrawString(timeStr, smallFont, timeBrush, timeRect, sf);
        }

        // Bottom border
        using var pen = new Pen(ThemeService.BorderColor, 1);
        g.DrawLine(pen, rect.Left + 10, rect.Bottom - 1, rect.Right - 10, rect.Bottom - 1);
    }

    private void DrawImageThumbnail(Graphics g, ClipboardRecord record, Rectangle rect)
    {
        try
        {
            if (!_thumbnailCache.TryGetValue(record.Id, out var thumb))
            {
                if (File.Exists(record.Content))
                {
                    using var original = Image.FromFile(record.Content);
                    thumb = original.GetThumbnailImage(rect.Width, rect.Height, () => false, IntPtr.Zero);
                }
                _thumbnailCache[record.Id] = thumb;
            }

            if (thumb != null)
                g.DrawImage(thumb, rect);
            else
            {
                using var brush = new SolidBrush(Color.FromArgb(200, 200, 200));
                g.FillRectangle(brush, rect);
            }
        }
        catch
        {
            using var brush = new SolidBrush(Color.FromArgb(200, 200, 200));
            g.FillRectangle(brush, rect);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int index = (e.Y + _scrollOffset) / _itemHeight;
        if (index != _hoverIndex)
        {
            _hoverIndex = (index >= 0 && index < _records.Count) ? index : -1;
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
        int index = (e.Y + _scrollOffset) / _itemHeight;
        if (index >= 0 && index < _records.Count)
        {
            if (e.Button == MouseButtons.Left)
                RecordClicked?.Invoke(this, _records[index]);
            else if (e.Button == MouseButtons.Right)
                RecordRightClicked?.Invoke(this, (_records[index], e.Location));
        }
        base.OnMouseClick(e);
    }

    private static string FormatTime(DateTime utcTime)
    {
        var local = utcTime.ToLocalTime();
        var now = DateTime.Now;
        if (local.Date == now.Date) return $"今天 {local:HH:mm}";
        if (local.Date == now.Date.AddDays(-1)) return $"昨天 {local:HH:mm}";
        return local.ToString("MM-dd HH:mm");
    }

    public void ClearThumbnailCache()
    {
        foreach (var img in _thumbnailCache.Values)
            img?.Dispose();
        _thumbnailCache.Clear();
    }
}
