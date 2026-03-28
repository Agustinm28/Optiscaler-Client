using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using OptiscalerClient.Views;
using OptiscalerClient.Models;

namespace OptiscalerClient.Services;

public class GameMetadataService
{
    private static readonly HttpClient SharedHttpClient = CreateHttpClient();
    private readonly HttpClient _httpClient;
    private readonly string _coversCachePath;
    private readonly ComponentManagementService? _componentService;

    public GameMetadataService(ComponentManagementService? componentService = null)
    {
        _httpClient = SharedHttpClient;
        _componentService = componentService;
        
        // Caching covers in AppData
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _coversCachePath = Path.Combine(appData, "OptiscalerClient", "Covers");
        
        if (!Directory.Exists(_coversCachePath))
        {
            Directory.CreateDirectory(_coversCachePath);
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.Add("User-Agent", "OptiscalerClient/1.0");
        return client;
    }

    /// <summary>
    /// Searches for game cover art using multiple sources with fallback.
    /// Priority: 1) Cache, 2) Steam API (with AppId if available), 3) SteamGridDB
    /// </summary>
    public async Task<string?> FetchAndCacheCoverImageAsync(string gameName, string appIdKey)
    {
        // Check if we already have it in cache
        string cacheFileName = $"{SanitizeFileName(appIdKey)}.jpg";
        string localPath = Path.Combine(_coversCachePath, cacheFileName);

        if (File.Exists(localPath))
        {
            DebugWindow.Log(() => $"[Cover] Using cached cover for: {gameName}");
            return localPath;
        }

        DebugWindow.Log(() => $"[Cover] Fetching cover for: {gameName} (AppId: {appIdKey})");

        // Try 1: If appIdKey is a numeric Steam AppId, use it directly
        if (int.TryParse(appIdKey, out int steamAppId))
        {
            var result = await TryDownloadSteamCoverByAppId(steamAppId, localPath, gameName);
            if (result != null) return result;
        }

        // Try 2: Search Steam Store API by name with improved matching
        var steamResult = await TryFetchFromSteamSearch(gameName, localPath);
        if (steamResult != null) return steamResult;

        // Try 3: Fallback to SteamGridDB
        var gridDbResult = await TryFetchFromSteamGridDB(gameName, localPath);
        if (gridDbResult != null) return gridDbResult;

        DebugWindow.Log(() => $"[Cover] No cover found for: {gameName}");
        return null;
    }

    private async Task<string?> TryDownloadSteamCoverByAppId(int appId, string localPath, string gameName)
    {
        try
        {
            string remoteUrl = $"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{appId}/library_600x900_2x.jpg";
            
            var imgBytes = await _httpClient.GetByteArrayAsync(remoteUrl);
            await File.WriteAllBytesAsync(localPath, imgBytes);
            
            DebugWindow.Log(() => $"[Cover] Downloaded from Steam AppId {appId}: {gameName}");
            return localPath;
        }
        catch
        {
            DebugWindow.Log(() => $"[Cover] Failed to download from Steam AppId {appId}");
            return null;
        }
    }

    private async Task<string?> TryFetchFromSteamSearch(string gameName, string localPath)
    {
        try
        {
            string cleanName = CleanGameName(gameName);
            string queryName = Uri.EscapeDataString(cleanName);
            string url = $"https://store.steampowered.com/api/storesearch/?term={queryName}&l=english&cc=US";

            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                DebugWindow.Log(() => $"[Cover] Steam search API returned: {response.StatusCode}");
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            var root = doc.RootElement;
            if (root.TryGetProperty("total", out var totalEl) && totalEl.GetInt32() > 0)
            {
                if (root.TryGetProperty("items", out var items) && items.GetArrayLength() > 0)
                {
                    // Try to find the best match instead of just taking the first result
                    var bestMatch = FindBestMatch(items, cleanName);
                    if (bestMatch.HasValue && bestMatch.Value.TryGetProperty("id", out var idEl))
                    {
                        int actualAppId = idEl.GetInt32();
                        string matchedName = bestMatch.Value.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "";
                        
                        string remoteUrl = $"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{actualAppId}/library_600x900_2x.jpg";
                        
                        try
                        {
                            var imgResponse = await _httpClient.GetAsync(remoteUrl);
                            if (imgResponse.IsSuccessStatusCode)
                            {
                                var imgBytes = await imgResponse.Content.ReadAsByteArrayAsync();
                                await File.WriteAllBytesAsync(localPath, imgBytes);
                                
                                DebugWindow.Log(() => $"[Cover] Downloaded from Steam search: {matchedName} (AppId: {actualAppId})");
                                return localPath;
                            }
                            else
                            {
                                DebugWindow.Log(() => $"[Cover] Steam image not found for AppId {actualAppId}: {imgResponse.StatusCode}");
                            }
                        }
                        catch (Exception imgEx)
                        {
                            DebugWindow.Log(() => $"[Cover] Failed to download Steam image: {imgEx.Message}");
                        }
                    }
                }
            }
            else
            {
                DebugWindow.Log(() => $"[Cover] No Steam search results for: {cleanName}");
            }
        }
        catch (Exception ex)
        {
            DebugWindow.Log(() => $"[Cover] Steam search failed: {ex.Message}");
        }

        return null;
    }

    private async Task<string?> TryFetchFromSteamGridDB(string gameName, string localPath)
    {
        // Get API key from user configuration
        string? apiKey = _componentService?.Config?.SteamGridDBApiKey;
        
        DebugWindow.Log(() => $"[Cover] Checking SteamGridDB/RAWG fallback (API key configured: {!string.IsNullOrEmpty(apiKey)})");
        
        if (string.IsNullOrEmpty(apiKey))
        {
            // Try public endpoint without API key (limited functionality)
            DebugWindow.Log("[Cover] No SteamGridDB API key, trying RAWG API...");
            return await TryFetchFromSteamGridDBPublic(gameName, localPath);
        }

        try
        {
            string cleanName = CleanGameName(gameName);
            string queryName = Uri.EscapeDataString(cleanName);
            string searchUrl = $"https://www.steamgriddb.com/api/v2/search/autocomplete/{queryName}";

            var request = new HttpRequestMessage(HttpMethod.Get, searchUrl);
            request.Headers.Add("Authorization", $"Bearer {apiKey}");

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("data", out var data) && data.GetArrayLength() > 0)
            {
                var firstGame = data[0];
                if (firstGame.TryGetProperty("id", out var gameId))
                {
                    int gridGameId = gameId.GetInt32();
                    
                    // Get grid images for this game
                    string gridsUrl = $"https://www.steamgriddb.com/api/v2/grids/game/{gridGameId}";
                    var gridsRequest = new HttpRequestMessage(HttpMethod.Get, gridsUrl);
                    gridsRequest.Headers.Add("Authorization", $"Bearer {apiKey}");
                    
                    var gridsResponse = await _httpClient.SendAsync(gridsRequest);
                    if (gridsResponse.IsSuccessStatusCode)
                    {
                        var gridsJson = await gridsResponse.Content.ReadAsStringAsync();
                        using var gridsDoc = JsonDocument.Parse(gridsJson);
                        
                        if (gridsDoc.RootElement.TryGetProperty("data", out var grids) && grids.GetArrayLength() > 0)
                        {
                            // Get first vertical grid (600x900 preferred)
                            var grid = grids[0];
                            if (grid.TryGetProperty("url", out var urlEl))
                            {
                                string imageUrl = urlEl.GetString() ?? "";
                                var imgBytes = await _httpClient.GetByteArrayAsync(imageUrl);
                                await File.WriteAllBytesAsync(localPath, imgBytes);
                                
                                DebugWindow.Log(() => $"[Cover] Downloaded from SteamGridDB: {gameName}");
                                return localPath;
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            DebugWindow.Log(() => $"[Cover] SteamGridDB failed: {ex.Message}");
        }

        return null;
    }

    private async Task<string?> TryFetchFromSteamGridDBPublic(string gameName, string localPath)
    {
        // When SteamGridDB API key is not configured, try alternative Steam sources
        DebugWindow.Log(() => $"[Cover] Trying alternative Steam sources for: {gameName}");
        
        var steamAltResult = await TryAlternativeSteamImages(gameName, localPath);
        if (steamAltResult != null) return steamAltResult;
        
        DebugWindow.Log(() => $"[Cover] All alternative sources exhausted for: {gameName}");
        return null;
    }

    private async Task<string?> TryAlternativeSteamImages(string gameName, string localPath)
    {
        try
        {
            // Try to find the game again in Steam and use alternative image URLs
            string cleanName = CleanGameName(gameName);
            string queryName = Uri.EscapeDataString(cleanName);
            string url = $"https://store.steampowered.com/api/storesearch/?term={queryName}&l=english&cc=US";

            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            var root = doc.RootElement;
            if (root.TryGetProperty("total", out var totalEl) && totalEl.GetInt32() > 0)
            {
                if (root.TryGetProperty("items", out var items) && items.GetArrayLength() > 0)
                {
                    var bestMatch = FindBestMatch(items, cleanName);
                    if (bestMatch.HasValue && bestMatch.Value.TryGetProperty("id", out var idEl))
                    {
                        int appId = idEl.GetInt32();
                        
                        // Try multiple Steam CDN image formats
                        string[] imageUrls = new[]
                        {
                            $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/library_600x900.jpg",
                            $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/header.jpg",
                            $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/capsule_616x353.jpg",
                            $"https://steamcdn-a.akamaihd.net/steam/apps/{appId}/library_600x900.jpg"
                        };

                        foreach (var imageUrl in imageUrls)
                        {
                            try
                            {
                                DebugWindow.Log(() => $"[Cover] Trying alternative Steam CDN: {imageUrl}");
                                var imgResponse = await _httpClient.GetAsync(imageUrl);
                                if (imgResponse.IsSuccessStatusCode)
                                {
                                    var imgBytes = await imgResponse.Content.ReadAsByteArrayAsync();
                                    if (imgBytes.Length > 5000) // Ensure it's a real image
                                    {
                                        await File.WriteAllBytesAsync(localPath, imgBytes);
                                        DebugWindow.Log(() => $"[Cover] Downloaded from alternative Steam CDN: {gameName}");
                                        return localPath;
                                    }
                                }
                            }
                            catch
                            {
                                // Continue to next URL
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            DebugWindow.Log(() => $"[Cover] Alternative Steam images failed: {ex.Message}");
        }

        return null;
    }

    private JsonElement? FindBestMatch(JsonElement items, string searchName)
    {
        var itemsList = items.EnumerateArray().ToList();
        
        // First try: exact match (case insensitive)
        foreach (var item in itemsList)
        {
            if (item.TryGetProperty("name", out var nameEl))
            {
                string itemName = nameEl.GetString() ?? "";
                if (itemName.Equals(searchName, StringComparison.OrdinalIgnoreCase))
                {
                    return item;
                }
            }
        }
        
        // Second try: starts with search name
        foreach (var item in itemsList)
        {
            if (item.TryGetProperty("name", out var nameEl))
            {
                string itemName = nameEl.GetString() ?? "";
                if (itemName.StartsWith(searchName, StringComparison.OrdinalIgnoreCase))
                {
                    return item;
                }
            }
        }
        
        // Fallback: return first item
        return itemsList.Count > 0 ? itemsList[0] : null;
    }

    private string CleanGameName(string gameName)
    {
        // Remove common suffixes and prefixes that might interfere with search
        var cleaned = gameName;
        
        // Remove year suffixes like "(2024)", "- 2024"
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s*[\(\-]\s*\d{4}\s*[\)]?\s*$", "");
        
        // Remove edition suffixes
        var editionPatterns = new[] { "Deluxe", "Ultimate", "Gold", "GOTY", "Complete", "Enhanced", "Remastered", "Definitive" };
        foreach (var pattern in editionPatterns)
        {
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, $@"\s*-?\s*{pattern}\s*(Edition)?\s*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        
        return cleaned.Trim();
    }

    private string SanitizeFileName(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Join("_", fileName.Split(invalid, StringSplitOptions.RemoveEmptyEntries));
    }

    public async Task<string?> FetchCoverImageUrlAsync(string gameName)
    {
        // Legacy method if still used elsewhere
        try
        {
            string queryName = Uri.EscapeDataString(gameName);
            string url = $"https://store.steampowered.com/api/storesearch/?term={queryName}&l=english&cc=US";

            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            var root = doc.RootElement;
            if (root.TryGetProperty("total", out var totalEl) && totalEl.GetInt32() > 0)
            {
                if (root.TryGetProperty("items", out var items) && items.GetArrayLength() > 0)
                {
                    var firstItem = items[0];
                    if (firstItem.TryGetProperty("id", out var idEl))
                    {
                        int appId = idEl.GetInt32();
                        return $"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{appId}/library_600x900_2x.jpg";
                    }
                }
            }
        }
        catch { }
        return null;
    }
}
