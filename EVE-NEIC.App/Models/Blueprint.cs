using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace EVE_NEIC.App.Models;

public partial class Blueprint : ObservableObject
{
    public int TypeId { get; set; }
    public int ProductTypeId { get; set; }
    public int ProductQuantity { get; set; } = 1;
    public string Name { get; set; } = string.Empty;
    public int GroupId { get; set; }
    public string GroupName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public ObservableCollection<Material> Materials { get; set; } = new();
    public string IconUrl => $"https://images.evetech.net/types/{TypeId}/bp?size=64";
    
    public decimal TotalBuildCost => Materials.Sum(m => m.TotalPrice);

    [ObservableProperty] 
    [NotifyPropertyChangedFor(nameof(Profit))] [NotifyPropertyChangedFor(nameof(Margin))] [NotifyPropertyChangedFor(nameof(IskPerHour))]
    private decimal _productPrice;
    
    // Divide by zero protection
    public double Margin => ProductPrice > 0 
        ? (double)(Profit / (ProductPrice * ProductQuantity)) * 100 
        : 0;
    
    public void RefreshTotal()
    {
        OnPropertyChanged(nameof(TotalBuildCost));
        OnPropertyChanged(nameof(Profit));
        OnPropertyChanged(nameof(Margin));
        OnPropertyChanged(nameof(IskPerHour));
    }
    
    // -- RESEARCH --
    [ObservableProperty] 
    [NotifyPropertyChangedFor(nameof(TotalBuildCost))] [NotifyPropertyChangedFor(nameof(Profit))] [NotifyPropertyChangedFor(nameof(Margin))] [NotifyPropertyChangedFor(nameof(IskPerHour))]
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

    partial void OnTimeEfficiencyChanged(int value)
    {
        OnPropertyChanged(nameof(FormattedAdjustedProductionTime));
        OnPropertyChanged(nameof(AdjustedProductionTime));
        OnPropertyChanged(nameof(IskPerHour));
    }
    
    // --- PROFIT ---
    // Raw time from SDE (in seconds)
    public int BaseProductionTime { get; set; }
    
    // Time after Time Efficiency is applied (in seconds)
    public double AdjustedProductionTime => BaseProductionTime * (1.0 - (TimeEfficiency / 100.0));
    
    public string FormattedAdjustedProductionTime
    {
        get
        {
            var t = TimeSpan.FromSeconds(AdjustedProductionTime);
            return string.Format("{0:D2}d {1:D2}h {2:D2}m {3:D2}", t.Days, t.Hours, t.Minutes, t.Seconds);
        }
    }
    
    // ISK Per Hour calculation
    // Formula: (Profit / TotalSeconds) * 3600
    public decimal IskPerHour => AdjustedProductionTime > 0 
        ? Profit / (decimal)AdjustedProductionTime * 3600 
        : 0;
    
    // --- TAXES ---
    // TODO: Update formulas
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Profit))] [NotifyPropertyChangedFor(nameof(Margin))] [NotifyPropertyChangedFor(nameof(IskPerHour))]
    private double _salesTaxRate = 7.5; // Base is 7.5%, reduced by Accounting skill
    
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Profit))] [NotifyPropertyChangedFor(nameof(Margin))] [NotifyPropertyChangedFor(nameof(IskPerHour))]
    private double _brokerFeeRate = 3.0; // Varies based on Broker Relations skill and standing -- Formula: Brokers Fee = 3% - (0.3% * Broker Relations Level) - (0.03% * Faction Standing) - (0.02% * Corporation Standing)
    
    // Update the profit calculation to subtract taxes and fees from the SELLING price
    // Formula: (Revenue - Taxes - Fees) - Cost
    public decimal Profit
    {
        get
        {
            decimal taxAmount = (ProductPrice * ProductQuantity) * (decimal)(SalesTaxRate / 100.0);
            decimal feeAmount = (ProductPrice * ProductQuantity) * (decimal)(BrokerFeeRate / 100.0);
            return (ProductPrice * ProductQuantity) - taxAmount - feeAmount - TotalBuildCost;
        }
    }
    
}