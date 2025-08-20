using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using AnilistClient;
using JellyfinApi;
using JellyfinAnilistSync;

var builder = WebApplication.CreateBuilder(args);

// Load configuration from config.json
var config = JellyfinAnilistSync.ConfigurationManager.LoadConfiguration();

// Create Jellyfin client
var jellyfinClient = new JellyfinClient(config.Jellyfin.ServerUrl, config.Jellyfin.ApiKey);

// Create AniList clients dictionary for each user
var aniListClients = new Dictionary<string, AniListClient>();
foreach (var userToken in config.AniList.UserTokens)
{
    var client = new AniListClient(userToken.Value);
    aniListClients[userToken.Key] = client;
    Console.WriteLine($"🎯 Created AniList client for user: {userToken.Key}");
}

// Configure logging for Windows Service
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddEventLog(); // Add Windows Event Log support

// Configure URLs from config
var urls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? $"http://{config.Webhook.Host}:{config.Webhook.Port}";
builder.WebHost.UseUrls(urls);

var app = builder.Build();

// Add logging
var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("Starting JellyfinAnilistSync webhook service on {Urls}", urls);

// Create VideoConversionService for H.265 conversion
var videoConversionService = new VideoConversionService(logger, jellyfinClient, config);

// Webhook handler function
async Task HandleWebhook(HttpContext context)
{
    using var reader = new StreamReader(context.Request.Body);
    var body = await reader.ReadToEndAsync();

    try
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        // Log entire payload using the logger
        // logger.LogInformation("Received webhook: {WebhookData}", root);

        // Check NotificationType field (the main event type from Jellyfin)
        if (root.TryGetProperty("NotificationType", out var notificationTypeProp))
        {
            var notificationType = notificationTypeProp.GetString();
            logger.LogInformation("NotificationType: {NotificationType}", notificationType);
            Console.WriteLine($"📩 Event Type: {notificationType}");

            // Handle different notification types
            switch (notificationType)
            {
                case "AuthenticationSuccess":
                    await HandleAuthenticationSuccess(root, logger);
                    break;
                case "PlaybackStarted":
                case "UserDataSaved":
                    await HandleUserDataSavedAsync(root, logger);
                    break;
                default:
                    logger.LogInformation("Unhandled notification type: {NotificationType}", notificationType);
                    Console.WriteLine($"❓ Unknown event type: {notificationType}");
                    break;
            }
        }
        else
        {
            logger.LogWarning("No 'NotificationType' property found in webhook data");
            Console.WriteLine("⚠️ No NotificationType found in webhook");
        }
    }
    catch (JsonException ex)
    {
        logger.LogError(ex, "Invalid JSON received in webhook: {ErrorMessage}", ex.Message);
    }

    context.Response.StatusCode = 200;
    await context.Response.WriteAsync("OK");
}

// Map webhook endpoints - both /webhook and root /
app.MapPost("/webhook", HandleWebhook);
app.MapPost("/", HandleWebhook);

// Map Sonarr webhook endpoint (only if enabled in config)
if (config.Sonarr?.Enabled == true)
{
    app.MapPost("/sonarr", HandleSonarrWebhook);
    var apiKeyStatus = !string.IsNullOrEmpty(config.Sonarr?.ApiKey) ? "with API key authentication" : "without authentication";
    Console.WriteLine($"🎯 Sonarr webhook endpoint enabled at /sonarr ({apiKeyStatus})");
    logger.LogInformation("Sonarr webhook integration enabled with API key: {HasApiKey}", !string.IsNullOrEmpty(config.Sonarr?.ApiKey));
}
else
{
    Console.WriteLine("💤 Sonarr webhook integration disabled");
    logger.LogInformation("Sonarr webhook integration disabled");
}

// Map conversion status endpoints (only if H.265 conversion is enabled)
if (config.Conversion.AutoConvertToHEVC)
{
    app.MapGet("/conversions", () => videoConversionService.GetActiveConversions());
    app.MapGet("/conversions/{jobId}", (string jobId) => videoConversionService.GetConversionStatus(jobId));
    Console.WriteLine("🎬 H.265 conversion status endpoints enabled at /conversions");
    logger.LogInformation("H.265 conversion status endpoints enabled");
}
else
{
    Console.WriteLine("💤 H.265 conversion status endpoints disabled");
    logger.LogInformation("H.265 conversion status endpoints disabled");
}

