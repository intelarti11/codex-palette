using System.Collections.ObjectModel;
using System.Windows.Media;
using CodexPalette.Native.Infrastructure;
using CodexPalette.Native.Models;

namespace CodexPalette.Native.ViewModels;

public sealed class ModelHeaderViewModel
{
    public ModelHeaderViewModel(ModelDefinition definition)
    {
        Name = definition.Name;
        Glyph = definition.Glyph;
        AccentBrush = BrushFactory.FromHex(definition.Accent);
    }

    public string Name { get; }
    public string Glyph { get; }
    public Brush AccentBrush { get; }
}

public sealed class EffortRowViewModel(string label, ObservableCollection<PaletteCellViewModel> cells)
{
    public string Label { get; } = label;
    public ObservableCollection<PaletteCellViewModel> Cells { get; } = cells;
}

public sealed class PaletteCellViewModel : ObservableObject
{
    private bool _isSelected;

    public PaletteCellViewModel(
        int modelIndex,
        int effortIndex,
        string modelName,
        Brush accentBrush,
        bool isSupported,
        bool isSelected)
    {
        ModelIndex = modelIndex;
        EffortIndex = effortIndex;
        ModelName = modelName;
        AccentBrush = accentBrush;
        IsSupported = isSupported;
        _isSelected = isSelected;
    }

    public int ModelIndex { get; }
    public int EffortIndex { get; }
    public string ModelName { get; }
    public Brush AccentBrush { get; }
    public bool IsSupported { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

public sealed class SpeedOptionViewModel : ObservableObject
{
    private bool _isSelected;

    public SpeedOptionViewModel(int index, string label, bool isSelected)
    {
        Index = index;
        Label = label;
        _isSelected = isSelected;
    }

    public int Index { get; }
    public string Label { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

internal static class BrushFactory
{
    public static Brush FromHex(string value)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(value)!;
        brush.Freeze();
        return brush;
    }
}
