using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace RoundedTB;

internal static class NativeTaskbarSymbols
{
    internal const int MaxPdbBytes = 160 * 1024 * 1024;
    private static readonly object Gate = new();
    private static readonly HashSet<string> Pending = new();
    private static readonly Dictionary<string, DateTime> RetryAfter = new();
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(2) };
    internal static string CacheDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RoundedTB", "NativeSymbols", "Resolved-v1");

    internal static string ReadCached(NativePdbSymbols.Module taskbar, NativePdbSymbols.Module view, string directory)
    {
        var a = ReadModuleCache(taskbar, directory);
        var b = ReadModuleCache(view, directory);
        if (a == null || b == null) return null;
        string descriptor = new JObject { ["Version"] = 1, ["Modules"] = new JArray(a, b) }.ToString();
        NativeTaskbarPin.ValidateDescriptor(descriptor, taskbar.Identity, view.Identity);
        return descriptor;
    }

    private static JObject ReadModuleCache(NativePdbSymbols.Module module, string directory)
    {
        try
        {
            var item = JObject.Parse(File.ReadAllText(CachePath(module, directory)));
            if ((int?)item["ResolverVersion"] != 1 || (string)item["Name"] != module.Name
                || (string)item["Sha256"] != module.Sha256 || (string)item["PdbKey"] != module.SymbolKey
                || (uint?)item["Timestamp"] != module.Identity.Timestamp || (uint?)item["ImageSize"] != module.Identity.ImageSize)
                return null;
            var wanted = module.Name.Equals("taskbar.dll", StringComparison.OrdinalIgnoreCase) ? NativePdbSymbols.TaskbarNames : NativePdbSymbols.ViewNames;
            foreach (var name in wanted)
            {
                if ((string)item["Symbols"]?[name.Key]?["Name"] != name.Value) return null;
                ulong rva = (ulong?)item["Symbols"]?[name.Key]?["Rva"] ?? 0;
                if (rva == 0 || rva >= module.Identity.ImageSize || !module.Sections.Any(section =>
                    (section.SectionCharacteristics & System.Reflection.PortableExecutable.SectionCharacteristics.MemExecute) != 0
                    && rva >= (uint)section.VirtualAddress && rva - (uint)section.VirtualAddress < (uint)section.VirtualSize)) return null;
            }
            return item;
        }
        catch (Exception error) when (error is IOException or Newtonsoft.Json.JsonException or ArgumentException or OverflowException or FormatException or InvalidCastException or InvalidOperationException)
        { return null; }
    }

    internal static void Prepare(NativePdbSymbols.Module taskbar, NativePdbSymbols.Module view, Action<string> log, Action ready)
    {
        string key = taskbar.Sha256 + view.Sha256;
        lock (Gate)
        {
            if (Pending.Contains(key) || (RetryAfter.TryGetValue(key, out var retry) && retry > DateTime.UtcNow)) return;
            Pending.Add(key);
        }
        _ = Task.Run(async () =>
        {
            try
            {
                log("Resolving exact Windows taskbar symbols from Microsoft in the background; existing work-area fallback remains active.");
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                Directory.CreateDirectory(CacheDirectory);
                await ResolveModuleAsync(taskbar, CacheDirectory, Client, timeout.Token);
                await ResolveModuleAsync(view, CacheDirectory, Client, timeout.Token);
                if (ReadCached(taskbar, view, CacheDirectory) == null) throw new InvalidDataException("Resolved symbol cache is incomplete.");
                log("Exact Windows taskbar symbols verified and cached.");
                ready?.Invoke();
            }
            catch (Exception error)
            {
                log("Windows taskbar symbol resolution unavailable; keeping work-area fallback: " + error.Message);
                lock (Gate) RetryAfter[key] = DateTime.UtcNow.AddMinutes(15);
            }
            finally { lock (Gate) Pending.Remove(key); }
        });
    }

    internal static Uri SymbolUri(NativePdbSymbols.Module module) => new("https://msdl.microsoft.com/download/symbols/"
        + Uri.EscapeDataString(module.PdbName) + "/" + module.SymbolKey + "/" + Uri.EscapeDataString(module.PdbName));

    internal static async Task ResolveModuleAsync(NativePdbSymbols.Module module, string directory, HttpClient client, CancellationToken cancel)
    {
        if (ReadModuleCache(module, directory) != null) return;
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".pdb.tmp");
        try
        {
            using var response = await client.GetAsync(SymbolUri(module), HttpCompletionOption.ResponseHeadersRead, cancel);
            response.EnsureSuccessStatusCode();
            if (response.RequestMessage?.RequestUri?.Scheme != Uri.UriSchemeHttps)
                throw new InvalidDataException("Symbol download requires HTTPS.");
            if (response.Content.Headers.ContentLength > MaxPdbBytes)
                throw new InvalidDataException("Symbol download exceeds the size limit.");
            await using (var input = await response.Content.ReadAsStreamAsync(cancel))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                await CopyLimitedAsync(input, output, MaxPdbBytes, cancel);
            }
            var wanted = module.Name.Equals("taskbar.dll", StringComparison.OrdinalIgnoreCase) ? NativePdbSymbols.TaskbarNames : NativePdbSymbols.ViewNames;
            Dictionary<string, ulong> addresses;
            using (var input = File.OpenRead(temporary)) addresses = NativePdbSymbols.Resolve(input, module, wanted);
            var symbols = new JObject();
            foreach (var address in addresses) symbols[address.Key] = new JObject { ["Name"] = wanted[address.Key], ["Rva"] = address.Value };
            var descriptor = new JObject
            {
                ["ResolverVersion"] = 1, ["Name"] = module.Name, ["Sha256"] = module.Sha256, ["PdbKey"] = module.SymbolKey,
                ["Timestamp"] = module.Identity.Timestamp, ["ImageSize"] = module.Identity.ImageSize, ["Symbols"] = symbols
            };
            string json = temporary + ".json";
            try
            {
                await File.WriteAllTextAsync(json, descriptor.ToString(), cancel);
                File.Move(json, CachePath(module, directory), overwrite: true);
            }
            finally { if (File.Exists(json)) File.Delete(json); }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal static async Task CopyLimitedAsync(Stream input, Stream output, int limit, CancellationToken cancel)
    {
        byte[] buffer = new byte[81920];
        int count, total = 0;
        while ((count = await input.ReadAsync(buffer, cancel)) != 0)
        {
            if (count > limit - total) throw new InvalidDataException("Symbol download exceeds the size limit.");
            await output.WriteAsync(buffer.AsMemory(0, count), cancel); total += count;
        }
    }

    private static string CachePath(NativePdbSymbols.Module module, string directory) => Path.Combine(directory, module.Sha256 + ".json");
}
