using CodexUsageWidget.Infrastructure.Windows;

namespace CodexUsageWidget.Tests.Infrastructure;

public sealed class CompanionPlacementTests
{
    [Theory]
    [InlineData(0, 0, 168)]
    [InlineData(2300, 0, 2021)]
    [InlineData(-1920, -1920, -1752)]
    public void BadgeStaysBesidePetAtEitherScreenEdge(double petX, double screenX, double expectedLeft)
    {
        var anchor = new CompanionAnchor(true, true, petX, 900, 150, 150, screenX, 0, 2560, 1540);
        var (left, top) = CompanionDesktopTracker.BadgePosition(anchor, 261, 65, 1.5, 1.5);
        Assert.Equal(expectedLeft, left);
        Assert.Equal(948, top);
        Assert.True(left + 261 <= petX || left >= petX + 150);
    }

    [Fact]
    public void BadgeIsClampedAboveTaskbar()
    {
        var anchor = new CompanionAnchor(true, false, 2500, 1500, 0, 0, 0, 0, 2560, 1540);
        var (left, top) = CompanionDesktopTracker.BadgePosition(anchor, 261, 65, 1.5, 1.5);
        Assert.Equal(2291, left);
        Assert.Equal(1467, top);
    }
}
