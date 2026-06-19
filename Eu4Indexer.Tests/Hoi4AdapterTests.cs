using Eu4Indexer.Core;
using Microsoft.FSharp.Core;

namespace Eu4Indexer.Tests;

public class Hoi4AdapterTests
{
    [Fact]
    public void Hoi4_GameId_IsHoi4()
    {
        Assert.Equal("hoi4", GameAdapterModule.hoi4.GameId);
    }

    [Fact]
    public void Hoi4_SteamAppId()
    {
        Assert.Equal("394360", GameAdapterModule.hoi4.SteamAppId);
    }

    [Fact]
    public void Hoi4_SteamGameDir()
    {
        Assert.Equal("Hearts of Iron IV", GameAdapterModule.hoi4.SteamGameDir);
    }

    [Fact]
    public void Hoi4_Languages_IncludesSimpChinese()
    {
        Assert.Contains("simp_chinese", GameAdapterModule.hoi4.Languages);
    }

    [Theory]
    [InlineData("localisation/english/foo.yml", "english")]
    [InlineData("localisation/simp_chinese/events.yml", "simp_chinese")]
    [InlineData("localisation/french/ui.yml", "french")]
    public void LocFileLanguage_DetectsLanguageFromParentDirectory(string path, string expected)
    {
        var result = GameAdapterModule.hoi4.LocFileLanguage.Invoke(path);
        Assert.Equal(FSharpOption<string>.Some(expected), result);
    }

    [Theory]
    [InlineData("localisation/zzz/foo.yml")]
    [InlineData("events/unrelated.txt")]
    [InlineData("")]
    public void LocFileLanguage_ReturnsNone_ForUnknownOrMissingDir(string path)
    {
        var result = GameAdapterModule.hoi4.LocFileLanguage.Invoke(path);
        Assert.Equal(FSharpOption<string>.None, result);
    }

    [Fact]
    public void RefKeyRules_ContainsHoi4SpecificKeys()
    {
        var rules = GameAdapterModule.hoi4.RefKeyRules;
        Assert.True(rules.ContainsKey("country_event"));
        Assert.True(rules.ContainsKey("has_country_flag"));
        Assert.True(rules.ContainsKey("set_country_flag"));
        Assert.True(rules.ContainsKey("clr_country_flag"));
        Assert.True(rules.ContainsKey("check_variable"));
        Assert.True(rules.ContainsKey("set_variable"));
        Assert.True(rules.ContainsKey("add_ideas"));
        Assert.True(rules.ContainsKey("has_idea"));
    }

    [Fact]
    public void CoreFolders_ContainsFocusTreeAndIdeas()
    {
        var folders = GameAdapterModule.hoi4.CoreFolders;
        Assert.True(folders.ContainsKey("common/national_focus"));
        Assert.True(folders.ContainsKey("common/ideas"));
        Assert.True(folders.ContainsKey("events"));
        Assert.True(folders.ContainsKey("common/decisions"));
    }

    [Fact]
    public void AllAdapters_IncludesHoi4()
    {
        var all = GameAdapterModule.allAdapters;
        Assert.Contains(all, a => a.GameId == "hoi4");
        Assert.Contains(all, a => a.GameId == "eu4");
    }

    [Fact]
    public void ById_ReturnsCorrectAdapter()
    {
        Assert.Equal(FSharpOption<GameAdapter>.Some(GameAdapterModule.hoi4), GameAdapterModule.byId("hoi4"));
        Assert.Equal(FSharpOption<GameAdapter>.Some(GameAdapterModule.eu4), GameAdapterModule.byId("eu4"));
        Assert.Equal(FSharpOption<GameAdapter>.None, GameAdapterModule.byId("unknown"));
    }

    [Fact]
    public void ById_IsCaseInsensitive()
    {
        Assert.Equal(FSharpOption<GameAdapter>.Some(GameAdapterModule.hoi4), GameAdapterModule.byId("HOI4"));
        Assert.Equal(FSharpOption<GameAdapter>.Some(GameAdapterModule.eu4), GameAdapterModule.byId("EU4"));
    }
}
