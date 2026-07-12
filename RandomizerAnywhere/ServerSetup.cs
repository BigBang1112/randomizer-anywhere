using RandomizerAnywhere.Config;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Tomlyn;

namespace RandomizerAnywhere;

internal sealed partial class ServerSetup
{
    private const string CacheFileName = ".download-cache.toml";
    private const int CopyBufferSize = 81920;

    private readonly HttpClient http;
    private readonly TmxRules tmxRules;
    private readonly AppConfig config;
    private readonly DedicatedServerType game;
    private readonly string serverDir;
    private readonly string dedicatedServerDir;
    private readonly string dedicatedExeFileName;

    private Process? serverProcess;

    public ServerSetup(HttpClient http, TmxRules tmxRules, AppConfig config)
    {
        this.http = http;
        this.tmxRules = tmxRules;
        this.config = config;

        game = config.Game switch
        {
            GameTitle.TMNF or GameTitle.TMUF => DedicatedServerType.TMF,
            GameTitle.TMN or GameTitle.TMS or GameTitle.TMO => DedicatedServerType.TM,
            _ => throw new InvalidOperationException($"Unsupported game: {config.Game}")
        };
        serverDir = Path.Combine(AppContext.BaseDirectory, "servers", game.ToString());

        switch (game)
        {
            case DedicatedServerType.TMF:
                dedicatedServerDir = serverDir;
                dedicatedExeFileName = "TrackmaniaServer.exe";
                break;
            case DedicatedServerType.TM:
                dedicatedServerDir = Path.Combine(serverDir, "TmDedicatedServer");
                dedicatedExeFileName = "TrackManiaServer.exe";
                break;
            default:
                throw new InvalidOperationException($"Unsupported server type: {game}");
        }
    }

    public async Task SetupServerAsync(CancellationToken cancellationToken = default)
    {
        var downloadUrl = config.DownloadUrls.TryGetValue(game, out var url) ? url : throw new InvalidOperationException($"Download URL for {game} not found in configuration.");

        var serverDir = Path.Combine(AppContext.BaseDirectory, "servers", game.ToString());
        var cacheFilePath = Path.Combine(serverDir, CacheFileName);

        var cachedEntry = await ReadCacheEntryAsync(cacheFilePath, cancellationToken);
        var canUseCachedEntry = cachedEntry is not null && cachedEntry.Url == downloadUrl && Directory.Exists(serverDir);

        using var request = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
        if (canUseCachedEntry)
        {
            if (cachedEntry!.ETag is { } etag && EntityTagHeaderValue.TryParse(etag, out var etagValue))
            {
                request.Headers.IfNoneMatch.Add(etagValue);
            }

            if (cachedEntry.LastModified is { } lastModified)
            {
                request.Headers.IfModifiedSince = lastModified;
            }
        }

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (canUseCachedEntry && response.StatusCode == HttpStatusCode.NotModified)
        {
            return;
        }

        response.EnsureSuccessStatusCode();

        var newEntry = new DownloadCacheEntry(
            downloadUrl,
            response.Headers.ETag?.ToString(),
            response.Content.Headers.LastModified,
            response.Content.Headers.ContentLength);

        // Some servers don't support conditional requests but still report ETag/Last-Modified/Content-Length,
        // so we can avoid re-downloading and re-extracting the archive if nothing actually changed.
        if (canUseCachedEntry && cachedEntry == newEntry)
        {
            return;
        }

        Directory.CreateDirectory(serverDir);

        var tempZipPath = Path.GetTempFileName();
        try
        {
            await using (var fileStream = new FileStream(tempZipPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await DownloadWithProgressAsync(response.Content, fileStream, cancellationToken);
            }

            await ZipFile.ExtractToDirectoryAsync(tempZipPath, serverDir, overwriteFiles: true, cancellationToken);
        }
        finally
        {
            File.Delete(tempZipPath);
        }

        await WriteCacheEntryAsync(cacheFilePath, newEntry, cancellationToken);
    }

    public async Task SetupDedicatedCfgAsync(CancellationToken cancellationToken = default)
    {
        var filePath = Path.Combine(dedicatedServerDir, "dedicated.cfg");

        var contents = await File.ReadAllTextAsync(filePath, cancellationToken);

        contents = ServerNameRegex().Replace(contents, $"<server_options>$1<name>$$0BFRandomizer $$FF80.1.0</name>");

        contents = XmlRpcPortRegex().Replace(contents, $"<xmlrpc_port>{config.XmlRpcPort}</xmlrpc_port>");

        await File.WriteAllTextAsync(filePath, contents, cancellationToken);
    }

    public async Task SetupMatchSettingsAsync(CancellationToken cancellationToken = default)
    {
        var matchSettingsFilePath = Path.Combine(dedicatedServerDir, "GameData", "Tracks", "MatchSettings", "Randomizer.txt");

        var warmupChallengeGbxFile = await tmxRules.NextChallengeGbxAsync(cancellationToken);
        await File.WriteAllBytesAsync(Path.Combine(dedicatedServerDir, "GameData", "Tracks", warmupChallengeGbxFile.FileName), warmupChallengeGbxFile.Data, cancellationToken);

        var matchSettingsXml = $"""
        <?xml version="1.0" encoding="utf-8" ?>
        <playlist>
        	<gameinfos>
        		<game_mode>1</game_mode>
        		<chat_time>0</chat_time>
        		<rounds_pointslimit>2</rounds_pointslimit>
        		<rounds_usenewrules>1</rounds_usenewrules>
        		<timeattack_limit>0</timeattack_limit>
        		<timeattack_synchstartperiod>0</timeattack_synchstartperiod>
        		<team_pointslimit>5</team_pointslimit>
        		<team_maxpoints>6</team_maxpoints>
        		<team_usenewrules>0</team_usenewrules>
        		<laps_nblaps>5</laps_nblaps>
        		<laps_timelimit>0</laps_timelimit>
        	</gameinfos>

        	<hotseat>
        		<game_mode>1</game_mode>
        		<timeattack_limit>3</timeattack_limit>
        		<rounds_count>5</rounds_count>
        	</hotseat>

        	<filter>
        		<is_solo>0</is_solo>
        		<is_hotseat>0</is_hotseat>
        		<is_lan>0</is_lan>
        		<is_internet>1</is_internet>
        		<sort_index>200</sort_index>
        		<random_map_order>1</random_map_order>
        	</filter>

        	<challenge>
        		<file>{warmupChallengeGbxFile.FileName}</file>
        	</challenge>
        </playlist>
        """;

        await File.WriteAllTextAsync(matchSettingsFilePath, matchSettingsXml, cancellationToken);
    }

    public void StartServer()
    {
        var args = new List<string>
        {
            "/game=sunrise",
            "/game_settings=MatchSettings/Randomizer.txt",
            "/dedicated_cfg=dedicated.cfg",
            "/verbose_rpc"
        };

        if (config.BindIP is not null)
        {
            args.Add($"/bindip={config.BindIP}");
        }

        serverProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(dedicatedServerDir, dedicatedExeFileName),
                WorkingDirectory = dedicatedServerDir,
                Arguments = string.Join(' ', args),
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Minimized, // ProcessWindowStyle.Hidden
            }
        };

