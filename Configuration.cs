using System.Text.Json;

namespace JellyfinAnilistSync;

public class Configuration
{
    public JellyfinConfig Jellyfin { get; set; } = new();
    public AniListConfig AniList { get; set; } = new();
    public WebhookConfig Webhook { get; set; } = new();
    public List<string> LibraryNames { get; set; } = new();
}

public class JellyfinConfig
{
    public string ServerUrl { get; set; } = "";
    public string ApiKey { get; set; } = "";
}

public class AniListConfig
{
    public string GlobalToken { get; set; } = "";
    public Dictionary<string, string> UserTokens { get; set; } = new();
}

public class WebhookConfig
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5000;
    public string Url { get; set; } = "";
}

public static class ConfigurationManager
{
    private static readonly string ConfigDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), 
        "JellyfinAnilistSync"
    );
    private static readonly string ConfigPath = Path.Combine(ConfigDirectory, "config.json");
    private static readonly string MissingSeriesPath = Path.Combine(ConfigDirectory, "missing_anilist_series.json");

    public static Configuration LoadConfiguration()
    {
        try
        {
            // Ensure directory exists
            if (!Directory.Exists(ConfigDirectory))
            {
                Directory.CreateDirectory(ConfigDirectory);
                Console.WriteLine($"📁 Created config directory: {ConfigDirectory}");
            }

            // Check if config file exists
            if (!File.Exists(ConfigPath))
            {
                Console.WriteLine($"📄 Config file not found. Creating default config at: {ConfigPath}");
                var defaultConfig = CreateDefaultConfiguration();
                SaveConfiguration(defaultConfig);
                
                Console.WriteLine("⚠️  Please edit the config file with your actual values:");
                Console.WriteLine($"   📝 Config file: {ConfigPath}");
                Console.WriteLine("   🔧 Update Jellyfin server URL and API key");
                Console.WriteLine("   🎯 Add AniList tokens for each user");
                Console.WriteLine("   🌐 Configure webhook settings");
                
                return defaultConfig;
            }

            // Load existing config
            var jsonContent = File.ReadAllText(ConfigPath);
            var config = JsonSerializer.Deserialize<Configuration>(jsonContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (config == null)
            {
                throw new InvalidOperationException("Failed to deserialize configuration");
            }

            Console.WriteLine($"✅ Loaded configuration from: {ConfigPath}");
            Console.WriteLine($"   🏠 Jellyfin: {config.Jellyfin.ServerUrl}");
            Console.WriteLine($"   👥 Users: {string.Join(", ", config.AniList.UserTokens.Keys)}");
            Console.WriteLine($"   🌐 Webhook: {config.Webhook.Host}:{config.Webhook.Port}");

            return config;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error loading configuration: {ex.Message}");
            Console.WriteLine("Creating default configuration...");
            
            var defaultConfig = CreateDefaultConfiguration();
            SaveConfiguration(defaultConfig);
            return defaultConfig;
        }
    }

    public static void SaveConfiguration(Configuration config)
    {
        try
        {
            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var jsonContent = JsonSerializer.Serialize(config, jsonOptions);
            File.WriteAllText(ConfigPath, jsonContent);
            
            Console.WriteLine($"💾 Configuration saved to: {ConfigPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error saving configuration: {ex.Message}");
        }
    }

    private static Configuration CreateDefaultConfiguration()
    {
        return new Configuration
        {
            Jellyfin = new JellyfinConfig
            {
                ServerUrl = "http://localhost:8096",
                ApiKey = "YOUR_JELLYFIN_API_KEY_HERE"
            },
            AniList = new AniListConfig
            {
                GlobalToken = "YOUR_GLOBAL_ANILIST_TOKEN_HERE",
                UserTokens = new Dictionary<string, string>
                {
                    { "YourJellyfinUsername", "YOUR_ANILIST_TOKEN_HERE" }
                }
            },
            Webhook = new WebhookConfig
            {
                Host = "localhost",
                Port = 5000,
                Url = "http://localhost:5000"
            },
            LibraryNames = new List<string> { "Animes", "Anime" }
        };
    }

    public static string GetAniListTokenForUser(Configuration config, string jellyfinUsername)
    {
        // Try to get user-specific token first
        if (config.AniList.UserTokens.TryGetValue(jellyfinUsername, out var userToken))
        {
            Console.WriteLine($"🎯 Using user-specific AniList token for: {jellyfinUsername}");
            return userToken;
        }

        // Fallback to global token
        if (!string.IsNullOrEmpty(config.AniList.GlobalToken))
        {
            Console.WriteLine($"🌐 Using global AniList token for: {jellyfinUsername}");
            return config.AniList.GlobalToken;
        }

        // No token available
        Console.WriteLine($"❌ No AniList token found for user: {jellyfinUsername}");
        return "";
    }

    public static void AddMissingSeries(MissingSeries series)
    {
        try
        {
            var existingSeries = LoadMissingSeries();
            
            // Check if series already exists (avoid duplicates)
            if (existingSeries.Any(s => s.JellyfinId == series.JellyfinId))
            {
                Console.WriteLine($"📝 Series already in missing list: {series.Name}");
                return;
            }
            
            existingSeries.Add(series);
            SaveMissingSeries(existingSeries);
            
            Console.WriteLine($"📝 Added to missing series list: {series.Name}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error adding missing series: {ex.Message}");
        }
    }

    public static List<MissingSeries> LoadMissingSeries()
    {
        try
        {
            if (!File.Exists(MissingSeriesPath))
            {
                return new List<MissingSeries>();
            }

            var jsonContent = File.ReadAllText(MissingSeriesPath);
            var series = JsonSerializer.Deserialize<List<MissingSeries>>(jsonContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return series ?? new List<MissingSeries>();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error loading missing series: {ex.Message}");
            return new List<MissingSeries>();
        }
    }

    private static void SaveMissingSeries(List<MissingSeries> series)
    {
        try
        {
            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var jsonContent = JsonSerializer.Serialize(series, jsonOptions);
            File.WriteAllText(MissingSeriesPath, jsonContent);
            
            Console.WriteLine($"💾 Missing series list saved: {series.Count} entries");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error saving missing series: {ex.Message}");
        }
    }
}

public class MissingSeries
{
    public string JellyfinId { get; set; } = "";
    public string Name { get; set; } = "";
    public string PremiereDate { get; set; } = "";
    public DateTime FirstSeen { get; set; } = DateTime.Now;
    public string? TmdbId { get; set; }
    public string? ImdbId { get; set; }
    public string? TvdbId { get; set; }
    public string SearchedName { get; set; } = "";
    public string Reason { get; set; } = "";
}
