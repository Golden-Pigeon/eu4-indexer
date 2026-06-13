using System.Text;
using Eu4Indexer.Core;

namespace Eu4Indexer.Tests;

/// <summary>
/// Verifies that Localisation.decodeSpecialEscape inverts the EU4 "special
/// escape" encoding used by non-Latin localisation mods. SpecialEscape below
/// is a faithful C# port of the reference encoder
/// (https://gist.github.com/bruceCzK/96ad6e054111f929ed67291552d36334) used
/// only to generate round-trip fixtures.
/// </summary>
public class LocalisationTests
{
    [Theory]
    [InlineData(0x4E2D)] // 中 — neither byte reserved, no offset
    [InlineData(0x4E20)] // low byte 0x20 (space) is reserved -> low offset
    [InlineData(0x4E80)] // low byte 0x80 reserved -> offset + cp1252 remap
    [InlineData(0x5B57)] // high byte 0x5B '[' reserved -> high offset
    [InlineData(0x7B2C)] // high byte 0x7B '{' reserved -> high offset
    [InlineData(0x6587)] // 文 — plain
    [InlineData(0x00C9)] // É — just above 0xFF, exercises high byte 0x00 path
    public void Decode_RoundTripsEncodedCodePoint(int codePoint)
    {
        var ch = char.ConvertFromUtf32(codePoint);
        var encoded = SpecialEscape(ch);

        var decoded = Localisation.decodeSpecialEscape(encoded);

        Assert.Equal(ch, decoded);
    }

    [Fact]
    public void Decode_KnownVector_ForChineseZhong()
    {
        // 中 (U+4E2D) encodes to marker 0x10 then '-' (0x2D) then 'N' (0x4E).
        var encoded = "-N";
        Assert.Equal("中", Localisation.decodeSpecialEscape(encoded));
    }

    [Fact]
    public void Decode_MixedSentence_RoundTrips()
    {
        var original = "稳定度 +1 Stability";
        var sb = new StringBuilder();
        foreach (var rune in original.EnumerateRunes())
            sb.Append(SpecialEscape(rune.ToString()));

        Assert.Equal(original, Localisation.decodeSpecialEscape(sb.ToString()));
    }

    [Theory]
    [InlineData("Stability +1")]
    [InlineData("Café déjà vu")] // Latin-1 letters stay below 0x100, pass through
    [InlineData("")]
    [InlineData("no markers here")]
    public void Decode_PlainText_PassesThroughUnchanged(string text)
    {
        Assert.Equal(text, Localisation.decodeSpecialEscape(text));
    }

    [Fact]
    public void Decode_LoneMarkerWithoutFollowingBytes_IsPreserved()
    {
        // A trailing marker with no two code points after it is left as-is.
        var input = "abc";
        Assert.Equal(input, Localisation.decodeSpecialEscape(input));
    }

    // -- markup stripping (§ colour codes, £ icon codes) -----------------------

    [Theory]
    [InlineData("§Yyellow§! and §Rred§!", "yellow and red")]
    // the bug case: every character wrapped in its own colour span
    [InlineData("§Aa§!§Bb§!§Cc§!", "abc")]
    [InlineData("trailing marker §", "trailing marker ")]
    [InlineData("Stability +1", "Stability +1")]
    [InlineData("", "")]
    public void StripMarkup_RemovesColorCodes(string input, string expected)
    {
        Assert.Equal(expected, Localisation.stripMarkup(input));
    }

    [Fact]
    public void StripMarkup_RemovesIconCodes()
    {
        Assert.Equal("cost  now", Localisation.stripMarkup("cost £gold£ now"));
    }

    // -- reference encoder (toUtf8 = true, newVersion = true / EU4 >= 1.26) ----

    private static readonly int[] InternalChars =
    {
        0x00, 0x0A, 0x0D, 0x20, 0x22, 0x24, 0x40, 0x5B, 0x5C, 0x7B, 0x7D,
        0x7E, 0x80, 0xA3, 0xA4, 0xA7, 0xBD, 0x3B, 0x5D, 0x5F, 0x3D, 0x23,
        0x2F // added when toUtf8
    };

    private static readonly Dictionary<int, int> Cp1252ToUtf8 = new()
    {
        [0x80] = 0x20AC, [0x82] = 0x201A, [0x83] = 0x0192, [0x84] = 0x201E,
        [0x85] = 0x2026, [0x86] = 0x2020, [0x87] = 0x2021, [0x88] = 0x02C6,
        [0x89] = 0x2030, [0x8A] = 0x0160, [0x8B] = 0x2039, [0x8C] = 0x0152,
        [0x8E] = 0x017D, [0x91] = 0x2018, [0x92] = 0x2019, [0x93] = 0x201C,
        [0x94] = 0x201D, [0x95] = 0x2022, [0x96] = 0x2013, [0x97] = 0x2014,
        [0x98] = 0x02DC, [0x99] = 0x2122, [0x9A] = 0x0161, [0x9B] = 0x203A,
        [0x9C] = 0x0153, [0x9E] = 0x017E, [0x9F] = 0x0178
    };

    private static string SpecialEscape(string ch)
    {
        var cp = char.ConvertToUtf32(ch, 0);
        if (cp < 256) return ch;

        const int lowByteOffset = 14; // newVersion
        const int highByteOffset = -9;

        var low = cp & 0xFF;
        var high = (cp >> 8) & 0xFF;

        var escapeChr = 0x10;
        if (InternalChars.Contains(high)) escapeChr += 2;
        if (InternalChars.Contains(low)) escapeChr += 1;

        if (escapeChr == 0x11 || escapeChr == 0x13) low += lowByteOffset;
        if (escapeChr == 0x12 || escapeChr == 0x13) high += highByteOffset;

        low = Cp1252ToUtf8.TryGetValue(low, out var lu) ? lu : low;
        high = Cp1252ToUtf8.TryGetValue(high, out var hu) ? hu : high;

        return char.ConvertFromUtf32(escapeChr)
             + char.ConvertFromUtf32(low)
             + char.ConvertFromUtf32(high);
    }
}
