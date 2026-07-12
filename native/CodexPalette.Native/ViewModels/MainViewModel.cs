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
    private int _selectedEffortIndex = -1;
    private int _selectedSpeedIndex = -1;
    private string _selectedModelName = string.Empty;
    private string _selectedEffortName = string.Empty;
    private string _selectorText = string.Empty;
    private string _speedLabel = string.Empty;
    private IReadOnlyList<IReadOnlyList<int>> _supportedEfforts = Array.Empty<IReadOnlyList<int>>();

    public MainViewModel(CodexAutomationService automation)
    {
        _automation = automation;
        Models = [];
        EffortRows = [];
        Speeds = [];
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
    public bool IsMatrixEmpty => Models.Count == 0 || EffortRows.Count == 0;
    public int ModelColumnCount => Math.Max(Models.Count, 1);

    public string SelectedModelName => !string.IsNullOrWhiteSpace(_selectedModelName)
        ? _selectedModelName
        : _selectorText;

    public string SelectedEffortName => _selectedEffortName;
    public Brush SelectedAccentBrush => Models.Count > 0 && _selectedModelIndex >= 0 && _selectedModelIndex < Models.Count
        ? Models[_selectedModelIndex].AccentBrush
        : BrushFactory.FromHex("#4A84AE");

    public string SpeedLabel
    {
        get => _speedLabel;
        private set => SetProperty(ref _speedLabel, value);
    }

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

    public async Task DiscoverAsync()
    {
        await _operationLock.WaitAsync();
        IsBusy = true;
        Notice = null;
        try
        {
            ApplyNativeState(await _automation.DiscoverPaletteAsync());
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
            _selectedModelName = cell.ModelName;
            _selectedEffortIndex = cell.EffortIndex;
            _selectedEffortName = cell.EffortIndex >= 0 && cell.EffortIndex < EffortRows.Count
                ? EffortRows[cell.EffortIndex].Label
                : _selectedEffortName;
            UpdateCellSelection();
            OnPropertyChanged(nameof(SelectedModelName));
            OnPropertyChanged(nameof(SelectedEffortName));
            OnPropertyChanged(nameof(SelectedAccentBrush));
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
        _selectedSpeedIndex = state.SpeedIndex;
        _selectedModelName = state.CurrentModel;
        _selectorText = state.SelectorText;
        _supportedEfforts = state.SupportedEfforts;

        if (!Models.Select(static model => model.Name).SequenceEqual(state.Models, StringComparer.Ordinal))
        {
            Models.Clear();
            for (var index = 0; index < state.Models.Count; index++)
            {
                Models.Add(new ModelHeaderViewModel(ModelCatalog.Create(state.Models[index], index)));
            }
            OnPropertyChanged(nameof(ModelColumnCount));
            OnPropertyChanged(nameof(IsMatrixEmpty));
        }

        if (state.Efforts.Count > 0 && Models.Count > 0)
        {
            _selectedEffortIndex = state.EffortIndex;
            RebuildEfforts(state.Efforts);
            if (_selectedEffortIndex >= 0 && _selectedEffortIndex < EffortRows.Count)
            {
                _selectedEffortName = EffortRows[_selectedEffortIndex].Label;
            }
            else if (!string.IsNullOrWhiteSpace(state.CurrentEffort))
            {
                _selectedEffortName = state.CurrentEffort;
            }
        }
        else
        {
            _selectedEffortIndex = -1;
            if (!string.IsNullOrWhiteSpace(state.CurrentEffort))
            {
                _selectedEffortName = state.CurrentEffort;
            }
            UpdateCellSelection();
        }

        RebuildSpeeds(state.SpeedLabel, state.Speeds);
        OnPropertyChanged(nameof(SelectedModelName));
        OnPropertyChanged(nameof(SelectedEffortName));
        OnPropertyChanged(nameof(SelectedAccentBrush));
        OnPropertyChanged(nameof(IsMatrixEmpty));
    }

    private void RebuildEfforts(IReadOnlyList<string> effortLabels)
    {
        EffortRows.Clear();
        for (var effortIndex = 0; effortIndex < effortLabels.Count; effortIndex++)
        {
            var cells = new ObservableCollection<PaletteCellViewModel>();
            for (var modelIndex = 0; modelIndex < Models.Count; modelIndex++)
            {
                var definition = Models[modelIndex];
                cells.Add(new PaletteCellViewModel(
                    modelIndex,
                    effortIndex,
                    definition.Name,
                    definition.AccentBrush,
                    modelIndex < _supportedEfforts.Count && _supportedEfforts[modelIndex].Contains(effortIndex),
                    modelIndex == _selectedModelIndex && effortIndex == _selectedEffortIndex));
            }

            EffortRows.Add(new EffortRowViewModel(effortLabels[effortIndex], cells));
        }
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
