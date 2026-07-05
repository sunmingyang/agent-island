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
    };
}
