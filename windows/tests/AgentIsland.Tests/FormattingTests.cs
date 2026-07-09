using AgentIsland.Core;

namespace AgentIsland.Tests;

/// Pins CompactTokens, including the rounding-boundary rollover that used to
/// print "1000k"/"1000M".
public static class FormattingTests
{
    public static void RunAll()
    {
        var cases = new (long Tokens, string Expected)[]
        {
            (0, "0"),
            (941, "941"),
            (999, "999"),
            (1_000, "1k"),
            (12_400, "12.4k"),
            (999_499, "999k"),
            // Boundary: 999_500 would print "1000k" before the fix; it must
            // roll over to the next unit instead ("1M", since >=100 rounds to
            // whole and 0.9995 renders as "1").
            (999_500, "1M"),
            (999_999, "1M"),
            (1_000_000, "1M"),
            (211_200_000, "211M"),
            (999_499_999, "999M"),
            (999_500_000, "1B"),
            (2_170_000_000, "2.2B"),
        };
        foreach (var (tokens, expected) in cases)
        {
            var actual = Formatting.CompactTokens(tokens);
            if (actual != expected)
            {
                throw new Exception($"CompactTokens({tokens}) = \"{actual}\", expected \"{expected}\"");
            }
            Console.WriteLine($"PASS CompactTokens({tokens}) = {actual}");
        }
        Console.WriteLine("FormattingTests GREEN");
    }
}
