using System.Collections.ObjectModel;

namespace EVE_NEIC.App.Models;

public class Blueprint
{
    public int TypeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int GroupId { get; set; }
    public string GroupName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public ObservableCollection<Material> Materials { get; set; } = new();
    public string IconUrl => $"https://images.evetech.net/types/{TypeId}/bp?size=64";
}