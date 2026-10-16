using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Text;

namespace RoundedTB;

// Reads only the MSF/PDB identity, DBI and public symbol records we need.
// Format references: llvm.org/docs/PDB/{MsfFile,PdbStream,DbiStream}.html.
internal static class NativePdbSymbols
{
    internal sealed record Module(string Name, NativeTaskbarPin.ImageIdentity Identity, Guid Guid,
        int Age, string PdbName, string Sha256, SectionHeader[] Sections)
    {
        internal string SymbolKey => Guid.ToString("N").ToUpperInvariant() + Age.ToString("X");
    }

    internal static readonly IReadOnlyDictionary<string, string> TaskbarNames = new Dictionary<string, string>
    {
        ["Hide"] = "?_Hide@TrayUI@@QEAAXXZ",
        ["SecondaryAutoHide"] = "?_AutoHide@CSecondaryTray@@AEAAX_N@Z"
    };
    internal static readonly IReadOnlyDictionary<string, string> ViewNames = new Dictionary<string, string>
    {
        ["ShouldExpand"] = "?ShouldTaskbarBeExpanded@ViewCoordinator@implementation@Taskbar@winrt@@QEAA_N_K_N@Z"
    };

    internal static Module ReadModule(string path)
    {
        using var stream = File.OpenRead(path);
        var identity = NativeTaskbarPin.ReadIdentity(stream);
        stream.Position = 0;
        string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
        stream.Position = 0;
        using var pe = new PEReader(stream);
        var entries = pe.ReadDebugDirectory().Where(entry => entry.Type == DebugDirectoryEntryType.CodeView).ToArray();
        if (entries.Length != 1) throw new InvalidDataException("Expected exactly one CodeView identity: " + path);
        var codeview = pe.ReadCodeViewDebugDirectoryData(entries[0]);
        string name = codeview.Path.Replace('\\', '/').Split('/').Last();
        if (codeview.Guid == Guid.Empty || codeview.Age < 1 || name.Length > 100
            || !name.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)
            || name.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '.' && c != '_' && c != '-'))
            throw new InvalidDataException("Invalid symbol server identity.");
        return new Module(Path.GetFileName(path), identity, codeview.Guid, codeview.Age, name, hash, pe.PEHeaders.SectionHeaders.ToArray());
    }

    internal static Dictionary<string, ulong> Resolve(Stream file, Module module, IReadOnlyDictionary<string, string> wanted)
    {
        var pdb = new Msf(file);
        var info = pdb.Stream(1);
        if (info.Length < 28 || U32(info, 8) < module.Age || new Guid(info.AsSpan(12, 16)) != module.Guid)
            throw new InvalidDataException("PDB GUID/age does not match the Windows module.");
        var dbi = pdb.Stream(3);
        if (dbi.Length < 64 || U32(dbi, 0) != uint.MaxValue || U16(dbi, 58) != 0x8664)
            throw new InvalidDataException("Unsupported PDB DBI header.");
        // Microsoft OpenValidate4 permits a newer PDB info age (stripped public
        // PDBs commonly have one), but requires the DBI age to match the image.
        // github.com/microsoft/microsoft-pdb/blob/master/PDB/dbi/pdb.cpp
        if (U32(dbi, 8) != module.Age) throw new InvalidDataException("PDB DBI age does not match the Windows module.");
        // OMAP changes section-relative addresses. Refuse it rather than applying
        // an address from the original, pre-optimization image to the live image.
        long optional = 64;
        foreach (int offset in new[] { 24, 28, 32, 36, 40, 52 }) optional += U32(dbi, offset);
        uint optionalSize = U32(dbi, 48);
        if (optional > dbi.Length || optionalSize > dbi.Length - optional || (optionalSize & 1) != 0)
            throw new InvalidDataException("Invalid DBI substream lengths.");
        foreach (int index in new[] { 3, 4 })
            if (optionalSize >= index * 2 + 2 && U16(dbi, checked((int)optional + index * 2)) != ushort.MaxValue)
                throw new InvalidDataException("OMAP symbol addresses are unsupported.");
        var records = pdb.Stream(U16(dbi, 20));
        var found = new Dictionary<string, ulong>();
        for (int offset = 0; offset < records.Length;)
        {
            if (records.Length - offset < 4) throw new InvalidDataException("Truncated symbol record.");
            int length = U16(records, offset) + 2;
            if (length < 4 || length > records.Length - offset) throw new InvalidDataException("Invalid symbol record length.");
            if (U16(records, offset + 2) == 0x110e) // S_PUB32: flags, offset, section, name.
            {
                if (length < 15) throw new InvalidDataException("Truncated public symbol.");
                int end = Array.IndexOf(records, (byte)0, offset + 14, length - 14);
                if (end < 0) throw new InvalidDataException("Unterminated public symbol.");
                string name = Encoding.UTF8.GetString(records, offset + 14, end - offset - 14);
                foreach (var match in wanted)
                {
                    // Full decorated names also specify the calling convention,
                    // return type and parameters. Never match just a substring.
                    if (name != match.Value) continue;
                    uint flags = U32(records, offset + 4), displacement = U32(records, offset + 8);
                    ushort section = U16(records, offset + 12);
                    if ((flags & 2) == 0 || section == 0 || section > module.Sections.Length)
                        throw new InvalidDataException("Expected a function in an image section.");
                    var header = module.Sections[section - 1];
                    ulong rva = (ulong)(uint)header.VirtualAddress + displacement;
                    if ((header.SectionCharacteristics & SectionCharacteristics.MemExecute) == 0
                        || displacement >= (uint)header.VirtualSize || rva == 0 || rva >= module.Identity.ImageSize)
                        throw new InvalidDataException("Symbol address outside executable image section.");
                    if (found.TryGetValue(match.Key, out ulong previous) && previous != rva)
                        throw new InvalidDataException("Ambiguous symbol: " + match.Key);
                    found[match.Key] = rva;
                }
            }
            offset += length;
        }
        foreach (string key in wanted.Keys)
            if (!found.ContainsKey(key)) throw new InvalidDataException("Required function signature unavailable: " + key);
        return found;
    }

    private static ushort U16(byte[] bytes, int offset) => System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2));
    private static uint U32(byte[] bytes, int offset) => System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));

    private sealed class Msf
    {
        private readonly Stream file;
        private readonly int blockSize;
        private readonly int[] sizes;
        private readonly uint[][] blocks;

        internal Msf(Stream file)
        {
            this.file = file;
            if (!file.CanSeek || file.Length < 56 || file.Length > NativeTaskbarSymbols.MaxPdbBytes)
                throw new InvalidDataException("Invalid PDB size.");
            byte[] header = Read(0, 56);
            if (!header.AsSpan(0, 32).SequenceEqual("Microsoft C/C++ MSF 7.00\r\n\u001aDS\0\0\0"u8))
                throw new InvalidDataException("Unsupported PDB container.");
            uint size = U32(header, 32), count = U32(header, 40), directorySize = U32(header, 44);
            if (size < 512 || size > 65536 || (size & (size - 1)) != 0 || (ulong)size * count != (ulong)file.Length
                || directorySize < 4 || directorySize > file.Length)
                throw new InvalidDataException("Invalid MSF block layout.");
            blockSize = (int)size;
            int directoryBlocks = checked((int)((directorySize + size - 1) / size));
            if ((long)directoryBlocks * 4 > size) throw new InvalidDataException("Unsupported MSF directory map.");
            byte[] map = Read((long)U32(header, 52) * blockSize, directoryBlocks * 4);
            var directoryIds = Enumerable.Range(0, directoryBlocks).Select(i => U32(map, i * 4)).ToArray();
            byte[] directory = Assemble(checked((int)directorySize), directoryIds);
            uint streams = U32(directory, 0);
            if (streams < 4 || streams > 65536 || 4L + streams * 4L > directory.Length)
                throw new InvalidDataException("Invalid MSF stream directory.");
            sizes = new int[streams]; blocks = new uint[streams][];
            int position = checked(4 + (int)streams * 4);
            for (int i = 0; i < streams; i++)
            {
                uint length = U32(directory, 4 + i * 4);
                if (length == uint.MaxValue) { sizes[i] = -1; blocks[i] = []; continue; }
                if (length > file.Length) throw new InvalidDataException("Invalid MSF stream size.");
                sizes[i] = (int)length;
                int n = (sizes[i] + blockSize - 1) / blockSize;
                if ((long)position + n * 4L > directory.Length) throw new InvalidDataException("Truncated MSF stream map.");
                blocks[i] = new uint[n];
                for (int j = 0; j < n; j++) { blocks[i][j] = U32(directory, position); position += 4; }
            }
        }

        internal byte[] Stream(int index)
        {
            if (index < 0 || index >= sizes.Length || sizes[index] < 0) throw new InvalidDataException("Missing PDB stream.");
            return Assemble(sizes[index], blocks[index]);
        }
        private byte[] Assemble(int length, uint[] ids)
        {
            var result = new byte[length];
            int position = 0;
            foreach (uint id in ids)
            {
                int bytes = Math.Min(blockSize, length - position);
                Read((long)id * blockSize, bytes).CopyTo(result, position); position += bytes;
            }
            return result;
        }
        private byte[] Read(long offset, int count)
        {
            if (offset < 0 || count < 0 || offset > file.Length - count) throw new InvalidDataException("MSF block outside file.");
            var result = new byte[count]; file.Position = offset; file.ReadExactly(result); return result;
        }
    }
}
