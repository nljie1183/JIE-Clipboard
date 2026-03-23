namespace JIE剪切板.Services;

public static class DpiHelper
{
    private static float _scaleFactor = 1.0f;

    public static float ScaleFactor => _scaleFactor;

    public static void Initialize()
    {
        try
        {
            using var g = Graphics.FromHwnd(IntPtr.Zero);
            _scaleFactor = g.DpiX / 96f;
        }
        catch
        {
            _scaleFactor = 1.0f;
        }
    }

    public static int Scale(int value) => (int)Math.Round(value * _scaleFactor);

    public static float ScaleF(float value) => value * _scaleFactor;

    public static Size Scale(Size size) => new(Scale(size.Width), Scale(size.Height));

    public static Padding Scale(Padding padding) =>
        new(Scale(padding.Left), Scale(padding.Top), Scale(padding.Right), Scale(padding.Bottom));

    public static Point Scale(Point point) => new(Scale(point.X), Scale(point.Y));
}
