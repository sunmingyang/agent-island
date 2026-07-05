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
        ["It's your turn"] = "该你了",
        ["{0} is waiting"] = "{0} 在等你",
        ["The session finished. Come back and reply."] = "会话已完成，回来接手吧。",
        ["Open session"] = "打开会话",
        ["Got it"] = "知道了",
        ["Provider"] = "提供方",
        ["Session"] = "会话",
        ["Project"] = "项目",
        ["{0} is waiting for you"] = "{0} 在等你回复",
        ["A background coding session finished a turn: {0}."] = "后台编码会话完成了一轮：{0}。",
        ["A background coding session finished a turn. It is your turn."] = "后台编码会话完成了一轮，该你了。",
        ["today"] = "今日",
        ["{0} this month"] = "本月 {0}",
        ["{0} tokens · {1} billable"] = "{0} tokens · 计费 {1}",
        ["Click a day for details"] = "点击某天查看明细",
        ["Add rule"] = "添加规则",
        ["Manage"] = "管理",
        ["after quota reset"] = "额度重置后",
        ["every {0}h"] = "每 {0} 小时",
        ["untrusted — click to trust"] = "未信任——点击信任",
        ["No rules yet. Add one to auto-resume a session after the quota resets."] = "还没有规则。添加一条，让会话在额度重置后自动续跑。",
        ["Auto-resume enabled (kill switch)"] = "自动续跑总开关",
        ["Open run records"] = "打开运行记录",
        ["Trust this project for auto-resume"] = "信任此项目用于自动续跑",
        ["Settings"] = "设置",
        ["General"] = "通用",
        ["Display"] = "显示",
        ["Providers"] = "提供方",
        ["Status"] = "状态",
        ["Language"] = "语言",
        ["Auto (system)"] = "自动（跟随系统）",
        ["Launch at login"] = "开机自启",
        ["Alerts"] = "阈值告警",
        ["Approaching-limit alerts"] = "接近限额告警",
        ["Warning at {0}%"] = "警告阈值 {0}%",
        ["Critical at {0}%"] = "严重阈值 {0}%",
        ["Left-click +5, right-click -5"] = "左键 +5，右键 -5",
        ["Language saved. Restart Agent Island to apply everywhere."] = "语言已保存。重启 Agent Island 后全局生效。",
        ["Chart style"] = "图表样式",
        ["Usage charts"] = "用量图表",
        ["Top bar"] = "顶栏",
        ["Wide (notch style)"] = "宽（刘海样式）",
        ["Compact"] = "紧凑",
        ["Bar width"] = "顶栏宽度",
        ["Show cost page"] = "显示成本页",
        ["Refresh"] = "刷新",
        ["Every 5 minutes"] = "每 5 分钟",
        ["Every 15 minutes"] = "每 15 分钟",
        ["Every 30 minutes"] = "每 30 分钟",
        ["Usage refresh interval"] = "用量刷新间隔",
        ["Accounts"] = "账户",
        ["Re-authenticate Claude"] = "重新登录 Claude",
        ["Re-authenticate Codex"] = "重新登录 Codex",
        ["Auto-resume"] = "自动续跑",
        ["Run records"] = "运行记录",
        ["Trusted projects"] = "受信任的项目",
        ["No trusted projects yet."] = "还没有受信任的项目。",
        ["Remove"] = "移除",
        ["Status legend"] = "状态图例",
        ["needs attention"] = "需要关注",
        ["Nothing running — or the turn is over and it's yours."] = "没有任务在跑——或者这一轮已经结束，该你了。",
        ["A session is working; the logo spins."] = "会话工作中；logo 旋转。",
        ["A turn finished; the alarm calls you back."] = "一轮完成；闹钟叫你回来。",
        ["Rate limit, login, network, or provider error."] = "限流、登录、网络或服务方错误。",
        ["Turn alarm"] = "回合闹钟",
        ["Turn alarms"] = "回合闹钟",
        ["Alarm sound"] = "闹钟声音",
        ["Sound"] = "音色",
        ["Volume"] = "音量",
        ["Show session details in alarms"] = "闹钟中显示会话详情",
    };
}
