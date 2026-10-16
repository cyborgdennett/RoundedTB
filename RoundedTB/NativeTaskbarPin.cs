using System;
using System.IO;
using System.ComponentModel;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;

namespace RoundedTB;

internal sealed class NativeTaskbarPin : IDisposable
{
    internal readonly record struct ImageIdentity(uint Timestamp, uint ImageSize);
    private readonly TaskbarPinController controller;
    private readonly Action<string> log;
    private volatile bool disposed;

    private NativeTaskbarPin(TaskbarPinController controller, Action<string> log)
    {
        this.controller = controller;
        this.log = log;
    }

    internal bool Active
    {
        get
        {
            try { return !disposed && controller.Status == 1 && LocalPInvoke.FindWindowExA(IntPtr.Zero, IntPtr.Zero, "Shell_TrayWnd", null) == controller.Taskbar; }
            catch (ObjectDisposedException) { return false; }
        }
    }

    internal static NativeTaskbarPin TryStart(Action<string> log, Action symbolsReady = null, Action attachmentPending = null)
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8) return null;
        var callback = log;
        log = message => { try { callback?.Invoke(message); } catch { } };
        try
        {
            string nativeDirectory = Path.Combine(AppContext.BaseDirectory, "Native");
            string source = Path.Combine(nativeDirectory, "RoundedTB.TaskbarPin.dll");
            string descriptors = Path.Combine(nativeDirectory, "symbols");
            if (!File.Exists(source))
            {
                log?.Invoke("Native taskbar pin unavailable: packaged helper missing; using work-area fallback.");
                return null;
            }
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string taskbarPath = Path.Combine(Environment.SystemDirectory, "taskbar.dll");
            string viewPath = Path.Combine(windows, "SystemApps", "MicrosoftWindows.Client.Core_cw5n1h2txyewy", "Taskbar.View.dll");
            var taskbar = ReadIdentity(taskbarPath);
            var view = ReadIdentity(viewPath);
            TaskbarPinController.Configuration? matching = null;
            foreach (string descriptor in (Directory.Exists(descriptors) ? Directory.EnumerateFiles(descriptors, "*.json") : Array.Empty<string>()).OrderBy(path => path, StringComparer.Ordinal))
            {
                try
                {
                    matching = ValidateDescriptor(File.ReadAllText(descriptor), taskbar, view);
                    break;
                }
                catch (Exception error) when (error is ArgumentException or InvalidDataException or Newtonsoft.Json.JsonException or IOException or OverflowException or FormatException)
                {
                    log?.Invoke("Native taskbar pin cache skipped: " + Path.GetFileName(descriptor) + ": " + error.Message);
                }
            }
            if (matching == null)
            {
                var taskbarModule = NativePdbSymbols.ReadModule(taskbarPath);
                var viewModule = NativePdbSymbols.ReadModule(viewPath);
                string cached = NativeTaskbarSymbols.ReadCached(taskbarModule, viewModule, NativeTaskbarSymbols.CacheDirectory);
                if (cached != null) matching = ValidateDescriptor(cached, taskbar, view);
                else
                {
                    NativeTaskbarSymbols.Prepare(taskbarModule, viewModule, log, symbolsReady);
                    return null;
                }
            }
            string hash;
            using (var stream = File.OpenRead(source)) hash = Convert.ToHexString(SHA256.HashData(stream));
            string cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RoundedTB", "Native");
            Directory.CreateDirectory(cache);
            string immutable = Path.Combine(cache, "RoundedTB.TaskbarPin-" + hash + ".dll");
            if (!File.Exists(immutable)) File.Copy(source, immutable);
            using (var stream = File.OpenRead(immutable))
                if (!string.Equals(Convert.ToHexString(SHA256.HashData(stream)), hash, StringComparison.Ordinal))
                    throw new InvalidDataException("Cached native helper checksum does not match packaged helper.");
            var native = new TaskbarPinController(immutable, matching.Value);
            log?.Invoke("Native taskbar pin active: native reservation released; RoundedTB controls segment visibility.");
            return new NativeTaskbarPin(native, log);
        }
        catch (Exception error)
        {
            log?.Invoke("Native taskbar pin unavailable; using work-area fallback: " + error.Message);
            if (error is Win32Exception { NativeErrorCode: 126 }) attachmentPending?.Invoke();
            return null;
        }
    }

    internal static ImageIdentity ReadIdentity(string path)
    {
        using var stream = File.OpenRead(path);
        return ReadIdentity(stream);
    }

    internal static ImageIdentity ReadIdentity(Stream stream)
    {
        if (!stream.CanSeek || stream.Length < 64) throw new InvalidDataException("Truncated PE DOS header.");
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        stream.Position = 0;
        if (reader.ReadUInt16() != 0x5A4D) throw new InvalidDataException("Invalid PE DOS signature.");
        stream.Position = 0x3C;
        uint offset = reader.ReadUInt32();
        if (offset < 64 || offset > stream.Length - 88) throw new InvalidDataException("Truncated PE header.");
        stream.Position = offset;
        if (reader.ReadUInt32() != 0x00004550 || reader.ReadUInt16() != 0x8664) throw new InvalidDataException("Native pin requires an AMD64 PE image.");
        reader.ReadUInt16();
        uint timestamp = reader.ReadUInt32();
        stream.Position = offset + 20;
        ushort optionalSize = reader.ReadUInt16();
        if (optionalSize < 64 || offset + 24L + optionalSize > stream.Length) throw new InvalidDataException("Truncated PE optional header.");
        stream.Position = offset + 24;
        if (reader.ReadUInt16() != 0x20B) throw new InvalidDataException("Native pin requires PE32+.");
        stream.Position = offset + 24 + 56;
        uint imageSize = reader.ReadUInt32();
        if (imageSize == 0) throw new InvalidDataException("Invalid PE image size.");
        return new ImageIdentity(timestamp, imageSize);
    }

    internal static TaskbarPinController.Configuration ValidateDescriptor(string json, ImageIdentity taskbar, ImageIdentity view)
    {
        var descriptor = JObject.Parse(json);
        if ((int?)descriptor["Version"] != 1 || descriptor["Modules"] is not JArray modules)
            throw new InvalidDataException("Unsupported native symbol descriptor.");
        JObject Module(string name, ImageIdentity identity)
        {
            var matches = modules.OfType<JObject>().Where(module => string.Equals((string)module["Name"], name, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1 || (uint?)matches[0]["Timestamp"] != identity.Timestamp || (uint?)matches[0]["ImageSize"] != identity.ImageSize)
                throw new InvalidDataException("Exact PE identity mismatch: " + name);
            return matches[0];
        }
        ulong Rva(JObject module, string symbol, ImageIdentity identity, bool optional = false)
        {
            var token = module["Symbols"]?[symbol]?["Rva"];
            if (token == null && optional) return 0;
            ulong value = token == null ? 0 : token.Value<ulong>();
            if (value == 0 || value >= identity.ImageSize) throw new InvalidDataException("Symbol RVA outside PE image: " + symbol);
            return value;
        }
        var native = Module("taskbar.dll", taskbar);
        var visual = Module("Taskbar.View.dll", view);
        return new TaskbarPinController.Configuration
        {
            TaskbarTimestamp = taskbar.Timestamp, TaskbarImageSize = taskbar.ImageSize,
            ViewTimestamp = view.Timestamp, ViewImageSize = view.ImageSize,
            Hide = Rva(native, "Hide", taskbar), SecondaryHide = Rva(native, "SecondaryAutoHide", taskbar, optional: true),
            ShouldExpand = Rva(visual, "ShouldExpand", view), UpdateExpanded = Rva(visual, "UpdateIsExpanded", view, optional: true),
            TaskbarModule = "taskbar.dll", ViewModule = "Taskbar.View.dll"
        };
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try { controller.Dispose(); }
        catch (Exception error) { log?.Invoke("Native taskbar pin cleanup failed: " + error.Message); }
    }
}
