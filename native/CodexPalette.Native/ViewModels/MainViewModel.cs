using System.Collections.ObjectModel;
using System.Windows.Media;
using CodexPalette.Native.Automation;
using CodexPalette.Native.Infrastructure;
using CodexPalette.Native.Models;

namespace CodexPalette.Native.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly CodexAutomationService _automation;
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private bool _isOpen;
    private bool _isBusy;
    private string? _notice;
    private int _selectedModelIndex;
    private int _selectedEffortIndex = 3;
    private int _selectedSpeedIndex = -1;

    public MainViewModel(CodexAutomationService automation)
    {
        _automation = automation;
        Models = new ObservableCollection<ModelHeaderViewModel>(
            ModelCatalog.All.Select(static model => new ModelHeaderViewModel(model)));
        EffortRows = [];
        Speeds = [];
        RebuildEfforts(Enumerable.Repeat("…", 5).ToArray());
    }

    public ObservableCollection<ModelHeaderViewModel> Models { get; }
    public ObservableCollection<EffortRowViewModel> EffortRows { get; }
    public ObservableCollection<SpeedOptionViewModel> Speeds { get; }

    public bool IsOpen
    {
        get => _isOpen;
        set => SetProperty(ref _isOpen, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public string? Notice
    {
        get => _notice;
        private set
        {
            if (SetProperty(ref _notice, value))
            {
                OnPropertyChanged(nameof(HasNotice));
            }
        }
    }

    public bool HasNotice => !string.IsNullOrWhiteSpace(Notice);
    public bool HasSpeeds => Speeds.Count == 2;

    public string SelectedModelName =>
        ModelCatalog.All[Math.Clamp(_selectedModelIndex, 0, ModelCatalog.All.Count - 1)].Name;

    public string SelectedEffortName =>
        _selectedEffortIndex >= 0 && _selectedEffortIndex < EffortRows.Count
            ? EffortRows[_selectedEffortIndex].Label
            : "…";

    public async Task RefreshAsync(bool showErrors = false)
    {
        if (!await _operationLock.WaitAsync(0))
        {
            return;
        }

        try
        {
            var state = await _automation.ReadStateAsync();
            ApplyNativeState(state);
            if (!showErrors)
            {
                Notice = null;
            }
        }
        catch (Exception exception)
        {
            if (showErrors)
            {
                Notice = exception.Message;
            }
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task<bool> ApplySelectionAsync(PaletteCellViewModel cell)
    {
        if (IsBusy || !cell.IsSupported)
        {
            return false;
        }

        await _operationLock.WaitAsync();
        IsBusy = true;
        Notice = null;
        try
        {
            var result = await _automation.ApplySelectionAsync(cell.ModelIndex, cell.EffortIndex);
            _selectedModelIndex = cell.ModelIndex;
            _selectedEffortIndex = cell.EffortIndex;
            UpdateCellSelection();
            OnPropertyChanged(nameof(SelectedModelName));
            OnPropertyChanged(nameof(SelectedEffortName));
            Notice = result.DisplayName;
            return true;
        }
        catch (Exception exception)
        {
            Notice = exception.Message;
            return false;
        }
        finally
        {
            IsBusy = false;
            _operationLock.Release();
        }
    }

    public async Task ApplySpeedAsync(SpeedOptionViewModel option)
    {
        if (IsBusy || option.Index == _selectedSpeedIndex)
        {
            return;
        }

        await _operationLock.WaitAsync();
        IsBusy = true;
        Notice = null;
        try
        {
            var result = await _automation.ApplySpeedAsync(option.Index);
            _selectedSpeedIndex = result.Index;
            UpdateSpeedSelection();
            Notice = result.Label;
        }
        catch (Exception exception)
        {
            Notice = exception.Message;
        }
        finally
        {
            IsBusy = false;
            _operationLock.Release();
        }
    }

    public void ClearNotice() => Notice = null;

    private void ApplyNativeState(NativePaletteState state)
    {
        _selectedModelIndex = state.ModelIndex;
        _selectedEffortIndex = state.EffortIndex;
        _selectedSpeedIndex = state.SpeedIndex;
        RebuildEfforts(state.Efforts);
        RebuildSpeeds(state.SpeedLabel, state.Speeds);
        OnPropertyChanged(nameof(SelectedModelName));
        OnPropertyChanged(nameof(SelectedEffortName));
    }

    private void RebuildEfforts(IReadOnlyList<string> effortLabels)
    {
        EffortRows.Clear();
        for (var effortIndex = 0; effortIndex < effortLabels.Count; effortIndex++)
        {
            var cells = new ObservableCollection<PaletteCellViewModel>();
            for (var modelIndex = 0; modelIndex < ModelCatalog.All.Count; modelIndex++)
            {
                var definition = ModelCatalog.All[modelIndex];
                cells.Add(new PaletteCellViewModel(
                    modelIndex,
                    effortIndex,
                    definition.Name,
                    BrushFactory.FromHex(definition.Accent),
                    ModelCatalog.Supports(modelIndex, effortIndex),
                    modelIndex == _selectedModelIndex && effortIndex == _selectedEffortIndex));
            }

            EffortRows.Add(new EffortRowViewModel(effortLabels[effortIndex], cells));
        }

        OnPropertyChanged(nameof(SelectedEffortName));
    }

    private void RebuildSpeeds(string label, IReadOnlyList<string> speedLabels)
    {
        Speeds.Clear();
        if (speedLabels.Count == 2)
        {
            for (var index = 0; index < speedLabels.Count; index++)
            {
                Speeds.Add(new SpeedOptionViewModel(index, speedLabels[index], index == _selectedSpeedIndex));
            }
        }

        SpeedLabel = label;
        OnPropertyChanged(nameof(HasSpeeds));
    }

    private string _speedLabel = string.Empty;
    public string SpeedLabel
    {
        get => _speedLabel;
        private set => SetProperty(ref _speedLabel, value);
    }

    private void UpdateCellSelection()
    {
        foreach (var cell in EffortRows.SelectMany(static row => row.Cells))
        {
            cell.IsSelected = cell.ModelIndex == _selectedModelIndex && cell.EffortIndex == _selectedEffortIndex;
        }
    }

    private void UpdateSpeedSelection()
    {
        foreach (var speed in Speeds)
        {
            speed.IsSelected = speed.Index == _selectedSpeedIndex;
        }
    }
}
