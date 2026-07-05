using System.ComponentModel;
using AgentIsland.Core;

namespace AgentIsland.UI;

public enum ChartStyle
{
    Ring,
    Bar,
    Stepped,
    Numeric,
    Spark,
}

/// Usage chart visualization. Defaults to Stepped — the segmented style the
/// product's own screenshots and website ship with.
public sealed class StylePreferenceStore : INotifyPropertyChanged
{
    private const string Key = "MacIsland.chartStyle";

    public static StylePreferenceStore Shared { get; } = new();

    private ChartStyle _style;

    public event PropertyChangedEventHandler? PropertyChanged;

    private StylePreferenceStore()
    {
        var raw = Preferences.Get<string?>(Key);
        _style = Enum.TryParse<ChartStyle>(raw, ignoreCase: true, out var parsed)
            ? parsed
            : ChartStyle.Stepped;
    }

    public ChartStyle Style
    {
        get => _style;
        set
        {
            if (_style == value) return;
            _style = value;
            Preferences.Set(Key, value.ToString());
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Style)));
        }
    }

    public void Cycle()
    {
        var values = Enum.GetValues<ChartStyle>();
        Style = values[(Array.IndexOf(values, _style) + 1) % values.Length];
    }
}