// Sonarr webhook handler function
async Task HandleSonarrWebhook(HttpContext context)
{
    // Check API key authentication if configured
    if (!string.IsNullOrEmpty(config.Sonarr?.ApiKey))
    {
        var providedApiKey = context.Request.Headers["X-Api-Key"].FirstOrDefault() ??
                           context.Request.Query["apikey"].FirstOrDefault();
        
        if (string.IsNullOrEmpty(providedApiKey) || providedApiKey != config.Sonarr.ApiKey)
        {
            logger.LogWarning("Sonarr webhook authentication failed - invalid or missing API key");
            Console.WriteLine("❌ Sonarr webhook authentication failed - invalid or missing API key");
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("Unauthorized");
            return;
        }
        
        Console.WriteLine("✅ Sonarr webhook authenticated successfully");
    }
    else
    {
        Console.WriteLine("⚠️ Sonarr webhook authentication disabled (no API key configured)");
    }
    
    using var reader = new StreamReader(context.Request.Body);
    var body = await reader.ReadToEndAsync();
    
    try
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        
        // Log entire Sonarr payload
        logger.LogInformation("Received Sonarr webhook: {SonarrWebhookData}", root);
        
        // Check eventType field (the main event type from Sonarr)
        if (root.TryGetProperty("eventType", out var eventTypeProp))
        {
            var eventType = eventTypeProp.GetString();
            logger.LogInformation("Sonarr EventType: {EventType}", eventType);
            Console.WriteLine($"📺 Sonarr Event Type: {eventType}");
            
            // Handle different Sonarr event types
            switch (eventType)
            {
                case "Download":
                case "ImportComplete":
                    await HandleSonarrImportCompleteAsync(root, logger);
                    break;
                case "Grab":
                    await HandleSonarrGrabAsync(root, logger);
                    break;
                case "Test":
                    Console.WriteLine("🧪 Sonarr test webhook received successfully");
                    logger.LogInformation("Sonarr test webhook received");
                    break;
                default:
                    logger.LogInformation("Unhandled Sonarr event type: {EventType}", eventType);
                    Console.WriteLine($"❓ Unknown Sonarr event type: {eventType}");
                    break;
            }
        }
        else
        {
            logger.LogWarning("No 'eventType' property found in Sonarr webhook data");
            Console.WriteLine("⚠️ No eventType found in Sonarr webhook");
        }
    }
    catch (JsonException ex)
    {
        logger.LogError(ex, "Invalid JSON received in Sonarr webhook: {ErrorMessage}", ex.Message);
        Console.WriteLine($"❌ Invalid JSON in Sonarr webhook: {ex.Message}");
    }
    
    context.Response.StatusCode = 200;
    await context.Response.WriteAsync("OK");
}

