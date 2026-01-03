using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
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

    [ObservableProperty] private double _downloadProgress;

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
        
        // Check if we need the database downloaded
        if (!File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EVE-NEIC", "eve.db")))
        {
            StatusText = "First Time Setup: Downloading Static Data Export (SDE)...";
            var progress = new Progress<string>(p =>
            {
                StatusText = p;
                
                // If the text contains a "%", try to parse it to update the progress bar
                if (p.Contains("%") && double.TryParse(p.Split('%')[0].Split(':').Last().Trim(), out var val))
                {
                    DownloadProgress = val;
                }
            });
            await _blueprintService.DownloadSdeAsync(progress);
        }

        // Load from the database
        StatusText = "Loading blueprints from database...";
        var list = await _blueprintService.GetBlueprintsFromSdeAsync();
        
        _allBlueprints = list;
        FilterBlueprints();
        
        IsBusy = false;
        StatusText = $"Loaded {list.Count} blueprints across {GroupedBlueprints.Count} categories.";
    }

    [RelayCommand]
    private async Task RefreshBlueprintsAsync()
    {
        IsBusy = true;
        
        StatusText = $"Refreshing blueprints from SDE...]";
        
        var list = await _blueprintService.GetBlueprintsFromSdeAsync();
        _allBlueprints = list;
        FilterBlueprints();
        
        IsBusy = false;
        StatusText = $"Refreshed {list.Count} blueprints.";
    }

    private void UpdateGroupedList(List<Blueprint> flatList)
    {
        GroupedBlueprints.Clear();
        
        // Check if we are currently searching
        bool shouldExpand = !string.IsNullOrWhiteSpace(SearchText);
        
        // LINQ Magic -> Group by Name -> Sort the groups by Name -> for each group sort its blueprints by name
        var groups = flatList
            .GroupBy(b => b.GroupName)
            .OrderBy(g => g.Key)
            .Select(g => new BlueprintGroup
            {
                Name = g.Key,
                Blueprints = g.OrderBy(b => b.Name).ToList(),
                IsExpanded = shouldExpand
            });

        foreach (var group in groups)
        {
            GroupedBlueprints.Add(group);
            
            Console.WriteLine($"{group.Name}: {group.Blueprints.Count} blueprints");
        }
    }
    
    
    [ObservableProperty] private string _searchText = string.Empty;
    private List<Blueprint> _allBlueprints = new();

    partial void OnSearchTextChanged(string value)
    {
        FilterBlueprints();
    }

    private void FilterBlueprints()
    {
        // If search is empty, show all blueprints
        var filtered = string.IsNullOrWhiteSpace(SearchText)
            ? _allBlueprints
            : _allBlueprints.Where(b => b.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase)).ToList();
        
        UpdateGroupedList(filtered);
    }
    
    [ObservableProperty] private Blueprint? _selectedBlueprint;
    
    partial void OnSelectedBlueprintChanged(Blueprint? value)
    {
        if (value != null)
        {
            // We use Task.Run so we don't block the UI while talking to the database
            Task.Run(async () =>
            {
                // Get the materials from a local SDE database
                if (value.Materials.Count == 0)
                {
                    var materials = await _blueprintService.GetMaterialsForBlueprintAsync(value);

                    // Avalonia UI updates must happen on the Main Thread
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        // Clear and refill the materials list
                        value.Materials.Clear();
                        foreach (var material in materials)
                        {
                            value.Materials.Add(material);
                        }
                    });
                    
                    // Fetch pices
                    StatusText = $"Fetching prices for {value.Name} from Jita...";
                    foreach (var material in value.Materials)
                    {
                        var price = await _blueprintService.GetJitaSellPriceAsync(material.TypeId);

                        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            material.UnitPrice = price;
                            // Recalculate the total build cost
                            value.RefreshTotal();
                            // We need to "refresh the UI
                            OnPropertyChanged(nameof(SelectedBlueprint));
                        });
                    }

                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        // Notify the UI that the total build cost has changed
                        OnPropertyChanged(nameof(SelectedBlueprint));
                    });
                    
                    StatusText = $"Loaded {value.Materials.Count} materials with live Jita prices.";
                }
                
                // Fetch the completed product price
                StatusText = $"Calculating profitability...";
                var productPrice = await _blueprintService.GetJitaSellPriceAsync(value.ProductTypeId);

                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    value.ProductPrice = productPrice;

                    // Final UI refresh
                    OnPropertyChanged(nameof(SelectedBlueprint));
                });

                StatusText = "Analysis Complete!";
            });
        }
        
        Console.WriteLine($"Selected blueprint: {value.Name} (ID: {value.TypeId})");
    }
}
