using System;

namespace Screenstop.Core.Geometry;

public static class PlacementResolver
{
    public const int BottomMargin = 24;

    public static PixelRect ResolveBottomCenter(PixelRect screenBounds, int panelWidth, int panelHeight)
    {
        if (panelWidth <= 0 || panelHeight <= 0)
        {
            return new PixelRect(screenBounds.X, screenBounds.Y, 0, 0);
        }

        int x = screenBounds.X + (screenBounds.Width - panelWidth) / 2;
        if (x < screenBounds.X)
        {
            x = screenBounds.X;
        }

        int y = screenBounds.Bottom - panelHeight - BottomMargin;
        if (y < screenBounds.Y)
        {
            y = screenBounds.Y;
        }

        return new PixelRect(x, y, panelWidth, panelHeight);
    }
}
