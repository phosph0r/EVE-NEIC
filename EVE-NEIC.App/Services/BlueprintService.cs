using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using EVE_NEIC.App.Models;

namespace EVE_NEIC.App.Services;

public class BlueprintService
{
    private readonly HttpClient _httpClient;
    private readonly string _cacheFilePath;
    private const string EsiBaseUrl = "https://esi.evetech.net/latest/";
    private const int BlueprintCategoryId = 9;

    public BlueprintService()
    {
        _httpClient = new HttpClient { BaseAddress = new Uri(EsiBaseUrl) };
        // We'll store the cache inthe user's local app data folder
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var cacheDir = Path.Combine(appData, "EVE-NEIC");
        Directory.CreateDirectory(cacheDir);
        _cacheFilePath = Path.Combine(cacheDir, "blueprints.json");
    }

    public async Task<List<Blueprint>> GetBlueprintsAsync(bool forceRefresh = false)
    {
        // Check if cache exists and we aren't forcing a refresh
        if (!forceRefresh && File.Exists(_cacheFilePath))
        {
            using var stream = File.OpenRead(_cacheFilePath);
            return await JsonSerializer.DeserializeAsync<List<Blueprint>>(stream) ?? new();
        }
        
        // Otherwise refresh from ESI
        return await RefreshCacheAsync();
    }

    public async Task<List<Blueprint>> RefreshCacheAsync(IProgress<string>? progress = null)
    {
        var blueprints = new List<Blueprint>();
        
        // Temporary cache to avoid fetching the same group name multiple times
        var groupNames = new Dictionary<int, string>();

        try
        {
            // Get group IDs from Category 9 (Blueprints)
            var categoryResponse = await _httpClient.GetFromJsonAsync<CategoryResponse>($"universe/categories/{BlueprintCategoryId}/");
            if (categoryResponse?.groups == null) return blueprints;

            foreach (var groupId in categoryResponse.groups)
            {
                // Fetch the group name if we haven't already
                if (!groupNames.ContainsKey(groupId))
                {
                    var groupResponse = await _httpClient.GetFromJsonAsync<GroupResponse>($"universe/groups/{groupId}/");
                    if (groupResponse != null)
                    {
                        groupNames[groupId] = groupResponse.name;
                        
                        // Now get the types for this group
                        if (groupResponse.types != null)
                        {
                            foreach (var typeId in groupResponse.types)
                            {
                                // Get details for each type
                                var typeResponse = await _httpClient.GetFromJsonAsync<TypeResponse>($"universe/types/{typeId}/");

                                if (typeResponse != null && typeResponse.published)
                                {
                                    progress?.Report($"Adding blueprint {typeResponse.name}");
                                    
                                    blueprints.Add(new Blueprint()
                                    {
                                        TypeId = typeId,
                                        Name = typeResponse.name,
                                        GroupId = groupId,
                                        GroupName = groupNames[groupId]
                                    });
                                }
                            }
                        }
                    }
                }
            }

            // Save to a local cache
            using var createStream = File.Create(_cacheFilePath);
            await JsonSerializer.SerializeAsync(createStream, blueprints);
        }
        catch (Exception ex)
        {
            // For now just log to console
            Console.WriteLine($"Error refreshing blueprints: {ex.Message}");
        }
        
        return blueprints;
    }
    
    // Helper records to match ESI JSON structure
    private record CategoryResponse(List<int> groups);
    private record GroupResponse(string name, List<int> types);
    private record TypeResponse(string name, bool published);
}