using Eu4Indexer.Core;
using Microsoft.FSharp.Core;

namespace Eu4Indexer.Tests;

public class ModDescriptorTests
{
    [Fact]
    public void ParseText_FullDescriptor_ExtractsAllFields()
    {
        const string text = """
            version="1.0.6"
            picture="thumbnail.png"
            tags={
                "New Nations"
                "Expansion"
            }
            dependencies={
                "Chinese Language Mod for 1.37"
            }
            name="Example Mod"
            replace_path="gfx/loadingscreens"
            replace_path="common/disasters"
            supported_version="v1.37.*"
            remote_file_id="3733682302"
            """;

        var result = ModDescriptor.parseText("descriptor.mod", text);
        Assert.True(result.IsOk, result.IsError ? result.ErrorValue : "");
        var info = result.ResultValue;

        Assert.Equal("Example Mod", info.Name);
        Assert.Equal("1.0.6", OptionModule.ToObj(info.Version));
        Assert.Equal("v1.37.*", OptionModule.ToObj(info.SupportedVersion));
        Assert.Equal("3733682302", OptionModule.ToObj(info.RemoteFileId));
        Assert.Equal(2, info.Tags.Length);
        Assert.Single(info.Dependencies);
        // replace_path is a repeatable leaf, both must be captured
        Assert.Equal(2, info.ReplacePaths.Length);
        Assert.Contains("gfx/loadingscreens", info.ReplacePaths);
        Assert.Contains("common/disasters", info.ReplacePaths);
    }

    [Fact]
    public void ParseText_MinimalDescriptor_UsesDefaults()
    {
        var result = ModDescriptor.parseText("descriptor.mod", "name=\"Tiny\"\n");
        Assert.True(result.IsOk);
        var info = result.ResultValue;
        Assert.Equal("Tiny", info.Name);
        Assert.True(FSharpOption<string>.get_IsNone(info.Version));
        Assert.Empty(info.ReplacePaths);
    }
}
