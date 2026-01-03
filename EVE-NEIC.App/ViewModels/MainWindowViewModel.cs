using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EVE_NEIC.App.Services;
using EVE_NEIC.App.Models;
using System.Linq;

namespace EVE_NEIC.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly BlueprintService _blueprintService = new();

    [ObservableProperty] private string _statusText = "Ready";

    [ObservableProperty] private bool _isBusy;

    public ObservableCollection<BlueprintGroup> GroupedBlueprints { get; } = new();

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

        UpdateGroupedList(list);
        
        IsBusy = false;
        StatusText = $"Loaded {list.Count} blueprints across {GroupedBlueprints.Count} categories.";
    }

    [RelayCommand]
    private async Task RefreshBlueprintsAsync()
    {
        IsBusy = true;

        var progress = new Progress<string>(name =>
        {
            StatusText = $"Refreshing blueprints from ESI (this may take a while)...[{name}]";
        });
        
        var list = await _blueprintService.RefreshCacheAsync(progress);
        
        UpdateGroupedList(list);
        
        IsBusy = false;
        StatusText = $"Refreshed {list.Count} blueprints.";
    }

    private void UpdateGroupedList(List<Blueprint> flatList)
    {
        GroupedBlueprints.Clear();
        
        // LINQ Magic -> Group by Name -> Sort the groups by Name -> for each group sort its blueprints by name
        var groups = flatList
            .GroupBy(b => b.GroupName)
            .OrderBy(g => g.Key)
            .Select(g => new BlueprintGroup
            {
                Name = g.Key,
                Blueprints = g.OrderBy(b => b.Name).ToList()
            });

        foreach (var group in groups)
        {
            GroupedBlueprints.Add(group);
            
            Console.WriteLine($"{group.Name}: {group.Blueprints.Count} blueprints");
        }
        
        
    }
}