// Sonarr handler methods
async Task HandleSonarrImportCompleteAsync(JsonElement root, ILogger logger)
{
    Console.WriteLine("📥 Processing Sonarr ImportComplete/Download event");
    
    // Extract series information
    var seriesTitle = GetNestedStringProperty(root, "series", "title");
    var seriesId = GetNestedIntProperty(root, "series", "id");
    var seriesTvdbId = GetNestedIntProperty(root, "series", "tvdbId");
    var seriesImdbId = GetNestedStringProperty(root, "series", "imdbId");
    
    // Extract episode information
    var episodes = root.TryGetProperty("episodes", out var episodesProp) ? episodesProp : new JsonElement();
    
    Console.WriteLine($"   📺 Series: {seriesTitle} (Sonarr ID: {seriesId})");
    Console.WriteLine($"   🆔 TVDB ID: {seriesTvdbId}");
    Console.WriteLine($"   🆔 IMDB ID: {seriesImdbId}");
    
    if (episodes.ValueKind == JsonValueKind.Array)
    {
        Console.WriteLine($"   📋 Episodes imported:");
        foreach (var episode in episodes.EnumerateArray())
        {
            var seasonNumber = GetIntProperty(episode, "seasonNumber");
            var episodeNumber = GetIntProperty(episode, "episodeNumber");
            var episodeTitle = GetStringProperty(episode, "title");
            var quality = GetNestedStringProperty(episode, "quality", "quality", "name");
            
            Console.WriteLine($"      • S{seasonNumber:D2}E{episodeNumber:D2} - {episodeTitle} ({quality})");
        }
    }
    
    logger.LogInformation("Sonarr import complete: {SeriesTitle} - {EpisodeCount} episodes", 
        seriesTitle, episodes.ValueKind == JsonValueKind.Array ? episodes.GetArrayLength() : 0);
    
                // Refresh Jellyfin series if configured
            if (JellyfinAnilistSync.ConfigurationManager.ShouldRefreshJellyfinOnSonarrImport(config))
            {
                if (seriesTvdbId > 0)
                {
                    Console.WriteLine($"🔄 Refreshing Jellyfin series with TVDB ID {seriesTvdbId}");
                    
                    try
                    {
                        // Find the series in Jellyfin by TVDB ID
                        var jellyfinSeriesId = await jellyfinClient.FindSeriesByTvdbIdAsync(seriesTvdbId);
                        
                        if (!string.IsNullOrEmpty(jellyfinSeriesId))
                        {
                            // Refresh the series metadata to pick up new episodes
                            var refreshSuccess = await jellyfinClient.RefreshSeriesAsync(jellyfinSeriesId);
                            
                            if (refreshSuccess)
                            {
                                Console.WriteLine($"✅ Successfully triggered Jellyfin refresh for {seriesTitle}");
                                logger.LogInformation("Jellyfin series refresh triggered for {SeriesTitle} (TVDB: {TvdbId})", seriesTitle, seriesTvdbId);
                            }
                            else
                            {
                                Console.WriteLine($"❌ Failed to trigger Jellyfin refresh for {seriesTitle}");
                                logger.LogWarning("Failed to trigger Jellyfin refresh for {SeriesTitle} (TVDB: {TvdbId})", seriesTitle, seriesTvdbId);
                            }

                            // Start H.265 conversion if enabled and file path is available
                            if (config.Conversion.AutoConvertToHEVC)
                            {
                                try
                                {
                                                                    // Try to get the file path from the episode information
                                var episodeFilePath = GetEpisodeFilePathFromSonarrWebhook(root);
                                
                                if (!string.IsNullOrEmpty(episodeFilePath))
                                {
                                    Console.WriteLine($"🎬 Starting H.265 conversion check for: {episodeFilePath}");
                                    
                                    // Start conversion asynchronously (won't block other webhook processing)
                                    var conversionStarted = await videoConversionService.StartConversionIfNeededAsync(
                                        episodeFilePath, 
                                        jellyfinSeriesId, 
                                        seriesTitle);
                                    
                                    if (conversionStarted)
                                    {
                                        Console.WriteLine($"🔄 H.265 conversion started for: {seriesTitle}");
                                        logger.LogInformation("H.265 conversion started for {SeriesTitle} - {FilePath}", seriesTitle, episodeFilePath);
                                    }
                                    else
                                    {
                                        Console.WriteLine($"💤 H.265 conversion not needed for: {seriesTitle}");
                                    }
                                }
                                else
                                {
                                    Console.WriteLine($"⚠️ Could not determine file path for H.265 conversion from Sonarr webhook");
                                    logger.LogWarning("Could not determine file path for H.265 conversion from Sonarr webhook for {SeriesTitle}", seriesTitle);
                                    
                                    // Log the webhook structure for debugging
                                    Console.WriteLine("🔍 Debug: Sonarr webhook structure:");
                                    if (root.TryGetProperty("episodeFiles", out var episodeFiles))
                                    {
                                        Console.WriteLine($"   📁 episodeFiles array found with {episodeFiles.GetArrayLength()} items");
                                    }
                                    else
                                    {
                                        Console.WriteLine("   ❌ episodeFiles array not found");
                                    }
                                    
                                    if (root.TryGetProperty("episodes", out var episodesDebug))
                                    {
                                        Console.WriteLine($"   📺 episodes array found with {episodesDebug.GetArrayLength()} items");
                                    }
                                    else
                                    {
                                        Console.WriteLine("   ❌ episodes array not found");
                                    }
                                }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"❌ Error starting H.265 conversion: {ex.Message}");
                                    logger.LogError(ex, "Error starting H.265 conversion for {SeriesTitle}", seriesTitle);
                                }
                            }
                        }
                        else
                        {
                            Console.WriteLine($"❌ Series not found in Jellyfin: {seriesTitle} (TVDB: {seriesTvdbId})");
                            logger.LogWarning("Series not found in Jellyfin: {SeriesTitle} (TVDB: {TvdbId})", seriesTitle, seriesTvdbId);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"❌ Error refreshing Jellyfin series: {ex.Message}");
                        logger.LogError(ex, "Error refreshing Jellyfin series {SeriesTitle} (TVDB: {TvdbId})", seriesTitle, seriesTvdbId);
                    }
                }
                else
                {
                    Console.WriteLine($"❌ No TVDB ID found for series: {seriesTitle}");
                    logger.LogWarning("No TVDB ID found for Sonarr series: {SeriesTitle}", seriesTitle);
                }
            }
            else
            {
                Console.WriteLine($"💤 Jellyfin refresh disabled in configuration");
            }


}

