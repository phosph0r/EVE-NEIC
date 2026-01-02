using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EVE_NEIC.App.Services;
using EVE_NEIC.App.Models;

namespace EVE_NEIC.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly BlueprintService _blueprintService = new();

    [ObservableProperty] private string _statusText = "Ready";

    [ObservableProperty] private bool _isBusy;

    public ObservableCollection<Blueprint> Blueprints { get; } = new();

    public MainWindowViewModel()
    {
        // Load blueprints on startup
        Task.Run(() => _ = LoadBlueprintsAsync());
    }

    [RelayCommand]
    private async Task LoadBlueprintsAsync()
    {
        IsBusy = true;
        StatusText = "Loading blueprints...";

        var list = await _blueprintService.GetBlueprintsAsync();
        
        Blueprints.Clear();
        foreach (var blueprint in list)
        {
            Blueprints.Add(blueprint);
        }
        
        IsBusy = false;
        StatusText = $"Loaded {Blueprints.Count} blueprints.";
    }

    [RelayCommand]
    private async Task RefreshBlueprintsAsync()
    {
        IsBusy = true;
        StatusText = "Refreshing blueprints from ESI (this may take a while)...";
        
        var list = await _blueprintService.RefreshCacheAsync();
        
        Blueprints.Clear();
        foreach (var blueprint in list)
        {
            Blueprints.Add(blueprint);
        }
        
        IsBusy = false;
        StatusText = $"Refreshed {Blueprints.Count} blueprints.";
    }
}
