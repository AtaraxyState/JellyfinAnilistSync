using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using JellyfinApi;

namespace JellyfinAnilistSync;

public class VideoConversionService
{
    private readonly ILogger _logger;
    private readonly JellyfinClient _jellyfinClient;
    private readonly Configuration _config;
    private readonly Dictionary<string, ConversionJob> _activeConversions = new();
    private readonly object _lockObject = new object();

    public VideoConversionService(ILogger logger, JellyfinClient jellyfinClient, Configuration config)
    {
        _logger = logger;
        _jellyfinClient = jellyfinClient;
        _config = config;
    }

    /// <summary>
    /// Starts an H.265 conversion job for a video file if it's H.264 and conversion is enabled.
    /// Always notifies Jellyfin to refresh series metadata, regardless of conversion status.
    /// </summary>
    /// <param name="filePath">Path to the video file</param>
    /// <param name="seriesId">Jellyfin series ID for refresh notification</param>
    /// <param name="seriesName">Series name for logging</param>
    /// <returns>True if conversion was started, false if not needed or failed</returns>
    public async Task<bool> StartConversionIfNeededAsync(string filePath, string? seriesId = null, string? seriesName = null)
    {
        var shouldNotifyJellyfin = !string.IsNullOrEmpty(seriesId);
        var conversionStarted = false;

        // Always notify Jellyfin first if we have a series ID
        if (shouldNotifyJellyfin)
        {
            await NotifyJellyfinOfImport(seriesId, seriesName, "Sonarr import completed");
        }

        // Check if H.265 conversion is enabled
        if (!_config.Conversion.AutoConvertToHEVC)
        {
            _logger.LogInformation("H.265 auto-conversion is disabled in configuration");
            return false;
        }

        if (string.IsNullOrEmpty(filePath))
        {
            _logger.LogWarning("No file path provided for H.265 conversion");
            return false;
        }

        if (!File.Exists(filePath))
        {
            _logger.LogWarning("File path does not exist: {FilePath}", filePath);
            
            // Check if it's a directory instead of a file
            if (Directory.Exists(filePath))
            {
                _logger.LogWarning("The provided path is a directory, not a file: {FilePath}", filePath);
                _logger.LogWarning("This suggests the Sonarr webhook didn't provide the episode file path correctly");
            }
            
            return false;
        }

        try
        {
            // Check if file is already H.265
            var videoCodec = await GetVideoCodecAsync(filePath);
            if (string.IsNullOrEmpty(videoCodec))
            {
                _logger.LogWarning("Could not determine video codec for file: {FilePath}", filePath);
                return false;
            }

            if (videoCodec.ToLower().Contains("hevc") || videoCodec.ToLower().Contains("h265"))
            {
                _logger.LogInformation("File is already H.265, no conversion needed: {FilePath}", filePath);
                
                // Log to conversion log file
                JellyfinAnilistSync.ConfigurationManager.WriteConversionLog($"💤 File already H.265, no conversion needed: {Path.GetFileName(filePath)}");
                
                return false;
            }

            if (!videoCodec.ToLower().Contains("h264") && !videoCodec.ToLower().Contains("avc"))
            {
                _logger.LogInformation("File is not H.264, skipping conversion: {FilePath} (Codec: {Codec})", filePath, videoCodec);
                
                // Log to conversion log file
                JellyfinAnilistSync.ConfigurationManager.WriteConversionLog($"💤 File not H.264, skipping conversion: {Path.GetFileName(filePath)} (Codec: {videoCodec})");
                
                return false;
            }

            // Start conversion
            var jobId = Guid.NewGuid().ToString();
            var conversionJob = new ConversionJob
            {
                Id = jobId,
                FilePath = filePath,
                SeriesId = seriesId,
                SeriesName = seriesName,
                StartTime = DateTime.UtcNow,
                Status = ConversionStatus.Starting
            };

            lock (_lockObject)
            {
                _activeConversions[jobId] = conversionJob;
            }

            _logger.LogInformation("Starting H.265 conversion for: {FilePath} (Job ID: {JobId})", filePath, jobId);
            
            // Log to conversion log file
            JellyfinAnilistSync.ConfigurationManager.WriteConversionLog($"🎬 Starting H.265 conversion for: {Path.GetFileName(filePath)} (Job ID: {jobId})");

            // Start conversion in background
            _ = Task.Run(async () => await RunConversionAsync(conversionJob));

            conversionStarted = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting video conversion for: {FilePath}", filePath);
            
            // Log to conversion log file
            JellyfinAnilistSync.ConfigurationManager.WriteConversionLog($"💥 Error starting video conversion for: {Path.GetFileName(filePath)} - {ex.Message}");
        }

        return conversionStarted;
    }