async Task HandleSonarrGrabAsync(JsonElement root, ILogger logger)
{
    Console.WriteLine("🎯 Processing Sonarr Grab event");
    
    var seriesTitle = GetNestedStringProperty(root, "series", "title");
    var releaseTitle = GetNestedStringProperty(root, "release", "releaseTitle");
    var quality = GetNestedStringProperty(root, "release", "quality", "quality", "name");
    
    Console.WriteLine($"   📺 Series: {seriesTitle}");
    Console.WriteLine($"   📦 Release: {releaseTitle}");
    Console.WriteLine($"   💎 Quality: {quality}");
    
    logger.LogInformation("Sonarr grab: {SeriesTitle} - {ReleaseTitle}", seriesTitle, releaseTitle);
}

// Handler methods for different webhook types
async Task HandleUserDataSavedAsync(JsonElement root, ILogger logger)
{
    Console.WriteLine("🎬 Processing UserDataSaved event");
    
    // Extract specific fields you're interested in
    var seriesName = GetStringProperty(root, "SeriesName");
    var seriesId = GetStringProperty(root, "SeriesId");
    var episodeNumber = GetIntProperty(root, "EpisodeNumber");
    var seasonNumber = GetIntProperty(root, "SeasonNumber");
    var played = GetBoolProperty(root, "Played");
    var saveReason = GetStringProperty(root, "SaveReason");
    var username = GetStringProperty(root, "NotificationUsername");
    var userId = GetStringProperty(root, "UserId");

    Console.WriteLine($"   👤 User: {username}");
    Console.WriteLine($"   📺 Series: {seriesName}");
    Console.WriteLine($"   📋 Episode: S{seasonNumber:D2}E{episodeNumber:D2}");
    Console.WriteLine($"   ✅ Played: {played}");
    Console.WriteLine($"   🔄 Reason: {saveReason}");
        
    logger.LogInformation("UserData changed: {Series} S{Season}E{Episode} - Played: {Played}", 
        seriesName, seasonNumber, episodeNumber, played);

    // Check if we have an AniList client for this user
    if (!string.IsNullOrEmpty(username) && aniListClients.TryGetValue(username, out var userAniListClient) && !string.IsNullOrEmpty(seriesId))
    {
        Console.WriteLine($"🔥 BEFORE AniList update for user: {username}");
        logger.LogInformation("Updating {Username}'s linked AniList account", username);
        
        // Get user's auto-add setting
        var autoAdd = JellyfinAnilistSync.ConfigurationManager.ShouldAutoAddForUser(config, username);
        
        if (played)
        {
            // Episode was marked as played - sync this specific episode
            var anilistId = await jellyfinClient.GetProviderFromSeriesAsync(seriesId, "AniList");
            
            if (!string.IsNullOrEmpty(anilistId) && int.TryParse(anilistId, out int anilistIdInt))
            {
                // Get the last watched episode for the series, will be null if the user doesnt have the series in their library
                var lastWatched = await jellyfinClient.GetLastWatchedEpisodeAsync(seriesId, userId);
                var progressEpisode = lastWatched?.EpisodeNumber ?? episodeNumber;

                Console.WriteLine($"🔍 Episode played - updating AniList to episode {progressEpisode}");
                await userAniListClient.UpdateProgressByAniListIdAsync(anilistIdInt, progressEpisode, autoAdd);
                Console.WriteLine("✅ AFTER AniList update");
            }
            else
            {
                Console.WriteLine($"❌ No AniList provider ID found for series: {seriesName}");
            }
        }
        else
        {
            // Episode was unmarked as played - sync overall series progress
            Console.WriteLine($"🔄 Episode unmarked - syncing overall series progress");
            
            if (!string.IsNullOrEmpty(userId))
            {
                var syncResult = await jellyfinClient.SyncSeriesProgressToAniListAsync(seriesId, userId, userAniListClient, autoAdd);
                
                if (syncResult.Status == JellyfinApi.SyncStatus.Success || syncResult.Status == JellyfinApi.SyncStatus.SuccessViaSearch)
                {
                    Console.WriteLine($"✅ Series progress synced: {syncResult.Message}");
                }
                else
                {
                    Console.WriteLine($"❌ Series sync failed: {syncResult.Message}");
                }
            }
            else
            {
                Console.WriteLine("❌ No user ID found for series sync");
            }
        }
    }
    else
    {
        if (string.IsNullOrEmpty(username))
        {
            Console.WriteLine($"❌ No username found in webhook");
        }
        else if (!aniListClients.ContainsKey(username))
        {
            Console.WriteLine($"❌ No AniList client configured for user: {username}");
            Console.WriteLine($"💡 Add '{username}' to config.json userTokens section");
        }
        else
        {
            Console.WriteLine($"❌ Skipping AniList update - SeriesId: {seriesId}");
        }
    }
}

