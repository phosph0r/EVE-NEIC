using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace EVE_NEIC.App.Models;

public partial class Material : ObservableObject
{
    private readonly Blueprint _parent;
    public int TypeId { get; set; }
    public string Name { get; set; } = string.Empty;
    // Store the base quantity from SDE
    public int BaseQuantity { get; set; }
    
    [ObservableProperty] 
    [NotifyPropertyChangedFor(nameof(TotalPrice))] [NotifyPropertyChangedFor(nameof(Quantity))]
    private decimal _unitPrice;

    // Make sure this material knows which blueprint it belongs to
    public Material(Blueprint parent)
    {
        _parent = parent;
    }
    
    // Calculated Quantity using Material Efficiency
    public int Quantity
    {
        get
        {
            if(_parent.MaterialEfficiency == 0) 
                return BaseQuantity;
            
            // Formula: Base * (1 - ME%)
            double factor = 1.0 - (_parent.MaterialEfficiency / 100.0);
            int calculated = (int)Math.Ceiling(BaseQuantity * factor);
            
            // Minimum required is always 1 (unless the base is 0)
            return Math.Max(BaseQuantity > 0 ? 1 : 0, calculated);
        }
    }

    public decimal TotalPrice => UnitPrice * Quantity;
    
    // Helper to refresh the UI
    public void RefreshUI()
    {
        OnPropertyChanged(nameof(TotalPrice));
        OnPropertyChanged(nameof(Quantity));
    }
}