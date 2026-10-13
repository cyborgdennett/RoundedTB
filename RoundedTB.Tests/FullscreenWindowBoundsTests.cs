using Xunit;

namespace RoundedTB.Tests;

public class FullscreenWindowBoundsTests
{
    [Theory]
    [InlineData("Chrome_WidgetWin_1", 0x170B0000u, 0, 1, true)]
    [InlineData("Chrome_WidgetWin_1", 0x170B0000u, 0, 2, true)]
    [InlineData("Chrome_WidgetWin_1", 0x170B0000u, 0, 0, false)]
    [InlineData("Chrome_WidgetWin_1", 0x170B0000u, 0, 3, false)]
    [InlineData("Chrome_WidgetWin_1", 0x17CF0000u, 0, 1, false)]
    [InlineData("Chrome_WidgetWin_1", 0x170B0000u, 8, 1, false)]
    [InlineData("Shell_TrayWnd", 0x170B0000u, 0, 1, false)]
    public void OnlyRepairsChromiumFullscreenBottomGap(string name, uint style, int leftInset, int gap, bool expected)
    {
        var monitor = new LocalPInvoke.RECT { Left = -1920, Top = -1080, Right = 0, Bottom = 0 };
        var window = new LocalPInvoke.RECT { Left = -1920 + leftInset, Top = -1080, Right = 0, Bottom = -gap };
        Assert.Equal(expected, FullscreenWindowBounds.HasBottomGap(name, style, window, monitor));
    }

    [Theory]
    [InlineData("Chrome_WidgetWin_1", 0x170B0000u, 0, 0, true)]
    [InlineData("Chrome_WidgetWin_1", 0x170B0000u, 0, 1, true)]
    [InlineData("Chrome_WidgetWin_1", 0x170B0000u, 0, 2, true)]
    [InlineData("Chrome_WidgetWin_1", 0x170B0000u, 0, 3, false)]
    [InlineData("Chrome_WidgetWin_1", 0x17CF0000u, 0, 0, false)]
    [InlineData("Chrome_WidgetWin_1", 0x170B0000u, 8, 0, false)]
    [InlineData("Progman", 0x170B0000u, 0, 0, false)]
    [InlineData("WorkerW", 0x170B0000u, 0, 0, false)]
    [InlineData("MultitaskingViewFrame", 0x170B0000u, 0, 0, false)]
    public void ForegroundFullscreenExcludesExpandedMaximizedWindowsAndShellSurfaces(string name, uint style, int leftInset, int gap, bool expected)
    {
        var monitor = new LocalPInvoke.RECT { Left = -1920, Top = -1080, Right = 0, Bottom = 0 };
        var window = new LocalPInvoke.RECT { Left = -1920 + leftInset, Top = -1080, Right = 0, Bottom = -gap };
        Assert.Equal(expected, FullscreenWindowBounds.IsFullscreen(name, style, window, monitor));
    }
}
