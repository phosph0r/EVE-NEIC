using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls.Converters;
using EVE_NEIC.App.Models;
using Microsoft.Data.Sqlite;
using ICSharpCode.SharpZipLib.BZip2;

namespace EVE_NEIC.App.Services;

public class BlueprintService
{
    private readonly string _dbPath;
    private const string DbFileName = "eve.db";
    private readonly HttpClient _httpClient;
    private readonly string _cacheFilePath;
    private const string EsiBaseUrl = "https://esi.evetech.net/latest/";
    private const int BlueprintCategoryId = 9;

    public BlueprintService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var cacheDir = Path.Combine(appData, "EVE-NEIC");
        Directory.CreateDirectory(cacheDir);
        _dbPath = Path.Combine(cacheDir, DbFileName);
        _cacheFilePath = Path.Combine(cacheDir, "blueprints.json");
        
        _httpClient = new HttpClient {BaseAddress = new Uri(EsiBaseUrl)};
        _httpClient.DefaultRequestHeaders.Add("Accept-Language", "en");
    }

    public async Task<List<Blueprint>> GetBlueprintsFromSdeAsync()
    {
        var blueprints = new List<Blueprint>();

        
        try
        {
            if (!File.Exists(_dbPath))
            {
                return blueprints;
            }

            using (var connection = new SqliteConnection($"Data Source={_dbPath};Mode=ReadOnly"))
            {
                await connection.OpenAsync();
            
                var command = connection.CreateCommand();
                
                command.CommandText =
                    @"
                    SELECT invTypes.typeID, invTypes.typeName, COALESCE(product.description, invTypes.description) AS description, invGroups.groupName
                    FROM invTypes
                    JOIN invGroups ON invTypes.groupID = invGroups.groupID
                    -- Link the blueprint to it's manufactured product
                    LEFT JOIN industryActivityProducts iap ON iap.typeID = invTypes.typeID AND iap.activityID = 1
                    -- Get the description for the manufactured product
                    LEFT JOIN invTypes product ON iap.productTypeID = product.typeID
                    WHERE invGroups.categoryID = 9 AND invTypes.published = 1
                ";

                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        blueprints.Add(new Blueprint
                        {
                            TypeId = reader.GetInt32(0),
                            Name = reader.GetString(1),
                            // Handle potential null descriptions
                            Description = reader.IsDBNull(2) ? "" : reader.GetString(2),
                            GroupName = reader.GetString(3)
                        });
                    }
                }
        }
        }
        catch(Exception ex)
        {
            Console.WriteLine($"SQL Error: {ex.Message}");
            throw;
        }
        
        return blueprints;
    }

    public async Task DownloadSdeAsync(IProgress<string>? progress = null)
    {
        string compressedFilePath = _dbPath + ".bz2";
        string downloadUrl = "https://www.fuzzwork.co.uk/dump/latest/eve.db.bz2";
        
        progress?.Report("Downloading Static Data Export (SDE) (this may take a while)...");

        var handler = new HttpClientHandler() {AllowAutoRedirect = true};
        using (var client = new HttpClient(handler))
        {
            client.DefaultRequestHeaders.Add("User-Agent", "EVE-NEIC");
            client.Timeout = TimeSpan.FromMinutes(1);
            // Download the file
            using (var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                
                // Get the total size of the file from the website
                var totalBytes = response.Content.Headers.ContentLength;
                
                using (var contentStream = await response.Content.ReadAsStreamAsync())
                using (var fileStream = File.Create(compressedFilePath))
                {
                    var buffer = new byte[81920]; // 80KB buffer
                    long totalRead = 0;
                    int read;

                    while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, read);
                        totalRead += read;
                        
                        // Report progress every time we read a chunk
                        if (totalBytes.HasValue)
                        {
                            var percentage = (double)totalRead / totalBytes.Value * 100;
                            progress?.Report($"Downloading SDE: {percentage:F1}% ({totalRead / 1024 / 1024}MB / {totalBytes.Value / 1024 / 1024}MB");
                        }

                        else
                        {
                            progress?.Report($"Downloading SDE: {totalRead / 1024 / 1024}MB");
                        }
                    }
                }
            }
            
            progress?.Report("Extracting the database...");
            
            // Extract the .bz2 to eve.db file
            await Task.Run(() =>
            {
                using (FileStream compressedStream = File.OpenRead(compressedFilePath))
                using (FileStream targetStream = File.Create(_dbPath))
                {
                    BZip2.Decompress(compressedStream, targetStream, true);
                }
            });
            
            // Delete the compressed file
            File.Delete(compressedFilePath);
            
            progress?.Report("SDE Database Ready!");
        }
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
                                        Description = typeResponse.description,
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
    private record TypeResponse(string name, string description, bool published);
}