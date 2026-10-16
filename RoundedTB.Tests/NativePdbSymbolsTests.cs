using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Newtonsoft.Json.Linq;

namespace RoundedTB.Tests;

public class NativePdbSymbolsTests
{
    private static readonly Guid Id = new("00112233-4455-6677-8899-aabbccddeeff");

    private static NativePdbSymbols.Module Module(string name = "taskbar.dll")
    {
        byte[] pe = new byte[512];
        using (var writer = new BinaryWriter(new MemoryStream(pe)))
        {
            writer.Write((ushort)0x5a4d); writer.BaseStream.Position = 0x3c; writer.Write(128);
            writer.BaseStream.Position = 128; writer.Write(0x4550); writer.Write((ushort)0x8664); writer.Write((ushort)1);
            writer.BaseStream.Position = 148; writer.Write((ushort)240);
            writer.BaseStream.Position = 152; writer.Write((ushort)0x20b);
            writer.BaseStream.Position = 208; writer.Write(0x4000);
            writer.BaseStream.Position = 392; writer.Write(Encoding.ASCII.GetBytes(".text\0\0\0"));
            writer.Write(0x1000); writer.Write(0x1000);
            writer.BaseStream.Position = 428; writer.Write(0x60000020);
        }
        using var reader = new PEReader(new MemoryStream(pe));
        return new(name, new(123, 0x4000), Id, 1, "Taskbar.pdb", name == "taskbar.dll" ? "AAA" : "BBB", reader.PEHeaders.SectionHeaders.ToArray());
    }

