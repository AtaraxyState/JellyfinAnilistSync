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