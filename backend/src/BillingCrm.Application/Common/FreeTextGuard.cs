namespace BillingCrm.Application.Common;

/// <summary>
/// Refuses a free-text field that contains a Unicode bidirectional control character or another C0/C1 control
/// character (spec 012, FR-009, research R7). Tab, line feed and carriage return are allowed (ordinary text
/// formatting); every other character below U+0020 or in U+0080-U+009F is refused, along with the specific bidi
/// override/embedding/isolate characters, because none of them can ever be legitimate in a name, address, note
/// or notes field and a bidi override is the one way plain text can visually disguise itself (spoofing a file
/// extension or a status word) without ever becoming markup. Pure: no I/O, no state.
/// </summary>
public static class FreeTextGuard
{
    private static readonly HashSet<char> BidiControls =
    [
        '‎', '‏', // LRM, RLM
        '‪', '‫', '‬', '‭', '‮', // LRE, RLE, PDF, LRO, RLO
        '⁦', '⁧', '⁨', '⁩', // LRI, RLI, FSI, PDI
    ];

    public static bool Refuses(string value)
    {
        foreach (var c in value)
        {
            if (BidiControls.Contains(c))
            {
                return true;
            }

            if (c is '\t' or '\n' or '\r')
            {
                continue;
            }

            if (c <= '\u001F' || (c >= '\u007F' && c <= '\u009F'))
            {
                return true;
            }
        }

        return false;
    }
}