    private static byte[] Pdb(bool ambiguous = false, bool badSignature = false, bool badSection = false, bool omap = false, int infoAge = 1, int dbiAge = 1)
    {
        using var records = new MemoryStream();
        using (var w = new BinaryWriter(records, Encoding.UTF8, true))
        {
            void Symbol(string name, uint address, ushort section = 1)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(name);
                w.Write((ushort)(13 + bytes.Length)); w.Write((ushort)0x110e);
                w.Write(2u); w.Write(address); w.Write(section); w.Write(bytes); w.Write((byte)0);
            }
            // Force wanted records across an MSF block boundary.
            Symbol(new string('x', 600), 0);
            Symbol(NativePdbSymbols.TaskbarNames["Hide"] + (badSignature ? "changed" : ""), 0x120, (ushort)(badSection ? 2 : 1));
            Symbol(NativePdbSymbols.TaskbarNames["SecondaryAutoHide"], 0x240);
            Symbol(NativePdbSymbols.ViewNames["ShouldExpand"], 0x330);
            if (ambiguous) Symbol(NativePdbSymbols.TaskbarNames["Hide"], 0x140);
        }
        var info = new byte[28]; BitConverter.GetBytes(infoAge).CopyTo(info, 8); Id.ToByteArray().CopyTo(info, 12);
        var dbi = new byte[86]; BitConverter.GetBytes(-1).CopyTo(dbi, 0); BitConverter.GetBytes(dbiAge).CopyTo(dbi, 8);
        BitConverter.GetBytes((ushort)4).CopyTo(dbi, 20); BitConverter.GetBytes(22).CopyTo(dbi, 48);
        BitConverter.GetBytes((ushort)0x8664).CopyTo(dbi, 58);
        for (int i = 64; i < dbi.Length; i++) dbi[i] = 0xff;
        if (omap) BitConverter.GetBytes((ushort)4).CopyTo(dbi, 70);
        byte[][] streams = [[], info, [], dbi, records.ToArray()];
        const int block = 512;
        int count = 3 + streams.Sum(s => (s.Length + block - 1) / block);
        byte[] pdb = new byte[count * block];
        using var output = new BinaryWriter(new MemoryStream(pdb));
        output.Write("Microsoft C/C++ MSF 7.00\r\n\u001aDS\0\0\0"u8);
        output.Write(block); output.Write(0); output.Write(count);
        output.Write(4 + streams.Length * 4 + (count - 3) * 4); output.Write(0); output.Write(1);
        output.BaseStream.Position = block; output.Write(2);
        output.BaseStream.Position = 2 * block; output.Write(streams.Length);
        foreach (var stream in streams) output.Write(stream.Length);
        int next = 3;
        foreach (var stream in streams)
            for (int offset = 0; offset < stream.Length; offset += block)
            {
                output.Write(next);
                Array.Copy(stream, offset, pdb, next * block, Math.Min(block, stream.Length - offset)); next++;
            }
        return pdb;
    }

    [Fact]
    public void ResolvesExactSignaturesAcrossMsfBlocks()
    {
        using var stream = new MemoryStream(Pdb());
        var result = NativePdbSymbols.Resolve(stream, Module(), NativePdbSymbols.TaskbarNames);
        Assert.Equal(0x1120ul, result["Hide"]);
        Assert.Equal(0x1240ul, result["SecondaryAutoHide"]);
    }

    [Theory]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, false, true)]
    public void RejectsAmbiguitySignatureChangesInvalidSectionsAndOmap(bool ambiguous, bool signature, bool section, bool omap)
    {
        using var stream = new MemoryStream(Pdb(ambiguous, signature, section, omap));
        Assert.Throws<InvalidDataException>(() => NativePdbSymbols.Resolve(stream, Module(), NativePdbSymbols.TaskbarNames));
    }

    [Fact]
    public void RejectsForeignPdbIdentityAndAge()
    {
        foreach (var module in new[] { Module() with { Guid = Guid.NewGuid() }, Module() with { Age = 2 } })
        {
            using var stream = new MemoryStream(Pdb());
            Assert.Throws<InvalidDataException>(() => NativePdbSymbols.Resolve(stream, module, NativePdbSymbols.TaskbarNames));
        }
    }

    [Fact]
    public void StrippedPublicPdbUsesMatchingDbiAgeDespiteNewerInfoAge()
    {
        using var valid = new MemoryStream(Pdb(infoAge: 3));
        Assert.Equal(0x1120ul, NativePdbSymbols.Resolve(valid, Module(), NativePdbSymbols.TaskbarNames)["Hide"]);
        using var invalid = new MemoryStream(Pdb(infoAge: 3, dbiAge: 2));
        Assert.Throws<InvalidDataException>(() => NativePdbSymbols.Resolve(invalid, Module(), NativePdbSymbols.TaskbarNames));
    }

    [Theory]
    [InlineData(20)]
    [InlineData(512)]
    [InlineData(2000)]
    public void RejectsTruncatedPdb(int bytes)
    {
        using var stream = new MemoryStream(Pdb()[..bytes]);
        Assert.Throws<InvalidDataException>(() => NativePdbSymbols.Resolve(stream, Module(), NativePdbSymbols.TaskbarNames));
    }

    [Fact]
    public void RejectsBlockOutsideFile()
    {
        var bytes = Pdb(); BitConverter.GetBytes(uint.MaxValue).CopyTo(bytes, 512);
        using var stream = new MemoryStream(bytes);
        Assert.Throws<InvalidDataException>(() => NativePdbSymbols.Resolve(stream, Module(), NativePdbSymbols.TaskbarNames));
    }

    private sealed class Handler(byte[] bytes, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        internal int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent(bytes), RequestMessage = request });
        }
    }

    [Fact]
    public async Task DownloadsOnceCachesExactModulesAndWorksOffline()
    {
        string directory = Path.Combine(Path.GetTempPath(), "rtb-symbol-test-" + Guid.NewGuid());
        var handler = new Handler(Pdb()); using var client = new HttpClient(handler);
        try
        {
            await NativeTaskbarSymbols.ResolveModuleAsync(Module(), directory, client, TestContext.Current.CancellationToken);
            await NativeTaskbarSymbols.ResolveModuleAsync(Module("Taskbar.View.dll"), directory, client, TestContext.Current.CancellationToken);
            await NativeTaskbarSymbols.ResolveModuleAsync(Module(), directory, client, TestContext.Current.CancellationToken);
            Assert.Equal(2, handler.Requests);
            string json = NativeTaskbarSymbols.ReadCached(Module(), Module("Taskbar.View.dll"), directory);
            Assert.Equal(0x1330ul, NativeTaskbarPin.ValidateDescriptor(json, new(123, 0x4000), new(123, 0x4000)).ShouldExpand);
            Assert.Null(NativeTaskbarSymbols.ReadCached(Module() with { Sha256 = "changed" }, Module("Taskbar.View.dll"), directory));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp*"));
            Assert.Equal(2, Directory.GetFiles(directory).Length);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrMalformedSymbolsLeaveNoCache(bool missing)
    {
        string directory = Path.Combine(Path.GetTempPath(), "rtb-symbol-test-" + Guid.NewGuid());
        using var client = new HttpClient(new Handler([1, 2, 3], missing ? HttpStatusCode.NotFound : HttpStatusCode.OK));
        try
        {
            await Assert.ThrowsAnyAsync<Exception>(() => NativeTaskbarSymbols.ResolveModuleAsync(Module(), directory, client, TestContext.Current.CancellationToken));
            Assert.Empty(Directory.GetFiles(directory));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task EnforcesDownloadSizeEvenWithoutContentLength()
    {
        using var input = new MemoryStream(new byte[9]);
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<InvalidDataException>(() => NativeTaskbarSymbols.CopyLimitedAsync(input, output, 8, TestContext.Current.CancellationToken));
        Assert.Equal(0, output.Length);
    }


    private sealed class OfflineHandler : HttpMessageHandler
    {
        internal int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            Requests++;
            throw new HttpRequestException("Simulated offline connection.");
        }
    }

    [Fact]
    public async Task OfflineStartupUsesCacheButWindowsUpdateCannotReuseOldAddresses()
    {
        string directory = Path.Combine(Path.GetTempPath(), "rtb-symbol-test-" + Guid.NewGuid());
        using var online = new HttpClient(new Handler(Pdb()));
        var handler = new OfflineHandler(); using var offline = new HttpClient(handler);
        var cancel = TestContext.Current.CancellationToken;
        try
        {
            await NativeTaskbarSymbols.ResolveModuleAsync(Module(), directory, online, cancel);
            await NativeTaskbarSymbols.ResolveModuleAsync(Module("Taskbar.View.dll"), directory, online, cancel);
            await NativeTaskbarSymbols.ResolveModuleAsync(Module(), directory, offline, cancel);
            await NativeTaskbarSymbols.ResolveModuleAsync(Module("Taskbar.View.dll"), directory, offline, cancel);
            Assert.Equal(0, handler.Requests);
            var updated = Module() with { Sha256 = "NEW-WINDOWS-DLL" };
            Assert.Null(NativeTaskbarSymbols.ReadCached(updated, Module("Taskbar.View.dll"), directory));
            await Assert.ThrowsAsync<HttpRequestException>(() => NativeTaskbarSymbols.ResolveModuleAsync(updated, directory, offline, cancel));
            Assert.Equal(1, handler.Requests);
            Assert.Empty(Directory.GetFiles(directory, "*.tmp*"));
            Assert.NotNull(NativeTaskbarSymbols.ReadCached(Module(), Module("Taskbar.View.dll"), directory));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("shape")]
    [InlineData("address")]
    [InlineData("identity")]
    [InlineData("signature")]
    public async Task CorruptCacheIsRejectedAndRepaired(string corruption)
    {
        string directory = Path.Combine(Path.GetTempPath(), "rtb-symbol-test-" + Guid.NewGuid());
        var handler = new Handler(Pdb()); using var client = new HttpClient(handler);
        var cancel = TestContext.Current.CancellationToken;
        try
        {
            await NativeTaskbarSymbols.ResolveModuleAsync(Module(), directory, client, cancel);
            await NativeTaskbarSymbols.ResolveModuleAsync(Module("Taskbar.View.dll"), directory, client, cancel);
            string path = Path.Combine(directory, "AAA.json");
            var item = JObject.Parse(File.ReadAllText(path));
            switch (corruption)
            {
                case "shape": item["Symbols"] = 7; break;
                case "address": item["Symbols"]!["Hide"]!["Rva"] = 0x3500; break;
                case "identity": item["PdbKey"] = "OTHER-GUID"; break;
                case "signature": item["Symbols"]!["Hide"]!["Name"] = "?_Hide@TrayUI@@QEAAX_N@Z"; break;
            }
            File.WriteAllText(path, item.ToString());
            Assert.Null(NativeTaskbarSymbols.ReadCached(Module(), Module("Taskbar.View.dll"), directory));
            await NativeTaskbarSymbols.ResolveModuleAsync(Module(), directory, client, cancel);
            Assert.NotNull(NativeTaskbarSymbols.ReadCached(Module(), Module("Taskbar.View.dll"), directory));
            Assert.Equal(3, handler.Requests);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void SymbolUrlUsesExactPdbGuidAge()
    {
        Assert.Equal("https://msdl.microsoft.com/download/symbols/Taskbar.pdb/00112233445566778899AABBCCDDEEFF1/Taskbar.pdb",
            NativeTaskbarSymbols.SymbolUri(Module()).AbsoluteUri);
    }
}