async Task HandleAuthenticationSuccess(JsonElement root, ILogger logger)
{
    Console.WriteLine("🚀 Processing SessionStarted event");
    
    var username = GetStringProperty(root, "NotificationUsername");
    var deviceName = GetStringProperty(root, "DeviceName");
    var client = GetStringProperty(root, "Client");
    var userId = GetStringProperty(root, "UserId");

    // Check if bulk update is enabled for this user
    var shouldBulkUpdate = JellyfinAnilistSync.ConfigurationManager.ShouldBulkUpdateForUser(config, username);
    
    // Sync for configured users on login (if bulk update is enabled)
    if (!string.IsNullOrEmpty(username) && aniListClients.TryGetValue(username, out var userAniListClient) && shouldBulkUpdate)
    {
        try
        {
            Console.WriteLine($"🔍 Finding anime library for user: {username}");
            
            // Get all libraries first
            var libraries = await jellyfinClient.GetLibrariesAsync();
            
            // Find anime library using configured library names
            var animeLibrary = libraries.FirstOrDefault(l => 
                config.LibraryNames.Any(name => l.Name.Contains(name, StringComparison.OrdinalIgnoreCase)) ||
                l.CollectionType == "tvshows");
            
            if (animeLibrary != null)
            {
                Console.WriteLine($"📚 Found library: {animeLibrary.Name} (ID: {animeLibrary.ItemId})");
                
                // Get user's auto-add setting for bulk sync
                var autoAdd = JellyfinAnilistSync.ConfigurationManager.ShouldAutoAddForUser(config, username);
                
                await jellyfinClient.SyncAllSeriesInLibraryAsync(animeLibrary.ItemId, userId, userAniListClient, autoAdd);
            }
            else
            {
                Console.WriteLine("❌ No anime library found");
                Console.WriteLine($"💡 Looking for libraries named: {string.Join(", ", config.LibraryNames)}");
                // List available libraries for debugging
                Console.WriteLine("Available libraries:");
                foreach (var lib in libraries)
                {
                    Console.WriteLine($"  - {lib.Name} ({lib.CollectionType}) ID: {lib.ItemId}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Library sync failed: {ex.Message}");
            logger.LogError(ex, "Failed to sync library on login for user {Username}", username);
        }
    }
    else
    {
        if (string.IsNullOrEmpty(username))
        {
            Console.WriteLine($"❌ No username found");
        }
        else if (!aniListClients.ContainsKey(username))
        {
            Console.WriteLine($"❌ No AniList client configured for user: {username}");
        }
        else if (!shouldBulkUpdate)
        {
            Console.WriteLine($"💤 Bulk update disabled for user: {username}");
        }
    }

    Console.WriteLine($"   👤 User: {username}");
    Console.WriteLine($"   📱 Device: {deviceName}");
    Console.WriteLine($"   💻 Client: {client}");
}

// Helper methods to safely extract field values
static string GetStringProperty(JsonElement element, string propertyName)
{
    return element.TryGetProperty(propertyName, out var prop) ? prop.GetString() ?? "" : "";
}

static int GetIntProperty(JsonElement element, string propertyName)
{
    return element.TryGetProperty(propertyName, out var prop) ? prop.GetInt32() : 0;
}

static bool GetBoolProperty(JsonElement element, string propertyName)
{
    return element.TryGetProperty(propertyName, out var prop) && prop.GetBoolean();
}

// Helper methods for nested JSON properties (for Sonarr webhooks)
static string GetNestedStringProperty(JsonElement element, params string[] propertyPath)
{
    var current = element;
    foreach (var property in propertyPath)
    {
        if (!current.TryGetProperty(property, out current))
        {
            return "";
        }
    }
    return current.ValueKind == JsonValueKind.String ? current.GetString() ?? "" : "";
}

static int GetNestedIntProperty(JsonElement element, params string[] propertyPath)
{
    var current = element;
    foreach (var property in propertyPath)
    {
        if (!current.TryGetProperty(property, out current))
        {
            return 0;
        }
    }
    return current.ValueKind == JsonValueKind.Number ? current.GetInt32() : 0;
}

/// <summary>
/// Extracts the file path from a Sonarr webhook payload
/// </summary>
/// <param name="root">The root JSON element from the Sonarr webhook</param>
/// <returns>The file path if found, null otherwise</returns>
static string? GetEpisodeFilePathFromSonarrWebhook(JsonElement root)
{
    try
    {
        // Try to get the file path from the episodeFiles array (this is where the actual file path is)
        if (root.TryGetProperty("episodeFiles", out var episodeFiles) && episodeFiles.ValueKind == JsonValueKind.Array)
        {
            foreach (var episodeFile in episodeFiles.EnumerateArray())
            {
                if (episodeFile.TryGetProperty("path", out var filePathProp))
                {
                    var filePath = filePathProp.GetString();
                    if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                    {
                        return filePath;
                    }
                }
            }
        }

        // Fallback: try to get from the episodes array (though this usually doesn't have file paths)
        if (root.TryGetProperty("episodes", out var episodes) && episodes.ValueKind == JsonValueKind.Array)
        {
            foreach (var episode in episodes.EnumerateArray())
            {
                if (episode.TryGetProperty("filePath", out var filePathProp))
                {
                    var filePath = filePathProp.GetString();
                    if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                    {
                        return filePath;
                    }
                }
            }
        }

        // Last fallback: try to get from the series level (this will be a folder path, not a file path)
        if (root.TryGetProperty("series", out var series) && 
            series.TryGetProperty("path", out var seriesPathProp))
        {
            var seriesPath = seriesPathProp.GetString();
            if (!string.IsNullOrEmpty(seriesPath))
            {
                Console.WriteLine($"⚠️ Warning: Only series folder path available, not episode file path: {seriesPath}");
                // Don't return the series path as it's a folder, not a file
                return null;
            }
        }

        Console.WriteLine("⚠️ No valid episode file path found in Sonarr webhook");
        return null;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Error extracting file path from Sonarr webhook: {ex.Message}");
        return null;
    }
}

// Handle graceful shutdown for Windows Service
app.Lifetime.ApplicationStopping.Register(() =>
{
    logger.LogInformation("JellyfinAnilistSync webhook service is stopping...");
});

app.Lifetime.ApplicationStopped.Register(() =>
{
    logger.LogInformation("JellyfinAnilistSync webhook service has stopped");
});

try
{
    app.Run();
}
catch (Exception ex)
{
    logger.LogCritical(ex, "Application failed to start: {ErrorMessage}", ex.Message);
    throw;
}