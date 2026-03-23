using JIE剪切板.Services;
using System.Drawing.Drawing2D;

namespace JIE剪切板.Controls;


public class ToggleSwitch : Control
{
    private bool _checked;
    private bool _hover;

    public bool Checked
    {
        get => _checked;
        set { _checked = value; Invalidate(); CheckedChanged?.Invoke(this, EventArgs.Empty); }
    }

    public event EventHandler? CheckedChanged;

    public ToggleSwitch()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer, true);
        Size = new Size(DpiHelper.Scale(44), DpiHelper.Scale(22));
        Cursor = Cursors.Hand;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        try
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var trackColor = _checked ? ThemeService.ThemeColor : Color.FromArgb(180, 180, 180);
            if (_hover) trackColor = ControlPaint.Light(trackColor, 0.2f);

            int h = Height;
            int w = Width;
            int radius = h / 2;

            using var trackPath = new GraphicsPath();
            trackPath.AddArc(0, 0, h, h, 90, 180);
            trackPath.AddArc(w - h, 0, h, h, 270, 180);
            trackPath.CloseFigure();

            using var trackBrush = new SolidBrush(trackColor);
            g.FillPath(trackBrush, trackPath);

            // Thumb
            int thumbSize = h - 4;
            int thumbX = _checked ? w - thumbSize - 2 : 2;
            using var thumbBrush = new SolidBrush(Color.White);
            g.FillEllipse(thumbBrush, thumbX, 2, thumbSize, thumbSize);
        }
        catch { }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnClick(EventArgs e)
    {
        Checked = !_checked;
        base.OnClick(e);
    }
}
