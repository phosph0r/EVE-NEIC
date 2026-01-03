using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace EVE_NEIC.App.Models;

public partial class Blueprint : ObservableObject
{
    public int TypeId { get; set; }
    public int ProductTypeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int GroupId { get; set; }
    public string GroupName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public ObservableCollection<Material> Materials { get; set; } = new();
    public string IconUrl => $"https://images.evetech.net/types/{TypeId}/bp?size=64";
    
    public decimal TotalBuildCost => Materials.Sum(m => m.TotalPrice);

    [ObservableProperty] 
    [NotifyPropertyChangedFor(nameof(Profit))] [NotifyPropertyChangedFor(nameof(Margin))]
    private decimal _productPrice;
    
    public decimal Profit => ProductPrice - TotalBuildCost;
    
    // Divide by zero protection
    public double Margin => ProductPrice > 0 ? (double)(Profit / ProductPrice) * 100 : 0;
    
    public void RefreshTotal()
    {
        OnPropertyChanged(nameof(TotalBuildCost));
        OnPropertyChanged(nameof(Profit));
        OnPropertyChanged(nameof(Margin));
    }
    
    // -- RESEARCH --
    [ObservableProperty] 
    [NotifyPropertyChangedFor(nameof(TotalBuildCost))] [NotifyPropertyChangedFor(nameof(Profit))] [NotifyPropertyChangedFor(nameof(Margin))]
    private int _materialEfficiency;
    
    [ObservableProperty]
    private int _timeEfficiency;

    partial void OnMaterialEfficiencyChanged(int value)
    {
        // When the slider moves, tell every material to re-calculate it's quantity
        foreach (var material in Materials)
        {
            material.RefreshUI();
        }
        
        // Update the grand total
        RefreshTotal();
    }
}