        serverProcess.Start();
    }

    public void StopServer()
    {
        var process = serverProcess;
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
            }
        }
        catch (InvalidOperationException)
        {
            // Process already exited
        }
        finally
        {
            process.Dispose();
            serverProcess = null;
        }
    }

    private static async Task DownloadWithProgressAsync(HttpContent content, Stream destination, CancellationToken cancellationToken)
    {
        var totalBytes = content.Headers.ContentLength;

        await using var source = await content.ReadAsStreamAsync(cancellationToken);

        var buffer = new byte[CopyBufferSize];
        long totalRead = 0;
        var lastReportedPercent = -1;
        int bytesRead;

        while ((bytesRead = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            totalRead += bytesRead;

            var downloadedMb = totalRead / 1024.0 / 1024.0;

            if (totalBytes is { } total && total > 0)
            {
                var percent = (int)(totalRead * 100 / total);
                if (percent == lastReportedPercent)
                {
                    continue;
                }

                lastReportedPercent = percent;
                Console.Write($"\rDownloading server files... {percent}% ({downloadedMb:F1} MB / {total / 1024.0 / 1024.0:F1} MB)");
            }
            else
            {
                Console.Write($"\rDownloading server files... {downloadedMb:F1} MB");
            }
        }

        Console.WriteLine();
    }

    private static async Task<DownloadCacheEntry?> ReadCacheEntryAsync(string cacheFilePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(cacheFilePath))
        {
            return null;
        }

        try
        {
            var tomlText = await File.ReadAllTextAsync(cacheFilePath, cancellationToken);
            return TomlSerializer.Deserialize<DownloadCacheEntry>(tomlText);
        }
        catch (TomlException)
        {
            return null;
        }
    }

    private static async Task WriteCacheEntryAsync(string cacheFilePath, DownloadCacheEntry entry, CancellationToken cancellationToken)
    {
        var tomlText = TomlSerializer.Serialize(entry);
        await File.WriteAllTextAsync(cacheFilePath, tomlText, cancellationToken);
    }

    private sealed record DownloadCacheEntry(string Url, string? ETag, DateTimeOffset? LastModified, long? ContentLength);

    [GeneratedRegex(@"<server_options>(\s*)<name>(.*?)<\/name>", RegexOptions.IgnoreCase)]
    private static partial Regex ServerNameRegex();

    [GeneratedRegex(@"<xmlrpc_port>(\d+)<\/xmlrpc_port>", RegexOptions.IgnoreCase)]
    private static partial Regex XmlRpcPortRegex();
}
