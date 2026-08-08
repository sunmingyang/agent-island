using System.IO;
using AgentIsland.Core;

namespace AgentIsland.Cost;

/// Gemini cost reader — an honest STUB.
///
/// Gemini ships NO local token ledger as of 2026-08. The CLI keeps a
/// per-project working directory at %USERPROFILE%\.gemini\tmp\&lt;project&gt;\
/// holding a chats\ folder of $set checkpoint streams plus a logs.json file,
/// but none of those records carry per-message token accounting — on the
/// survey machine chats\ is empty and logs.json is []. There is nothing here
/// to cost, so <see cref="Scan"/> returns no events today.
///
/// Gemini's usage percentages come from the live Code Assist endpoint (the
/// macOS GeminiUsageFetcher), which reports QUOTA buckets — not tokens and not
/// dollars. That path is display-only and unrelated to this reader; this file
/// never invents a cost or a token count to stand in for it.
///
/// The seam is <see cref="ParseFile"/>: <see cref="Scan"/> already walks the
/// same tree a real build would, and the one method a future contributor fills
/// in is marked with TODO(gemini-tokens) — the day a per-message usage shape
/// appears, wire it there and every downstream summary picks Gemini up
/// automatically.
public static class GeminiLogReader
{
    // Mirrors SessionScanner's private GeminiTmpRoot. Kept local so this
    // stub owns no other file; promote to IslandPaths if a second consumer
    // ever needs it.
    private static string GeminiTmpRoot => Path.Combine(IslandPaths.Home, ".gemini", "tmp");

    /// Walk %USERPROFILE%\.gemini\tmp\&lt;project&gt;\{chats\*.jsonl, logs.json}
    /// and return every usage-bearing turn from the last <paramref name="lookbackDays"/>
    /// days. Pure file IO; no network. Returns an empty list today (no local
    /// Gemini token ledger exists) and never throws on the empty / missing /
    /// unreadable case.
    public static List<TokenEvent> Scan(int lookbackDays)
    {
        var cutoff = DateTimeOffset.Now.AddDays(-lookbackDays);
        var output = new List<TokenEvent>();
        if (!Directory.Exists(GeminiTmpRoot)) return output;

        foreach (var projectDir in SafeEnumerateDirectories(GeminiTmpRoot))
        {
            // Two candidate ledgers per project, both empty as of 2026-08:
            // the chat checkpoint streams and the top-level logs.json.
            var chats = Path.Combine(projectDir, "chats");
            if (Directory.Exists(chats))
            {
                foreach (var path in SessionScanner.SafeEnumerateFiles(chats, "*.jsonl"))
                {
                    output.AddRange(ParseFile(path, cutoff));
                }
            }
            output.AddRange(ParseFile(Path.Combine(projectDir, "logs.json"), cutoff));
        }
        return output;
    }

    /// The single fill-in point. Reads one ledger file's text (a missing,
    /// empty, or unreadable file contributes nothing — this never throws) and
    /// hands it to the decode step.
    internal static List<TokenEvent> ParseFile(string path, DateTimeOffset cutoff)
    {
        var output = new List<TokenEvent>();
        string text;
        try
        {
            if (!File.Exists(path)) return output;
            text = File.ReadAllText(path);
        }
        catch
        {
            return output;
        }
        if (string.IsNullOrWhiteSpace(text)) return output;

        // TODO(gemini-tokens): decode per-message usage from `text`. The
        // expected shape mirrors every other reader — for each assistant turn
        // recover (timestamp, model, inputTokens, outputTokens, cacheReadTokens)
        // and, guarding `timestamp >= cutoff`, add:
        //
        //   output.Add(new TokenEvent(
        //       TriggerTool.Gemini, timestamp, model,
        //       inputTokens, outputTokens,
        //       0 /* CacheCreationTokens */, cacheReadTokens,
        //       null /* SelfReportedCostUSD — Gemini ships no dollar figure */));
        //
        // Leave SelfReportedCostUSD null so the event stays table-priced
        // downstream; never fabricate a cost. If the shape turns out to be
        // line-oriented (JSONL), stream with File.ReadLines rather than
        // ReadAllText.
        //
        // As of 2026-08 no such record exists (chats\ empty, logs.json == []),
        // so there is nothing to decode and we emit nothing.
        return output;
    }

    /// Immediate subdirectories only, tolerating a missing root and a folder
    /// that vanishes mid-walk (Gemini rotates per-project dirs while running).
    private static List<string> SafeEnumerateDirectories(string root)
    {
        try
        {
            if (!Directory.Exists(root)) return new List<string>();
            return Directory.EnumerateDirectories(root).ToList();
        }
        catch
        {
            return new List<string>();
        }
    }
}