    /// <summary>
    /// Gets the current status of all active conversions
    /// </summary>
    public List<ConversionJob> GetActiveConversions()
    {
        lock (_lockObject)
        {
            return _activeConversions.Values.ToList();
        }
    }

    /// <summary>
    /// Gets the status of a specific conversion job
    /// </summary>
    public ConversionJob? GetConversionStatus(string jobId)
    {
        lock (_lockObject)
        {
            return _activeConversions.TryGetValue(jobId, out var job) ? job : null;
        }
    }

    private async Task RunConversionAsync(ConversionJob job)
    {
        try
        {
            job.Status = ConversionStatus.Running;
            job.StartTime = DateTime.UtcNow;

            _logger.LogInformation("Conversion started for: {FilePath} (Job ID: {JobId})", job.FilePath, job.Id);
            
            // Log to conversion log file
            JellyfinAnilistSync.ConfigurationManager.WriteConversionLog($"🔄 Conversion started for: {Path.GetFileName(job.FilePath)} (Job ID: {job.Id})");
            
            // Add initial progress log entry to confirm logging is working
            JellyfinAnilistSync.ConfigurationManager.WriteConversionLog($"📊 Progress: 0.0% complete (00:00:00 / ~{GetEstimatedVideoDuration(job.FilePath):hh\\:mm\\:ss}) for {Path.GetFileName(job.FilePath)}");

            // Generate output file path
            var outputPath = GenerateOutputPath(job.FilePath);
            job.OutputPath = outputPath;

            // Run FFmpeg conversion
            var success = await RunFFmpegConversionAsync(job.FilePath, outputPath, job);

            if (success)
            {
                job.Status = ConversionStatus.Completed;
                job.EndTime = DateTime.UtcNow;
                job.Duration = job.EndTime - job.StartTime;

                // Log final progress (100% complete)
                JellyfinAnilistSync.ConfigurationManager.WriteConversionLog($"📊 Progress: 100.0% complete for {Path.GetFileName(job.FilePath)}");

                _logger.LogInformation("Conversion completed successfully: {FilePath} -> {OutputPath} (Duration: {Duration})", 
                    job.FilePath, outputPath, job.Duration);
                
                // Log to conversion log file
                JellyfinAnilistSync.ConfigurationManager.WriteConversionLog($"✅ Conversion completed successfully: {Path.GetFileName(job.FilePath)} -> {Path.GetFileName(outputPath)} (Duration: {job.Duration})");

                // Notify Jellyfin to refresh the series
                if (!string.IsNullOrEmpty(job.SeriesId))
                {
                    await NotifyJellyfinOfConversion(job);
                }

                // Clean up original file if conversion was successful
                await CleanupOriginalFileAsync(job);
            }
            else
            {
                job.Status = ConversionStatus.Failed;
                job.EndTime = DateTime.UtcNow;
                job.Duration = job.EndTime - job.StartTime;

                _logger.LogError("Conversion failed for: {FilePath} (Job ID: {JobId})", job.FilePath, job.Id);
                
                // Log to conversion log file
                JellyfinAnilistSync.ConfigurationManager.WriteConversionLog($"❌ Conversion failed for: {Path.GetFileName(job.FilePath)} (Job ID: {job.Id})");
            }
        }
        catch (Exception ex)
        {
            job.Status = ConversionStatus.Failed;
            job.EndTime = DateTime.UtcNow;
            job.Duration = job.EndTime - job.StartTime;
            job.ErrorMessage = ex.Message;

            _logger.LogError(ex, "Unexpected error during conversion: {FilePath} (Job ID: {JobId})", job.FilePath, job.Id);
            
            // Log to conversion log file
            JellyfinAnilistSync.ConfigurationManager.WriteConversionLog($"💥 Unexpected error during conversion: {Path.GetFileName(job.FilePath)} (Job ID: {job.Id}) - {ex.Message}");
        }
        finally
        {
            // Remove completed/failed jobs after a delay to allow status queries
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromMinutes(5)); // Keep job info for 5 minutes
                lock (_lockObject)
                {
                    _activeConversions.Remove(job.Id);
                }
            });
        }
    }

    private async Task<bool> RunFFmpegConversionAsync(string inputPath, string outputPath, ConversionJob job)
    {
        try
        {
            // Ensure output directory exists
            var outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            // Build FFmpeg command based on GPU acceleration settings
            var arguments = BuildFFmpegArguments(inputPath, outputPath);

            _logger.LogInformation("Running FFmpeg: ffmpeg {Arguments}", arguments);
            
            // Log to conversion log file
            var preset = _config.Conversion.HEVCPreset ?? "medium";
            JellyfinAnilistSync.ConfigurationManager.WriteConversionLog($"🎬 Running FFmpeg conversion for: {Path.GetFileName(inputPath)} with preset: {preset}");
            
            // Debug: log the actual FFmpeg command being executed
            _logger.LogDebug("FFmpeg command: ffmpeg {Arguments}", arguments);

            var startInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };
            
            // Capture FFmpeg output for progress tracking
            var outputLines = new List<string>();
            var errorLines = new List<string>();
            var lastProgressLogTime = DateTime.UtcNow;
            var progressLogInterval = TimeSpan.FromMinutes(1); // Log progress every 1 minute (more frequent)

            process.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    outputLines.Add(e.Data);
                    job.LastOutput = e.Data;
                }
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    errorLines.Add(e.Data);
                    job.LastError = e.Data;
                    
                    // FFmpeg sends progress information to stderr, not stdout
                    // Try to extract progress information from the error stream
                    if (e.Data.Contains("time="))
                    {
                        var timeMatch = System.Text.RegularExpressions.Regex.Match(e.Data, @"time=(\d{2}):(\d{2}):(\d{2})\.(\d{2})");
                        if (timeMatch.Success)
                        {
                            var hours = int.Parse(timeMatch.Groups[1].Value);
                            var minutes = int.Parse(timeMatch.Groups[2].Value);
                            var seconds = int.Parse(timeMatch.Groups[3].Value);
                            var centiseconds = int.Parse(timeMatch.Groups[4].Value);
                            
                            job.CurrentProgress = new TimeSpan(0, hours, minutes, seconds, centiseconds * 10);
                            
                            // Log progress at regular intervals
                            var now = DateTime.UtcNow;
                            if (now - lastProgressLogTime >= progressLogInterval)
                            {
                                _logger.LogDebug("Progress update triggered for job {JobId}: {Progress}", job.Id, job.CurrentProgress);
                                LogProgressUpdate(job);
                                lastProgressLogTime = now;
                            }
                        }
                        else
                        {
                            // Debug: log when we see "time=" but regex didn't match
                            _logger.LogDebug("Saw 'time=' in FFmpeg output but regex didn't match: {Data}", e.Data);
                        }
                    }
                    
                    // Debug: log first few FFmpeg output lines to see what we're getting
                    if (errorLines.Count <= 5)
                    {
                        _logger.LogDebug("FFmpeg stderr output [{Count}]: {Data}", errorLines.Count, e.Data);
                    }
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Wait for completion with timeout
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromHours(2)); // 2 hour timeout
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("FFmpeg conversion timed out for: {FilePath}", inputPath);
                try { process.Kill(); } catch { }
                return false;
            }

            if (process.ExitCode == 0)
            {
                // Verify output file exists and has content
                if (File.Exists(outputPath) && new FileInfo(outputPath).Length > 0)
                {
                    _logger.LogInformation("FFmpeg conversion completed successfully: {OutputPath}", outputPath);
                    
                    // Log to conversion log file
                    JellyfinAnilistSync.ConfigurationManager.WriteConversionLog($"✅ FFmpeg conversion completed successfully: {Path.GetFileName(outputPath)}");
                    
                    return true;
                }
                else
                {
                    _logger.LogError("FFmpeg completed but output file is missing or empty: {OutputPath}", outputPath);
                    
                    // Log to conversion log file
                    JellyfinAnilistSync.ConfigurationManager.WriteConversionLog($"⚠️ FFmpeg completed but output file is missing or empty: {Path.GetFileName(outputPath)}");
                    
                    return false;
                }
            }
            else
            {
                _logger.LogError("FFmpeg conversion failed with exit code {ExitCode}: {FilePath}", process.ExitCode, inputPath);
                _logger.LogError("FFmpeg error output: {ErrorOutput}", string.Join("\n", errorLines));
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error running FFmpeg conversion: {FilePath}", inputPath);
            return false;
        }
    }

    /// <summary>
    /// Notifies Jellyfin to refresh series metadata after a Sonarr import
    /// </summary>
    /// <param name="seriesId">Jellyfin series ID</param>
    /// <param name="seriesName">Series name for logging</param>
    /// <param name="reason">Reason for the refresh</param>
    private async Task NotifyJellyfinOfImport(string seriesId, string? seriesName, string reason)
    {
        try
        {
            _logger.LogInformation("Notifying Jellyfin of {Reason} for series: {SeriesName} (ID: {SeriesId})", 
                reason, seriesName ?? "Unknown", seriesId);

            // Refresh the series metadata to pick up any new episodes
            var refreshSuccess = await _jellyfinClient.RefreshSeriesAsync(seriesId);

            if (refreshSuccess)
            {
                _logger.LogInformation("Successfully notified Jellyfin of {Reason} for series: {SeriesName}", reason, seriesName ?? "Unknown");
                
                // Log to conversion log file
                JellyfinAnilistSync.ConfigurationManager.WriteConversionLog($"🔄 Notified Jellyfin of {reason} for series: {seriesName ?? "Unknown"}");
            }
            else
            {
                _logger.LogWarning("Failed to notify Jellyfin of {Reason} for series: {SeriesName}", reason, seriesName ?? "Unknown");
                
                // Log to conversion log file
                JellyfinAnilistSync.ConfigurationManager.WriteConversionLog($"⚠️ Failed to notify Jellyfin of {reason} for series: {seriesName ?? "Unknown"}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error notifying Jellyfin of {Reason} for series: {SeriesName}", reason, seriesName ?? "Unknown");
            
            // Log to conversion log file
            JellyfinAnilistSync.ConfigurationManager.WriteConversionLog($"💥 Error notifying Jellyfin of {reason} for series: {seriesName ?? "Unknown"} - {ex.Message}");
        }
    }

    private async Task NotifyJellyfinOfConversion(ConversionJob job)
    {
        try
        {
            if (string.IsNullOrEmpty(job.SeriesId))
            {
                _logger.LogInformation("No series ID available for Jellyfin notification");
                return;
            }

            _logger.LogInformation("Notifying Jellyfin of completed conversion for series: {SeriesName} (ID: {SeriesId})", 
                job.SeriesName, job.SeriesId);

            // Refresh the series metadata to pick up the new H.265 file
            var refreshSuccess = await _jellyfinClient.RefreshSeriesAsync(job.SeriesId);

            if (refreshSuccess)
            {
                _logger.LogInformation("Successfully notified Jellyfin of conversion completion for series: {SeriesName}", job.SeriesName);
                
                // Log to conversion log file
                JellyfinAnilistSync.ConfigurationManager.WriteConversionLog($"🔄 Notified Jellyfin of conversion completion for series: {job.SeriesName}");
            }
            else
            {
                _logger.LogWarning("Failed to notify Jellyfin of conversion completion for series: {SeriesName}", job.SeriesName);
                
                // Log to conversion log file
                JellyfinAnilistSync.ConfigurationManager.WriteConversionLog($"⚠️ Failed to notify Jellyfin of conversion completion for series: {job.SeriesName}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error notifying Jellyfin of conversion completion for series: {SeriesName}", job.SeriesName);
            
            // Log to conversion log file
            JellyfinAnilistSync.ConfigurationManager.WriteConversionLog($"💥 Error notifying Jellyfin of conversion completion for series: {job.SeriesName} - {ex.Message}");
        }
    }

    /// <summary>
    /// Logs progress updates to both the logger and conversion log file
    /// </summary>
    /// <param name="job">The conversion job to log progress for</param>
    private void LogProgressUpdate(ConversionJob job)
    {
        try
        {
            if (job.CurrentProgress.HasValue)
            {
                var currentTime = job.CurrentProgress.Value;
                var elapsed = DateTime.UtcNow - job.StartTime;
                
                // Get the actual video duration for accurate percentage calculation
                var videoDuration = GetEstimatedVideoDuration(job.FilePath);
                var percentage = Math.Min(100.0, (currentTime.TotalSeconds / videoDuration.TotalSeconds) * 100.0);
                
                var progressMessage = $"📊 Progress: {percentage:F1}% complete ({currentTime:hh\\:mm\\:ss} / {videoDuration:hh\\:mm\\:ss})";
                
                _logger.LogInformation("Conversion progress for {FileName}: {ProgressMessage}", 
                    Path.GetFileName(job.FilePath), progressMessage);
                
                // Log to conversion log file
                JellyfinAnilistSync.ConfigurationManager.WriteConversionLog($"📊 Progress: {percentage:F1}% complete ({currentTime:hh\\:mm\\:ss} / {videoDuration:hh\\:mm\\:ss}) for {Path.GetFileName(job.FilePath)}");
            }
            else
            {
                _logger.LogDebug("No progress data available for job {JobId}", job.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error logging progress update for job {JobId}", job.Id);
        }
    }

    /// <summary>
    /// Builds the FFmpeg command arguments based on GPU acceleration configuration
    /// </summary>
    /// <param name="inputPath">Input file path</param>
    /// <param name="outputPath">Output file path</param>
    /// <returns>FFmpeg command arguments string</returns>
    private string BuildFFmpegArguments(string inputPath, string outputPath)
    {
        var preset = _config.Conversion.HEVCPreset ?? "medium";
        
        if (_config.Conversion.UseGPUAcceleration)
        {
            var gpuEncoder = _config.Conversion.GPUEncoder?.ToLower() ?? "auto";
            
            // Try to detect and use the best available GPU encoder
            if (gpuEncoder == "auto" || gpuEncoder == "nvidia")
            {
                // NVIDIA NVENC encoder
                return $"-i \"{inputPath}\" -c:v hevc_nvenc -preset p4 -rc vbr -cq 23 -c:a copy \"{outputPath}\"";
            }
            else if (gpuEncoder == "amd")
            {
                // AMD AMF encoder
                return $"-i \"{inputPath}\" -c:v hevc_amf -quality speed -rc cqp -qp_i 23 -qp_p 23 -c:a copy \"{outputPath}\"";
            }
            else if (gpuEncoder == "intel")
            {
                // Intel QSV encoder
                return $"-i \"{inputPath}\" -c:v hevc_qsv -preset medium -global_quality 23 -c:a copy \"{outputPath}\"";
            }
            else
            {
                _logger.LogWarning("Unknown GPU encoder '{GPUEncoder}', falling back to CPU encoding", gpuEncoder);
                return $"-i \"{inputPath}\" -c:v libx265 -preset {preset} -crf 23 -c:a copy \"{outputPath}\"";
            }
        }
        else
        {
            // CPU encoding (default)
            return $"-i \"{inputPath}\" -c:v libx265 -preset {preset} -crf 23 -c:a copy \"{outputPath}\"";
        }
    }

    /// <summary>
    /// Gets an estimated video duration for progress calculation
    /// </summary>
    /// <param name="filePath">Path to the video file</param>
    /// <returns>Estimated duration, defaults to 20 minutes if cannot determine</returns>
    private TimeSpan GetEstimatedVideoDuration(string filePath)
    {
        try
        {
            // Try to get duration from FFprobe
            var startInfo = new ProcessStartInfo
            {
                FileName = "ffprobe",
                Arguments = $"-v quiet -show_entries format=duration -of csv=p=0 \"{filePath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };
            process.Start();
            
            var output = process.StandardOutput.ReadToEndAsync().Result;
            process.WaitForExit();

            if (process.ExitCode == 0 && double.TryParse(output.Trim(), out var durationSeconds))
            {
                return TimeSpan.FromSeconds(durationSeconds);
            }
        }
        catch
        {
            // Ignore errors, fall back to default
        }
        
        // Default to 20 minutes if we can't determine duration
        return TimeSpan.FromMinutes(20);
    }

    private async Task<bool> CleanupOriginalFileAsync(ConversionJob job)
    {
        try
        {
            if (string.IsNullOrEmpty(job.OutputPath) || !File.Exists(job.OutputPath))
            {
                _logger.LogWarning("Output file not found, skipping cleanup: {OutputPath}", job.OutputPath);
                return false;
            }

            var outputFileInfo = new FileInfo(job.OutputPath);
            if (outputFileInfo.Length == 0)
            {
                _logger.LogWarning("Output file is empty, skipping cleanup: {OutputPath}", job.OutputPath);
                return false;
            }

            // Verify the output file is valid by checking if it's larger than a minimum size
            if (outputFileInfo.Length < 1024 * 1024) // Less than 1MB
            {
                _logger.LogWarning("Output file seems too small, skipping cleanup: {OutputPath} ({Size} bytes)", 
                    job.OutputPath, outputFileInfo.Length);
                return false;
            }

            // Delete original file
            if (File.Exists(job.FilePath))
            {
                File.Delete(job.FilePath);
                _logger.LogInformation("Deleted original file after successful conversion: {FilePath}", job.FilePath);
                
                // Log to conversion log file
                JellyfinAnilistSync.ConfigurationManager.WriteConversionLog($"🗑️ Deleted original file after successful conversion: {Path.GetFileName(job.FilePath)}");
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during cleanup of original file: {FilePath}", job.FilePath);
            return false;
        }
    }

    private async Task<string> GetVideoCodecAsync(string filePath)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "ffprobe",
                Arguments = $"-v quiet -select_streams v:0 -show_entries stream=codec_name -of csv=p=0 \"{filePath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };
            process.Start();
            
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0)
            {
                return output.Trim();
            }
            else
            {
                _logger.LogWarning("FFprobe failed to get codec info for: {FilePath}", filePath);
                return string.Empty;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting video codec for: {FilePath}", filePath);
            return string.Empty;
        }
    }

    private string GenerateOutputPath(string inputPath)
    {
        var directory = Path.GetDirectoryName(inputPath);
        var fileName = Path.GetFileNameWithoutExtension(inputPath);
        var extension = Path.GetExtension(inputPath);

        // Add H265 suffix to indicate conversion
        var outputFileName = $"{fileName}_H265{extension}";
        
        return Path.Combine(directory ?? "", outputFileName);
    }
}

public class ConversionJob
{
    public string Id { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string? OutputPath { get; set; }
    public string? SeriesId { get; set; }
    public string? SeriesName { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public TimeSpan? Duration { get; set; }
    public ConversionStatus Status { get; set; }
    public string? LastOutput { get; set; }
    public string? LastError { get; set; }
    public string? ErrorMessage { get; set; }
    public TimeSpan? CurrentProgress { get; set; }
}

public enum ConversionStatus
{
    Starting,
    Running,
    Completed,
    Failed
}
