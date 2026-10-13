using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace RoundedTB;

internal static class SettingsPersistence
{
    internal static Types.Settings Read(string path, Action<string> report)
    {
        var json = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonConvert.DeserializeObject<Types.Settings>(json); }
        catch (JsonException error)
        {
            var backup = path + ".invalid-" + Guid.NewGuid().ToString("N") + ".json";
            File.Copy(path, backup);
            report?.Invoke($"Invalid settings preserved at {backup}; loading defaults. {error.Message}");
            return null;
        }
    }

    internal static void Write(string path, Types.Settings settings)
    {
        // Serialize before touching disk. A same-directory atomic rename avoids
        // truncating the previous document if saving is interrupted or fails.
        var json = JsonConvert.SerializeObject(settings, Formatting.Indented);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                using var writer = new StreamWriter(file, new UTF8Encoding(false), 1024, leaveOpen: true);
                writer.Write(json);
                writer.Flush();
                file.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
