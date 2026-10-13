using System;
using System.IO;
using Xunit;

namespace RoundedTB.Tests;

public class SettingsPersistenceTests
{
    [Fact]
    public void RepeatedSavingProducesReadableSettingsAndNoTemporaryFiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), "RoundedTB-settings-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "rtb.json");
            for (int i = 0; i < 25; i++)
            {
                SettingsPersistence.Write(path, new Types.Settings { AppOpenRevealSeconds = i + 1 });
                Assert.Equal(i + 1, SettingsPersistence.Read(path, null).AppOpenRevealSeconds);
            }
            Assert.Single(Directory.GetFiles(directory));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void CorruptSettingsArePreservedAndDefaultsCanLoad()
    {
        var directory = Path.Combine(Path.GetTempPath(), "RoundedTB-settings-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "rtb.json");
            const string invalid = "{\"DynamicAppListLayout\":{";
            File.WriteAllText(path, invalid);
            string report = null;
            Assert.Null(SettingsPersistence.Read(path, message => report = message));
            Assert.Contains("loading defaults", report);
            var backups = Directory.GetFiles(directory, "*.invalid-*.json");
            Assert.Single(backups);
            Assert.Equal(invalid, File.ReadAllText(backups[0]));
            SettingsPersistence.Write(path, new Types.Settings { AppOpenRevealSeconds = 3 });
            Assert.Equal(3, SettingsPersistence.Read(path, null).AppOpenRevealSeconds);
            Assert.Equal(invalid, File.ReadAllText(backups[0]));
        }
        finally { Directory.Delete(directory, true); }
    }
}
