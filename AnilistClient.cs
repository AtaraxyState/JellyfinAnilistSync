using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace AnilistClient;

public class AniListClient
{
    private readonly string _accessToken;
    private readonly HttpClient _httpClient;
    private string _lastSearchedName = "";

    public AniListClient(string accessToken)
    {
        _accessToken = accessToken;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_accessToken}");
    }

    public string GetLastSearchedName(string originalName)
    {
        return !string.IsNullOrEmpty(_lastSearchedName) ? _lastSearchedName : CleanAnimeName(originalName);
    }

    public async Task<string> UpdateProgressByAniListIdAsync(int aniListId, int progress, bool autoAddToList = true)
    {
        // Find the media list entry for this AniList media ID
        var findAnimeQuery = new
        {
            query = $@"
                query {{
                    Media(id: {aniListId}) {{
                        id
                        title {{
                            romaji
                        }}
                        mediaListEntry {{
                            id
                            progress
                        }}
                    }}
                }}"
        };

        var findContent = new StringContent(JsonSerializer.Serialize(findAnimeQuery), Encoding.UTF8, "application/json");
        Console.WriteLine($"🔍 Finding media list entry for AniList ID: {aniListId}");
        
        var findResponse = await _httpClient.PostAsync("https://graphql.anilist.co", findContent);
        var findResponseContent = await findResponse.Content.ReadAsStringAsync();
        
        if (!findResponse.IsSuccessStatusCode)
        {
            // Handle rate limiting specifically
            if (findResponse.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                Console.WriteLine("⏱️ Rate limited by AniList. Waiting 10 seconds...");
                await Task.Delay(10000); // Wait 10 seconds
                
                // Retry once
                findResponse = await _httpClient.PostAsync("https://graphql.anilist.co", findContent);
                findResponseContent = await findResponse.Content.ReadAsStringAsync();
                
                if (!findResponse.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"Failed to find media after retry: {findResponse.StatusCode} - {findResponseContent}");
                }
            }
            else
            {
                throw new HttpRequestException($"Failed to find media: {findResponse.StatusCode} - {findResponseContent}");
            }
        }

        Console.WriteLine($"📡 Find media response: {findResponseContent}");
        
        // Parse the response to get the media list entry ID
        using var doc = JsonDocument.Parse(findResponseContent);
        var media = doc.RootElement.GetProperty("data").GetProperty("Media");
        
        int mediaListEntryId;
        var animeTitle = media.GetProperty("title").GetProperty("romaji").GetString();
        
        if (!media.TryGetProperty("mediaListEntry", out var mediaListEntry) || mediaListEntry.ValueKind == JsonValueKind.Null)
        {
            if (!autoAddToList)
            {
                throw new InvalidOperationException($"Anime '{animeTitle}' not in your AniList and auto-add is disabled. Please add it manually or enable auto-add.");
            }

            // Anime not in list - add it first
            Console.WriteLine($"📋 Anime '{animeTitle}' not in your list. Adding it first...");
            
            var addToListQuery = new
            {
                query = $@"
                    mutation {{
                        SaveMediaListEntry(
                            mediaId: {aniListId},
                            status: CURRENT,
                            progress: {progress}
                        ) {{
                            id
                            progress
                            media {{
                                title {{
                                    romaji
                                }}
                            }}
                        }}
                    }}"
            };

            var addContent = new StringContent(JsonSerializer.Serialize(addToListQuery), Encoding.UTF8, "application/json");
            Console.WriteLine($"🌐 Adding anime to list with progress {progress}");

            var addResponse = await _httpClient.PostAsync("https://graphql.anilist.co", addContent);
            var addResponseContent = await addResponse.Content.ReadAsStringAsync();
            
            Console.WriteLine($"📡 Add to list response ({addResponse.StatusCode}): {addResponseContent}");
            
            if (!addResponse.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"Failed to add anime to list: {addResponse.StatusCode} - {addResponseContent}");
            }

            // Parse the response to get the new entry ID
            using var addDoc = JsonDocument.Parse(addResponseContent);
            var savedEntry = addDoc.RootElement.GetProperty("data").GetProperty("SaveMediaListEntry");
            mediaListEntryId = savedEntry.GetProperty("id").GetInt32();
            
            Console.WriteLine($"✅ Successfully added '{animeTitle}' to your list with progress {progress}");
            return addResponseContent;
        }
        else
        {
            mediaListEntryId = mediaListEntry.GetProperty("id").GetInt32();
            Console.WriteLine($"📋 Found anime: {animeTitle}");
            Console.WriteLine($"📋 Media list entry ID: {mediaListEntryId}");
        }

        // Now update the progress
        var updateQuery = new
        {
            query = $@"
                mutation {{
                    SaveMediaListEntry(
                        id: {mediaListEntryId},
                        progress: {progress}
                    ) {{
                        id
                        progress
                        media {{
                            title {{
                                romaji
                            }}
                        }}
                    }}
                }}"
        };

        var updateContent = new StringContent(JsonSerializer.Serialize(updateQuery), Encoding.UTF8, "application/json");
        Console.WriteLine($"🌐 Updating progress to {progress}");

                    var updateResponse = await _httpClient.PostAsync("https://graphql.anilist.co", updateContent);
            var updateResponseContent = await updateResponse.Content.ReadAsStringAsync();
            
            Console.WriteLine($"📡 Update response ({updateResponse.StatusCode}): {updateResponseContent}");
            
            if (!updateResponse.IsSuccessStatusCode)
            {
                // Handle rate limiting for update requests too
                if (updateResponse.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    Console.WriteLine("⏱️ Rate limited during update. Waiting 10 seconds...");
                    await Task.Delay(10000); // Wait 10 seconds
                    
                    // Retry once
                    updateResponse = await _httpClient.PostAsync("https://graphql.anilist.co", updateContent);
                    updateResponseContent = await updateResponse.Content.ReadAsStringAsync();
                    
                    if (!updateResponse.IsSuccessStatusCode)
                    {
                        throw new HttpRequestException($"AniList API error after retry: {updateResponse.StatusCode} - {updateResponseContent}");
                    }
                }
                else
                {
                    throw new HttpRequestException($"AniList API error: {updateResponse.StatusCode} - {updateResponseContent}");
                }
            }

        return updateResponseContent;
    }

    public async Task<int?> SearchAnimeByNameAsync(string animeName, string? premiereDate = null)
    {
        // Clean up the name for better search results
        var searchName = CleanAnimeName(animeName);
        _lastSearchedName = searchName; // Track for missing series logging
        
        // Extract year from premiere date if available
        int? year = null;
        if (!string.IsNullOrEmpty(premiereDate) && DateTime.TryParse(premiereDate, out var date))
        {
            year = date.Year;
        }

        var searchQuery = new
        {
            query = $@"
                query {{
                    Page(page: 1, perPage: 10) {{
                        media(search: ""{searchName}"", type: ANIME{(year.HasValue ? $", seasonYear: {year}" : "")}) {{
                            id
                            title {{
                                romaji
                                english
                                native
                            }}
                            startDate {{
                                year
                            }}
                            format
                            status
                        }}
                    }}
                }}"
        };

        var content = new StringContent(JsonSerializer.Serialize(searchQuery), Encoding.UTF8, "application/json");
        Console.WriteLine($"🔍 Searching AniList for: '{searchName}'{(year.HasValue ? $" ({year})" : "")}");

        var response = await _httpClient.PostAsync("https://graphql.anilist.co", content);
        var responseContent = await response.Content.ReadAsStringAsync();

        // Handle rate limiting
        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                Console.WriteLine("⏱️ Rate limited during search. Waiting 10 seconds...");
                await Task.Delay(10000);
                
                // Retry once
                response = await _httpClient.PostAsync("https://graphql.anilist.co", content);
                responseContent = await response.Content.ReadAsStringAsync();
                
                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"❌ Search failed after retry: {response.StatusCode}");
                    return null;
                }
            }
            else
            {
                Console.WriteLine($"❌ Search failed: {response.StatusCode} - {responseContent}");
                return null;
            }
        }

        using var doc = JsonDocument.Parse(responseContent);
        var page = doc.RootElement.GetProperty("data").GetProperty("Page");
        var media = page.GetProperty("media");

        if (media.GetArrayLength() == 0)
        {
            Console.WriteLine($"❌ No search results found for '{searchName}'");
            return null;
        }

        // Find the best match
        foreach (var item in media.EnumerateArray())
        {
            var id = item.GetProperty("id").GetInt32();
            var titles = item.GetProperty("title");
            var romajiTitle = titles.TryGetProperty("romaji", out var romaji) ? romaji.GetString() : "";
            var englishTitle = titles.TryGetProperty("english", out var english) ? english.GetString() : "";
            
            // Get year for better matching
            var itemYear = 0;
            if (item.TryGetProperty("startDate", out var startDate) && 
                startDate.TryGetProperty("year", out var yearProp) && 
                yearProp.ValueKind == JsonValueKind.Number)
            {
                itemYear = yearProp.GetInt32();
            }

            Console.WriteLine($"📋 Found: '{romajiTitle}' / '{englishTitle}' ({itemYear}) - ID: {id}");

            // Simple name matching - take the first result for now
            // TODO: Implement more sophisticated matching logic
            if (IsGoodMatch(searchName, romajiTitle, englishTitle, year, itemYear))
            {
                Console.WriteLine($"✅ Selected match: '{romajiTitle}' - ID: {id}");
                return id;
            }
        }

        // If no perfect match, return the first result
        var firstResult = media[0];
        var firstId = firstResult.GetProperty("id").GetInt32();
        var firstTitle = firstResult.GetProperty("title").GetProperty("romaji").GetString();
        
        Console.WriteLine($"🤔 No perfect match found. Using first result: '{firstTitle}' - ID: {firstId}");
        return firstId;
    }

    private static string CleanAnimeName(string name)
    {
        // Remove common suffixes that might interfere with search
        var cleanName = name;
        
        // Remove language indicators
        var patterns = new[]
        {
            @"\s*\(English Dub\)$",
            @"\s*\(Dub\)$", 
            @"\s*\(Sub\)$",
            @"\s*\(Subbed\)$",
            @"\s*\(Dubbed\)$",
            @"\s*\([^)]*Sub[^)]*\)$",
            @"\s*\([^)]*Dub[^)]*\)$",
            @"\s*-\s*.*$", // Remove everything after dash (like "- La légende des Familias")
            @"\s*Season\s+\d+$", // Remove "Season X"
            @"\s*S\d+$", // Remove "S1", "S2", etc.
        };

        foreach (var pattern in patterns)
        {
            cleanName = System.Text.RegularExpressions.Regex.Replace(cleanName, pattern, "", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        return cleanName.Trim();
    }

    private static bool IsGoodMatch(string searchName, string? romajiTitle, string? englishTitle, int? searchYear, int itemYear)
    {
        // Simple matching logic - can be improved
        var titles = new[] { romajiTitle, englishTitle }.Where(t => !string.IsNullOrEmpty(t));
        
        foreach (var title in titles)
        {
            if (string.Equals(searchName, title, StringComparison.OrdinalIgnoreCase))
            {
                // Exact title match
                if (!searchYear.HasValue || Math.Abs(searchYear.Value - itemYear) <= 1)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
