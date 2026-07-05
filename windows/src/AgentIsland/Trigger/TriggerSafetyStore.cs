using System.ComponentModel;
using System.IO;
using AgentIsland.Core;

namespace AgentIsland.Trigger;

/// The guardrails around unattended resume: a kill switch and a per-project
/// allow list. Both persist; blocked fires are logged, never silent.
public sealed class TriggerSafetyStore : INotifyPropertyChanged
{
    public static TriggerSafetyStore Shared { get; } = new();

    private const string EnabledKey = "AgentIsland.triggerExecutionEnabled";
    private const string AllowedRootsKey = "AgentIsland.triggerAllowedRoots";

    private bool _executionEnabled;
    private HashSet<string> _allowedRoots;

    public event PropertyChangedEventHandler? PropertyChanged;

    private TriggerSafetyStore()
    {
        _executionEnabled = Preferences.Get<bool?>(EnabledKey) ?? true;
        _allowedRoots = new HashSet<string>(
            Preferences.Get<List<string>?>(AllowedRootsKey) ?? new List<string>(),
            StringComparer.OrdinalIgnoreCase);
    }

    public bool ExecutionEnabled
    {
        get => _executionEnabled;
        set
        {
            _executionEnabled = value;
            Preferences.Set(EnabledKey, value);
            Raise(nameof(ExecutionEnabled));
        }
    }

    public IReadOnlyCollection<string> AllowedRoots => _allowedRoots;

    public bool IsAllowed(string cwd)
    {
        var root = Normalized(cwd);
        return root.Length == 0 || _allowedRoots.Contains(root);
    }

    public void SetAllowed(string cwd, bool allowed)
    {
        var root = Normalized(cwd);
        if (root.Length == 0) return;
        if (allowed) _allowedRoots.Add(root); else _allowedRoots.Remove(root);
        Preferences.Set(AllowedRootsKey, _allowedRoots.OrderBy(r => r).ToList());
        Raise(nameof(AllowedRoots));
    }

    public static string Normalized(string cwd)
    {
        if (string.IsNullOrWhiteSpace(cwd)) return "";
        try { return Path.GetFullPath(cwd).TrimEnd('\\', '/'); }
        catch { return cwd.TrimEnd('\\', '/'); }
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
