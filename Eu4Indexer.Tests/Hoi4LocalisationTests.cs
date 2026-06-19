using Eu4Indexer.Core;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;

namespace Eu4Indexer.Tests;

public class Hoi4LocalisationTests
{
    [Fact]
    public void Hoi4_LocFileLanguage_ReturnsEnglish_ForEnglishSubdirectory()
    {
        var adapter = GameAdapterModule.hoi4;
        var result = adapter.LocFileLanguage.Invoke("localisation/english/fixture_l_english.yml");
        Assert.Equal(FSharpOption<string>.Some("english"), result);
    }

    [Fact]
    public void Hoi4_LocFileLanguage_ReturnsSimpChinese_ForSimpChineseSubdirectory()
    {
        var adapter = GameAdapterModule.hoi4;
        var result = adapter.LocFileLanguage.Invoke("localisation/simp_chinese/fixture_l_simp_chinese.yml");
        Assert.Equal(FSharpOption<string>.Some("simp_chinese"), result);
    }

    [Fact]
    public void Hoi4_LocFileLanguage_ReturnsNone_ForUnknownDirectory()
    {
        var adapter = GameAdapterModule.hoi4;
        var result = adapter.LocFileLanguage.Invoke("localisation/martian/foo.yml");
        Assert.Equal(FSharpOption<string>.None, result);
    }

    [Fact]
    public void Hoi4_LocFileLanguage_FindsLanguage_InNestedSubdirectory()
    {
        var adapter = GameAdapterModule.hoi4;
        // E.g. localisation/simp_chinese/kr_country_specific/ger - germany.yml
        var result = adapter.LocFileLanguage.Invoke(
            "localisation/simp_chinese/kr_country_specific/ger - germany l_simp_chinese.yml");
        Assert.Equal(FSharpOption<string>.Some("simp_chinese"), result);
    }

    [Fact]
    public void Hoi4_LocFileLanguage_FindsLanguage_DeeplyNested()
    {
        var adapter = GameAdapterModule.hoi4;
        var result = adapter.LocFileLanguage.Invoke(
            "localisation/english/replace/some_mod/events.yml");
        Assert.Equal(FSharpOption<string>.Some("english"), result);
    }

    [Fact]
    public void Hoi4_LocFileLanguage_ReturnsNone_ForNonLocalisationPath()
    {
        var adapter = GameAdapterModule.hoi4;
        var result = adapter.LocFileLanguage.Invoke("events/fixture_events.txt");
        Assert.Equal(FSharpOption<string>.None, result);
    }

    [Fact]
    public void Eu4_LocFileLanguage_StillUsesFileNameSuffix()
    {
        var adapter = GameAdapterModule.eu4;
        var result = adapter.LocFileLanguage.Invoke("fixture_l_english.yml");
        Assert.Equal(FSharpOption<string>.Some("english"), result);

        var result2 = adapter.LocFileLanguage.Invoke("some_file_l_german.yml");
        Assert.Equal(FSharpOption<string>.Some("german"), result2);
    }

    [Fact]
    public void Eu4_LocFileLanguage_ReturnsNone_ForNonMatchingFile()
    {
        var adapter = GameAdapterModule.eu4;
        var result = adapter.LocFileLanguage.Invoke("some_script.txt");
        Assert.Equal(FSharpOption<string>.None, result);
    }
}
