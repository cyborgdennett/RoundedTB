using Xunit;

namespace RoundedTB.Tests;

public class AppOpenRevealTests
{
    [Fact]
    public void RevealExpiresAfterOneSecondAndRequiresTaskbarHoverAndOptIn()
    {
        var taskbar = new Types.Taskbar { AppOpenRevealStartedAt = 1000 };
        var settings = new Types.Settings { SimpleTaskbarLayout = new() { ShowOnHover = true }, RevealOnAppOpen = true };
        Assert.True(AppOpenReveal.Active(taskbar, settings, 1000));
        Assert.True(AppOpenReveal.Active(taskbar, settings, 1999));
        Assert.False(AppOpenReveal.Active(taskbar, settings, 2000));
        settings.SimpleTaskbarLayout.ShowOnHover = false;
        Assert.False(AppOpenReveal.Active(taskbar, settings, 1500));
        settings.SimpleTaskbarLayout.ShowOnHover = true;
        settings.RevealOnAppOpen = false;
        Assert.False(AppOpenReveal.Active(taskbar, settings, 1500));
    }
    [Theory]
    [InlineData(2.5, 2500)]
    [InlineData(0, 100)]
    [InlineData(90, 60000)]
    [InlineData(double.NaN, 1000)]
    public void ConfigurableDurationIsBounded(double seconds, long milliseconds)
    {
        var settings = new Types.Settings { AppOpenRevealSeconds = seconds };
        Assert.Equal(milliseconds, AppOpenReveal.DurationMilliseconds(settings));
    }

    [Fact]
    public void OldSettingsDefaultToOneSecondAndNewDurationPersists()
    {
        var old = Newtonsoft.Json.JsonConvert.DeserializeObject<Types.Settings>("{}");
        Assert.Equal(1000, AppOpenReveal.DurationMilliseconds(old));
        old.AppOpenRevealSeconds = 3.5;
        var restored = Newtonsoft.Json.JsonConvert.DeserializeObject<Types.Settings>(
            Newtonsoft.Json.JsonConvert.SerializeObject(old));
        Assert.Equal(3500, AppOpenReveal.DurationMilliseconds(restored));
    }
}
