using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
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

    // Helper records to match ESI JSON structure
    private record CategoryResponse(List<int> groups);
    private record GroupResponse(string name, List<int> types);
    private record TypeResponse(string name, string description, bool published);
    private record MarketOrderResponse(decimal price);
    
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
                    SELECT invTypes.typeID, invTypes.typeName, COALESCE(product.description, invTypes.description) AS description, invGroups.groupName, iap.productTypeID, iap.quantity, industryActivity.time
                    FROM invTypes
                    JOIN invGroups ON invTypes.groupID = invGroups.groupID
                    -- Link the blueprint to it's manufactured product
                    LEFT JOIN industryActivityProducts iap ON iap.typeID = invTypes.typeID AND iap.activityID = 1
                    -- Get the description for the manufactured product
                    LEFT JOIN invTypes product ON iap.productTypeID = product.typeID
                    LEFT JOIN industryActivity ON industryActivity.typeID = invTypes.TypeID AND industryActivity.activityID = 1
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
                            GroupName = reader.GetString(3),
                            ProductTypeId = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
                            ProductQuantity = reader.IsDBNull(5) ? 1 : reader.GetInt32(5),
                            BaseProductionTime = reader.IsDBNull(6) ? 0 : reader.GetInt32(6)
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
            client.Timeout = TimeSpan.FromMinutes(10);
            
            progress?.Report("Connecting to Fuzzwork...");
            
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

                    int updateCounter = 0;
                    while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, read);
                        totalRead += read;
                        updateCounter++;
                        
                        // Only update the UI every 10 chunks to avoid excessive UI updates
                        if (updateCounter % 10 == 0)
                        {
                            if (!totalBytes.HasValue) continue;
                            
                            var percentage = (double)totalRead / totalBytes.Value * 100;
                            progress?.Report($"Downloading EVE Static Data: {percentage:F1}% ({totalRead / 1024 / 1024}MB / {totalBytes.Value / 1024 / 1024}MB");
                        }
                        
                        await Task.Delay(1);
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

    public async Task<List<Material>> GetMaterialsForBlueprintAsync(Blueprint blueprint)
    {
        var materials = new List<Material>();

        try
        {
            using (var connection = new SqliteConnection($"Data Source={_dbPath};Mode=ReadOnly"))
            {
                await connection.OpenAsync();

                var command = connection.CreateCommand();
                command.CommandText = @"
                    SELECT it.typeID, it.typeName, iam.quantity
                    FROM industryActivityMaterials iam
                    JOIN invTypes it ON iam.materialTypeID = it.typeID
                    WHERE iam.typeID = @blueprintId
                    AND iam.activityID = 1";

                command.Parameters.AddWithValue("@blueprintId", blueprint.TypeId);

                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        materials.Add(new Material(blueprint)
                        {
                            TypeId = reader.GetInt32(0),
                            Name = reader.GetString(1),
                            BaseQuantity = reader.GetInt32(2)
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching materials for blueprint {blueprint.TypeId}: {ex.Message}");
        }
        
        return materials;
    }

    public async Task<decimal> GetJitaSellPriceAsync(int typeId)
    {
        try
        {
            // 10000002 is 'The Forge'
            string url = $"markets/10000002/orders/?order_type=sell&type_id={typeId}";

            var orders = await _httpClient.GetFromJsonAsync<List<MarketOrderResponse>>(url);

            if (orders == null || orders.Count == 0)
                return 0;

            // Find the lowest price (cheapest sell order)
            return orders.Min(o => o.price);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching price for {typeId}: {ex.Message}");
            return 0;
        }
    }
}