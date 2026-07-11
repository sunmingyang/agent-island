using AgentIsland.Update;

namespace AgentIsland.Tests;

/// Pins the update-check version parsing: tags with and without the "v"
/// prefix parse, junk never reads as an update, and ordering is numeric
/// (1.10.0 beats 1.9.9).
public static class UpdateCheckerTests
{
    public static void RunAll()
    {
        Expect(UpdateChecker.ParseTag("v1.5.4") == new Version(1, 5, 4), "v-prefixed tag parses");
        Expect(UpdateChecker.ParseTag("1.5.4") == new Version(1, 5, 4), "bare tag parses");
        Expect(UpdateChecker.ParseTag("windows-test") is null, "junk tag is ignored");
        Expect(UpdateChecker.ParseTag("v1.10.0")! > new Version(1, 9, 9), "ordering is numeric, not lexical");
        Console.WriteLine("UpdateCheckerTests GREEN");
    }

    private static void Expect(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
