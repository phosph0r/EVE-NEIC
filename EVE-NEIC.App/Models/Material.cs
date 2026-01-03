using CommunityToolkit.Mvvm.ComponentModel;

namespace EVE_NEIC.App.Models;

public partial class Material : ObservableObject
{
    public int TypeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Quantity { get; set; }
    
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(TotalPrice))]
    private decimal _unitPrice;
    
    public decimal TotalPrice => Quantity * UnitPrice;
}