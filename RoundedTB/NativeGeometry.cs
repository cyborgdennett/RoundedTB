using System;

namespace RoundedTB;

internal static class NativeGeometry
{
    // Keep metrics well inside GDI's coordinate range even for extreme user
    // input at high DPI, without changing normal positive/negative spacing.
    internal static int Pixel(double value)
        => (int)Math.Clamp(Math.Round(double.IsFinite(value) ? value : 0), -1048576, 1048576);

    internal static IntPtr CreateRegion(int left, int top, int right, int bottom, int radiusX, int radiusY)
    {
        if (right <= left || bottom <= top) return LocalPInvoke.CreateRectRgn(0, 0, 0, 0);
        int limit = (int)Math.Min(32768, 2 * Math.Min((long)right - left, (long)bottom - top));
        return LocalPInvoke.CreateRoundRectRgn(left, top, right, bottom,
            Math.Clamp(radiusX, 0, limit), Math.Clamp(radiusY, 0, limit));
    }
}
