using System.Collections.Generic;

namespace EVE_NEIC.App.Models;

public class BlueprintGroup
{
    public string Name { get; set; } = string.Empty;
    public List<Blueprint> Blueprints { get; set; } = new();
}