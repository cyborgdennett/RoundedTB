using System;
using System.IO;
using Newtonsoft.Json.Linq;
using Xunit;

namespace RoundedTB.Tests;

public class NativeTaskbarPinTests
{
    private static MemoryStream Image(ushort machine = 0x8664)
    {
        var stream = new MemoryStream(new byte[512]);
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write((ushort)0x5A4D);
        stream.Position = 0x3C; writer.Write(128u);
        stream.Position = 128; writer.Write(0x00004550u); writer.Write(machine); writer.Write((ushort)1); writer.Write(123u);
        stream.Position = 148; writer.Write((ushort)240);
        stream.Position = 152; writer.Write((ushort)0x20B);
        stream.Position = 208; writer.Write(4096u);
        return stream;
    }

    [Fact]
    public void ReadsPeIdentityRatherThanFileLength()
    {
        using var image = Image();
        Assert.Equal(new NativeTaskbarPin.ImageIdentity(123, 4096), NativeTaskbarPin.ReadIdentity(image));
        Assert.True(image.CanRead);
    }

    [Fact]
    public void RejectsWrongArchitectureBeforeUsingSymbols()
    {
        using var image = Image(0xAA64);
        Assert.Throws<InvalidDataException>(() => NativeTaskbarPin.ReadIdentity(image));
    }

    [Theory]
    [InlineData(32)]
    [InlineData(150)]
    [InlineData(300)]
    public void RejectsTruncatedPeHeaders(int length)
    {
        using var image = Image();
        using var truncated = new MemoryStream(image.ToArray()[..length]);
        Assert.Throws<InvalidDataException>(() => NativeTaskbarPin.ReadIdentity(truncated));
    }

    private static JObject Descriptor() => JObject.Parse("""
        {"Version":1,"Modules":[
          {"Name":"taskbar.dll","Timestamp":123,"ImageSize":4096,"Symbols":{"Hide":{"Rva":512}}},
          {"Name":"Taskbar.View.dll","Timestamp":456,"ImageSize":8192,"Symbols":{"ShouldExpand":{"Rva":1024}}}
        ]}
        """);

    [Fact]
    public void ValidatesBothExactModuleIdentitiesAndOptionalSymbols()
    {
        var config = NativeTaskbarPin.ValidateDescriptor(Descriptor().ToString(), new(123, 4096), new(456, 8192));
        Assert.Equal(512ul, config.Hide);
        Assert.Equal(1024ul, config.ShouldExpand);
        Assert.Equal(0ul, config.SecondaryHide);
        Assert.Equal(0ul, config.UpdateExpanded);
        Assert.Throws<InvalidDataException>(() => NativeTaskbarPin.ValidateDescriptor(Descriptor().ToString(), new(123, 4096), new(457, 8192)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4096)]
    [InlineData(99999)]
    public void RejectsMissingOrOutOfBoundsHookAddress(int rva)
    {
        var descriptor = Descriptor();
        descriptor["Modules"]![0]!["Symbols"]!["Hide"]!["Rva"] = rva;
        Assert.Throws<InvalidDataException>(() => NativeTaskbarPin.ValidateDescriptor(descriptor.ToString(), new(123, 4096), new(456, 8192)));
    }

    [Fact]
    public void RejectsAmbiguousModuleEntries()
    {
        var descriptor = Descriptor();
        ((JArray)descriptor["Modules"]!).Add(descriptor["Modules"]![0]!.DeepClone());
        Assert.Throws<InvalidDataException>(() => NativeTaskbarPin.ValidateDescriptor(descriptor.ToString(), new(123, 4096), new(456, 8192)));
    }
}
