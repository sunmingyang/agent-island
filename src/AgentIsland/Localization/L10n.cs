using System.Globalization;

namespace AgentIsland.Localization;

/// String lookup with an English key namespace, mirroring the macOS L10n
/// helper. The zh-Hans table is ported from Resources/zh-Hans.lproj; keys
/// missing from the table fall back to the key itself (English).
public static class L10n
{
    public enum Language
    {
        Auto,
        English,
        SimplifiedChinese,
    }

    public static Language Current { get; set; } = Language.Auto;

    public static bool IsChinese => Current switch
    {
        Language.SimplifiedChinese => true,
        Language.English => false,
        _ => CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase),
    };

    public static string Tr(string key)
    {
        if (!IsChinese) return key;
        return ChineseTable.TryGetValue(key, out var value) ? value : key;
    }

    /// Composite-format lookup: the key is an English format string with
    /// {0}-style holes; the zh table carries the translated format.
    public static string TrFormat(string key, params object[] args) =>
        string.Format(Tr(key), args);

    /// Grows as features land; seeded with the strings the shell needs.
    private static readonly Dictionary<string, string> ChineseTable = new()
    {
        ["Show / Hide island"] = "显示 / 隐藏岛屿",
        ["Settings…"] = "设置…",
        ["Quit Agent Island"] = "退出 Agent Island",
        ["idle"] = "空闲",
        ["running"] = "运行中",
        ["your turn"] = "该你了",
        ["stalled"] = "已停滞",
        ["rate limited"] = "已限流",
        ["auth required"] = "需要登录",
        ["Continue"] = "继续",
        ["Command unavailable"] = "命令不可用",
        ["Usage refresh failed"] = "用量刷新失败",
        ["Claude stale"] = "Claude 数据已过期",
        ["Codex stale"] = "Codex 数据已过期",
        ["5h"] = "5小时",
        ["week"] = "周",
        ["resets in {0}"] = "{0} 后重置",
        ["Re-authenticate"] = "重新登录",
        ["waiting for login…"] = "等待登录…",
        ["Synced"] = "已同步",
        ["Syncing…"] = "同步中…",
        ["Usage"] = "用量",
        ["Cost"] = "成本",
        ["Overview"] = "总览",
        ["Triggers"] = "自动续跑",
        ["coming soon"] = "即将上线",
    };
}
