using JIE剪切板.Models;
using JIE剪切板.Services;
using System.Drawing.Drawing2D;

namespace JIE剪切板.Controls;

public class RecordListPanel : Panel
{
    private List<ClipboardRecord> _records = new();
    private int _hoverIndex = -1;
    private int _scrollOffset = 0;
    private int _itemHeight = DpiHelper.Scale(60);
    private readonly VScrollBar _scrollBar;
    private readonly Dictionary<string, Image?> _thumbnailCache = new();
    private readonly Font _pinFont;
    private Font? _smallFont;
    private float _lastSmallFontSize;

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

        _pinFont = new Font("Segoe UI Emoji", DpiHelper.ScaleF(9f));
    }

    public void SetRecords(List<ClipboardRecord> records)
    {
        _records = records;
        _hoverIndex = -1;
        _scrollOffset = 0;
        if (_thumbnailCache.Count > 200)
            ClearThumbnailCache();
        UpdateScrollBar();
        Invalidate();
    }

    public void SetItemHeight(int height)
    {
        _itemHeight = Math.Max(DpiHelper.Scale(40), DpiHelper.Scale(height));
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
            using var pinBrush = new SolidBrush(ThemeService.ThemeColor);
            g.DrawString("📌", _pinFont, pinBrush, rect.X + DpiHelper.Scale(5), rect.Y + DpiHelper.Scale(4));
        }

        int leftMargin = record.IsPinned ? DpiHelper.Scale(25) : DpiHelper.Scale(10);
        int rightMargin = DpiHelper.Scale(100);
        int contentWidth = rect.Width - leftMargin - rightMargin;

        // Content preview
        if (record.ContentType == ClipboardContentType.Image && !record.IsEncrypted)
        {
            DrawImageThumbnail(g, record, new Rectangle(leftMargin, rect.Y + 4, _itemHeight - 8, _itemHeight - 8));
            leftMargin += _itemHeight - 4;
            contentWidth -= _itemHeight + 4;
        }

        var preview = ClipboardService.GetContentPreview(record, 120);
        var textRect = new Rectangle(leftMargin, rect.Y + DpiHelper.Scale(8), contentWidth, _itemHeight - DpiHelper.Scale(16));
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
        var timeRect = new Rectangle(rect.Right - rightMargin, rect.Y + DpiHelper.Scale(8), rightMargin - DpiHelper.Scale(10), _itemHeight - DpiHelper.Scale(16));
        using (var timeBrush = new SolidBrush(ThemeService.SecondaryTextColor))
        {
            var sf = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };
            g.DrawString(timeStr, GetSmallFont(), timeBrush, timeRect, sf);
        }

        // Bottom border
        using var pen = new Pen(ThemeService.BorderColor, 1);
        g.DrawLine(pen, rect.Left + DpiHelper.Scale(10), rect.Bottom - 1, rect.Right - DpiHelper.Scale(10), rect.Bottom - 1);
    }

    private void DrawImageThumbnail(Graphics g, ClipboardRecord record, Rectangle rect)
    {
        try
        {
            if (!_thumbnailCache.TryGetValue(record.Id, out var thumb))
            {
                byte[]? imageBytes = null;
                if (record.Content.EndsWith(".enc", StringComparison.OrdinalIgnoreCase) && File.Exists(record.Content))
                {
                    // Decrypt encrypted image in memory
                    imageBytes = FileService.DecryptFileBytes(record.Content);
                }
                else if (File.Exists(record.Content))
                {
                    imageBytes = File.ReadAllBytes(record.Content);
                }

                if (imageBytes != null)
                {
                    using var ms = new MemoryStream(imageBytes);
                    using var original = Image.FromStream(ms);
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

    private Font GetSmallFont()
    {
        var targetSize = Math.Max(6f, ThemeService.GlobalFont.Size - 1);
        if (_smallFont == null || _lastSmallFontSize != targetSize)
        {
            _smallFont?.Dispose();
            _smallFont = new Font(ThemeService.GlobalFont.FontFamily, targetSize);
            _lastSmallFontSize = targetSize;
        }
        return _smallFont;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ClearThumbnailCache();
            _pinFont?.Dispose();
            _smallFont?.Dispose();
        }
        base.Dispose(disposing);
    }
}
