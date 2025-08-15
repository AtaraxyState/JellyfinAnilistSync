using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace JellyfinApi;

public class JellyfinClient
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;

    public JellyfinClient(string baseUrl, string apiKey)
    {
        _baseUrl = baseUrl;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("X-Emby-Token", apiKey);
    }

    public async Task<string?> GetProviderFromSeriesAsync(string seriesId, string providerName)
    {
        var url = $"{_baseUrl}/Items?ids={seriesId}&IncludeItemTypes=Series&Fields=ProviderIds,RecursiveItemCount&limit=100&StartIndex=0";
        
        Console.WriteLine($"🔍 Querying Jellyfin API: {url}");
        
        var response = await _httpClient.GetAsync(url);
        
        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"❌ Jellyfin API error: {response.StatusCode}");
            return null;
        }

        var content = await response.Content.ReadAsStringAsync();
        Console.WriteLine($"📡 Jellyfin response: {content}");
        
        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;
        
        if (!root.TryGetProperty("TotalRecordCount", out var totalCount) || totalCount.GetInt32() != 1)
        {
            Console.WriteLine($"❌ Expected 1 series, got {totalCount.GetInt32()}");
            return null;
        }
        
        var items = root.GetProperty("Items");
        if (items.GetArrayLength() == 0)
        {
            Console.WriteLine("❌ No items found");
            return null;
        }
        
        var firstItem = items[0];
        if (!firstItem.TryGetProperty("ProviderIds", out var providerIds))
        {
            Console.WriteLine("❌ No ProviderIds found");
            return null;
        }
        
        // Search for the provider (case-insensitive)
        foreach (var provider in providerIds.EnumerateObject())
        {
            if (string.Equals(provider.Name, providerName, StringComparison.OrdinalIgnoreCase))
            {
                var providerId = provider.Value.GetString();
                Console.WriteLine($"✅ Found {providerName} ID: {providerId}");
                return providerId;
            }
        }
        
        Console.WriteLine($"❌ {providerName} provider not found");
        return null;
    }

    public async Task<List<SeriesInfo>> GetAllSeriesFromLibraryAsync(string libraryId)
    {
        var url = $"{_baseUrl}/Items?ParentId={libraryId}&IncludeItemTypes=Series&Fields=ProviderIds,RecursiveItemCount,PremiereDate,EndDate&Recursive=true&limit=1000&StartIndex=0";
        
        Console.WriteLine($"🔍 Querying all series from library: {libraryId}");
        
        var response = await _httpClient.GetAsync(url);
        
        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"❌ Jellyfin API error: {response.StatusCode}");
            throw new HttpRequestException($"Failed to get library series: {response.StatusCode}");
        }

        var content = await response.Content.ReadAsStringAsync();
        Console.WriteLine($"📡 Found series in library");
        
        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;
        
        var seriesList = new List<SeriesInfo>();
        
        if (!root.TryGetProperty("Items", out var items))
        {
            Console.WriteLine("❌ No Items found in response");
            return seriesList;
        }
        
        foreach (var item in items.EnumerateArray())
        {
            var series = new SeriesInfo
            {
                Id = GetStringFromJson(item, "Id"),
                Name = GetStringFromJson(item, "Name"),
                PremiereDate = GetStringFromJson(item, "PremiereDate"),
                EndDate = GetStringFromJson(item, "EndDate"),
                ProviderIds = new Dictionary<string, string>()
            };
            
            if (item.TryGetProperty("ProviderIds", out var providerIds))
            {
                foreach (var provider in providerIds.EnumerateObject())
                {
                    series.ProviderIds[provider.Name] = provider.Value.GetString() ?? "";
                }
            }
            
            seriesList.Add(series);
        }
        
        Console.WriteLine($"✅ Found {seriesList.Count} series in library");
        return seriesList;
    }

    public async Task<List<LibraryInfo>> GetLibrariesAsync()
    {
        var url = $"{_baseUrl}/Library/VirtualFolders";
        
        Console.WriteLine($"🔍 Getting all libraries");
        
        var response = await _httpClient.GetAsync(url);
        
        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"❌ Jellyfin API error: {response.StatusCode}");
            throw new HttpRequestException($"Failed to get libraries: {response.StatusCode}");
        }

        var content = await response.Content.ReadAsStringAsync();
        
        using var doc = JsonDocument.Parse(content);
        var libraries = new List<LibraryInfo>();
        
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var library = new LibraryInfo
            {
                Name = GetStringFromJson(item, "Name"),
                CollectionType = GetStringFromJson(item, "CollectionType"),
                ItemId = GetStringFromJson(item, "ItemId")
            };
            
            libraries.Add(library);
        }
        
        Console.WriteLine($"✅ Found {libraries.Count} libraries");
        return libraries;
    }

    public async Task<EpisodeProgress?> GetLastWatchedEpisodeAsync(string seriesId, string userId)
    {
        var url = $"{_baseUrl}/Shows/{seriesId}/Episodes?UserId={userId}&Fields=UserData&limit=1000";
        
        Console.WriteLine($"🔍 Getting episodes for series: {seriesId}");
        
        var response = await _httpClient.GetAsync(url);
        
        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"❌ Jellyfin API error: {response.StatusCode}");
            throw new HttpRequestException($"Failed to get episodes: {response.StatusCode}");
        }

        var content = await response.Content.ReadAsStringAsync();
        
        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;
        
        if (!root.TryGetProperty("Items", out var items))
        {
            Console.WriteLine("❌ No episodes found");
            return null;
        }

        EpisodeProgress? lastWatched = null;
        
        foreach (var item in items.EnumerateArray())
        {
            var episodeNumber = item.TryGetProperty("IndexNumber", out var indexProp) ? indexProp.GetInt32() : 0;
            var seasonNumber = item.TryGetProperty("ParentIndexNumber", out var seasonProp) ? seasonProp.GetInt32() : 0;
            var episodeName = GetStringFromJson(item, "Name");
            var episodeId = GetStringFromJson(item, "Id");
            
            // Check if this episode has been watched
            if (item.TryGetProperty("UserData", out var userData) && 
                userData.TryGetProperty("Played", out var playedProp) && 
                playedProp.GetBoolean())
            {
                var episode = new EpisodeProgress
                {
                    EpisodeId = episodeId,
                    EpisodeName = episodeName,
                    SeasonNumber = seasonNumber,
                    EpisodeNumber = episodeNumber,
                    IsPlayed = true
                };
                
                // Keep track of the highest watched episode
                if (lastWatched == null || 
                    seasonNumber > lastWatched.SeasonNumber ||
                    (seasonNumber == lastWatched.SeasonNumber && episodeNumber > lastWatched.EpisodeNumber))
                {
                    lastWatched = episode;
                }
            }
        }
        
        if (lastWatched != null)
        {
            Console.WriteLine($"✅ Last watched: S{lastWatched.SeasonNumber:D2}E{lastWatched.EpisodeNumber:D2} - {lastWatched.EpisodeName}");
        }
        else
        {
            Console.WriteLine("❌ No watched episodes found");
        }
        
        return lastWatched;
    }

    public async Task<List<EpisodeProgress>> GetAllEpisodesProgressAsync(string seriesId, string userId)
    {
        var url = $"{_baseUrl}/Shows/{seriesId}/Episodes?UserId={userId}&Fields=UserData&limit=1000";
        
        Console.WriteLine($"🔍 Getting all episodes progress for series: {seriesId}");
        
        var response = await _httpClient.GetAsync(url);
        
        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"❌ Jellyfin API error: {response.StatusCode}");
            throw new HttpRequestException($"Failed to get episodes: {response.StatusCode}");
        }

        var content = await response.Content.ReadAsStringAsync();
        
        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;
        
        var episodes = new List<EpisodeProgress>();
        
        if (!root.TryGetProperty("Items", out var items))
        {
            Console.WriteLine("❌ No episodes found");
            return episodes;
        }
        
        foreach (var item in items.EnumerateArray())
        {
            var episodeNumber = item.TryGetProperty("IndexNumber", out var indexProp) ? indexProp.GetInt32() : 0;
            var seasonNumber = item.TryGetProperty("ParentIndexNumber", out var seasonProp) ? seasonProp.GetInt32() : 0;
            var episodeName = GetStringFromJson(item, "Name");
            var episodeId = GetStringFromJson(item, "Id");
            
            bool isPlayed = false;
            if (item.TryGetProperty("UserData", out var userData) && 
                userData.TryGetProperty("Played", out var playedProp))
            {
                isPlayed = playedProp.GetBoolean();
            }
            
            var episode = new EpisodeProgress
            {
                EpisodeId = episodeId,
                EpisodeName = episodeName,
                SeasonNumber = seasonNumber,
                EpisodeNumber = episodeNumber,
                IsPlayed = isPlayed
            };
            
            episodes.Add(episode);
        }
        
        // Sort by season and episode number
        episodes.Sort((a, b) => 
        {
            int seasonCompare = a.SeasonNumber.CompareTo(b.SeasonNumber);
            return seasonCompare != 0 ? seasonCompare : a.EpisodeNumber.CompareTo(b.EpisodeNumber);
        });
        
        var watchedCount = episodes.Count(e => e.IsPlayed);
        Console.WriteLine($"✅ Found {episodes.Count} episodes, {watchedCount} watched");
        
        return episodes;
    }

    public async Task<SyncResult> SyncSeriesProgressToAniListAsync(string seriesId, string jellyfinUserId, AnilistClient.AniListClient aniListClient, bool autoAddToList = true)
    {
        var result = new SyncResult { SeriesId = seriesId };
        
        try
        {
            Console.WriteLine($"🔄 Starting sync for series: {seriesId}");
            
            // Step 1: Get series info from Jellyfin first
            var url = $"{_baseUrl}/Items?ids={seriesId}&IncludeItemTypes=Series&Fields=ProviderIds,Name,PremiereDate&limit=1";
            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                result.Status = SyncStatus.Error;
                result.Message = $"Failed to get series info: {response.StatusCode}";
                return result;
            }

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);
            var items = doc.RootElement.GetProperty("Items");
            if (items.GetArrayLength() == 0)
            {
                result.Status = SyncStatus.Error;
                result.Message = "Series not found in Jellyfin";
                return result;
            }

            var seriesInfo = items[0];
            var seriesName = GetStringFromJson(seriesInfo, "Name");
            var premiereDate = GetStringFromJson(seriesInfo, "PremiereDate");
            
            // Try to get AniList provider ID first
            int aniListIdInt = 0;
            var aniListId = "";
            
            if (seriesInfo.TryGetProperty("ProviderIds", out var providerIds) && 
                providerIds.TryGetProperty("AniList", out var aniListProp))
            {
                aniListId = aniListProp.GetString() ?? "";
                if (int.TryParse(aniListId, out aniListIdInt))
                {
                    Console.WriteLine($"✅ Found AniList ID from provider: {aniListIdInt}");
                }
            }
            
            // If no provider ID, try searching by name
            if (aniListIdInt == 0)
            {
                Console.WriteLine($"🔍 No AniList provider ID found. Searching by name: '{seriesName}'");
                
                var searchResult = await aniListClient.SearchAnimeByNameAsync(seriesName, premiereDate);
                if (searchResult != null)
                {
                    aniListIdInt = searchResult.Value;
                    Console.WriteLine($"✅ Found AniList ID via search: {aniListIdInt}");
                }
                else
                {
                    // Track this missing series
                    var missingSeries = new JellyfinAnilistSync.MissingSeries
                    {
                        JellyfinId = seriesId,
                        Name = seriesName,
                        PremiereDate = premiereDate,
                        SearchedName = aniListClient.GetLastSearchedName(seriesName),
                        Reason = string.IsNullOrEmpty(aniListId) ? "No AniList provider ID and search failed" : "AniList search by name failed"
                    };
                    
                    // Extract other provider IDs for reference
                    if (seriesInfo.TryGetProperty("ProviderIds", out var allProviders))
                    {
                        missingSeries.TmdbId = GetStringFromJson(allProviders, "Tmdb");
                        missingSeries.ImdbId = GetStringFromJson(allProviders, "Imdb");
                        missingSeries.TvdbId = GetStringFromJson(allProviders, "Tvdb");
                    }
                    
                    JellyfinAnilistSync.ConfigurationManager.AddMissingSeries(missingSeries);
                    
                    result.Status = SyncStatus.NoAniListId;
                    result.Message = $"No AniList ID found and name search failed for '{seriesName}'";
                    Console.WriteLine($"❌ {result.Message}");
                    return result;
                }
            }
            
            result.AniListId = aniListIdInt;
            Console.WriteLine($"✅ Found AniList ID: {aniListIdInt}");
            
            // Step 2: Get last watched episode from Jellyfin
            var lastWatched = await GetLastWatchedEpisodeAsync(seriesId, jellyfinUserId);
            
            int progressToSet = 0;
            if (lastWatched == null)
            {
                Console.WriteLine("📺 No episodes watched - setting progress to 0");
                result.LastWatchedEpisode = 0;
                result.LastWatchedSeason = 0;
                progressToSet = 0;
            }
            else
            {
                result.LastWatchedEpisode = lastWatched.EpisodeNumber;
                result.LastWatchedSeason = lastWatched.SeasonNumber;
                progressToSet = lastWatched.EpisodeNumber;
                Console.WriteLine($"✅ Last watched: S{lastWatched.SeasonNumber:D2}E{lastWatched.EpisodeNumber:D2} - {lastWatched.EpisodeName}");
            }
            
            // Step 3: Update AniList progress (will add to list if needed)
            var updateResponse = await aniListClient.UpdateProgressByAniListIdAsync(aniListIdInt, progressToSet, autoAddToList);
            
            // Set appropriate status based on how we found the AniList ID
            result.Status = !string.IsNullOrEmpty(aniListId) ? SyncStatus.Success : SyncStatus.SuccessViaSearch;
            result.Message = $"Successfully synced progress to episode {progressToSet}";
            result.AniListResponse = updateResponse;
            
            Console.WriteLine($"✅ Sync completed successfully!");
            return result;
        }
        catch (Exception ex)
        {
            result.Status = SyncStatus.Error;
            result.Message = $"Sync failed: {ex.Message}";
            result.Error = ex;
            Console.WriteLine($"❌ Sync failed: {ex.Message}");
            return result;
        }
    }

    public async Task<List<SyncResult>> SyncAllSeriesInLibraryAsync(string libraryId, string jellyfinUserId, AnilistClient.AniListClient aniListClient, bool autoAddToList = true)
    {
        var results = new List<SyncResult>();
        
        Console.WriteLine($"🚀 Starting bulk sync for library: {libraryId}");
        
        try
        {
            // Get all series in the library
            var allSeries = await GetAllSeriesFromLibraryAsync(libraryId);
            Console.WriteLine($"📚 Found {allSeries.Count} series in library");
            
            foreach (var series in allSeries)
            {
                Console.WriteLine($"\n📺 Processing: {series.Name}");
                
                var result = await SyncSeriesProgressToAniListAsync(series.Id, jellyfinUserId, aniListClient, autoAddToList);
                result.SeriesName = series.Name;
                results.Add(result);
                
                // Longer delay to avoid rate limiting
                await Task.Delay(2000); // 2 seconds between requests
            }
            
            // Summary
            var successful = results.Count(r => r.Status == SyncStatus.Success);
            var successViaSearch = results.Count(r => r.Status == SyncStatus.SuccessViaSearch);
            var failed = results.Count(r => r.Status == SyncStatus.Error);
            var noAniListId = results.Count(r => r.Status == SyncStatus.NoAniListId);
            
            Console.WriteLine($"\n📊 Bulk sync completed:");
            Console.WriteLine($"   ✅ Successful (provider ID): {successful}");
            Console.WriteLine($"   🔍 Successful (via search): {successViaSearch}");
            Console.WriteLine($"   ❌ Failed: {failed}");
            Console.WriteLine($"   🆔 No AniList found: {noAniListId}");
            
            // Show details for series without AniList IDs
            if (noAniListId > 0)
            {
                Console.WriteLine($"\n🆔 Series missing AniList provider IDs:");
                foreach (var result in results.Where(r => r.Status == SyncStatus.NoAniListId))
                {
                    Console.WriteLine($"   - {result.SeriesName}");
                }
                Console.WriteLine($"\n💡 To fix: Add AniList provider IDs to these series in Jellyfin metadata");
            }
            
            // Show details for failed syncs
            if (failed > 0)
            {
                Console.WriteLine($"\n❌ Failed syncs:");
                foreach (var result in results.Where(r => r.Status == SyncStatus.Error))
                {
                    Console.WriteLine($"   - {result.SeriesName}: {result.Message}");
                }
            }
            
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Bulk sync failed: {ex.Message}");
        }
        
        return results;
    }

    private static string GetStringFromJson(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var prop) ? prop.GetString() ?? "" : "";
    }
}

public class SeriesInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string PremiereDate { get; set; } = "";
    public string EndDate { get; set; } = "";
    public Dictionary<string, string> ProviderIds { get; set; } = new();
}

public class LibraryInfo
{
    public string Name { get; set; } = "";
    public string CollectionType { get; set; } = "";
    public string ItemId { get; set; } = "";
}

public class EpisodeProgress
{
    public string EpisodeId { get; set; } = "";
    public string EpisodeName { get; set; } = "";
    public int SeasonNumber { get; set; }
    public int EpisodeNumber { get; set; }
    public bool IsPlayed { get; set; }
}

public class SyncResult
{
    public string SeriesId { get; set; } = "";
    public string SeriesName { get; set; } = "";
    public int? AniListId { get; set; }
    public int LastWatchedEpisode { get; set; }
    public int LastWatchedSeason { get; set; }
    public SyncStatus Status { get; set; }
    public string Message { get; set; } = "";
    public string? AniListResponse { get; set; }
    public Exception? Error { get; set; }
}

public enum SyncStatus
{
    Success,
    SuccessViaSearch,  // Found via name search instead of provider ID
    NoAniListId,
    NoProgress,
    Error
}
