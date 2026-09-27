using BillingCrm.Application.Common;

namespace BillingCrm.Application.Tests;

/// <summary>Spec 012, User Story 3: the shared free-text guard refuses bidi/control characters, nothing else.</summary>
public class FreeTextGuardTests
{
    [Theory]
    [InlineData('‎')] // LRM
    [InlineData('‏')] // RLM
    [InlineData('‪')] // LRE
    [InlineData('‫')] // RLE
    [InlineData('‬')] // PDF
    [InlineData('‭')] // LRO
    [InlineData('‮')] // RLO
    [InlineData('⁦')] // LRI
    [InlineData('⁧')] // RLI
    [InlineData('⁨')] // FSI
    [InlineData('⁩')] // PDI
    [InlineData('\u0007')] // BEL, an ordinary C0 control character
    [InlineData('\u009F')] // APC, a C1 control character
    public void A_bidi_or_control_character_anywhere_in_the_text_is_refused(char forbidden)
    {
        Assert.True(FreeTextGuard.Refuses($"Acme{forbidden}Trading"));
    }

    [Theory]
    [InlineData('\t')]
    [InlineData('\n')]
    [InlineData('\r')]
    public void Tab_and_line_breaks_are_allowed(char allowed)
    {
        Assert.False(FreeTextGuard.Refuses($"Line one{allowed}line two"));
    }

    [Fact]
    public void Ordinary_arabic_and_english_text_is_allowed()
    {
        Assert.False(FreeTextGuard.Refuses("Acme Trading LLC - شركة أكمي التجارية"));
    }

    [Fact]
    public void Emoji_is_allowed()
    {
        Assert.False(FreeTextGuard.Refuses("Invoice notes 🙂👍"));
    }

    [Fact]
    public void A_long_ordinary_string_with_none_of_the_forbidden_characters_is_allowed()
    {
        Assert.False(FreeTextGuard.Refuses(new string('a', 1000)));
    }
}
