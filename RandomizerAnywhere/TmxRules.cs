using RandomizerAnywhere.Config;
using System.Buffers;
using System.Globalization;
using System.Text;

namespace RandomizerAnywhere;

internal sealed class TmxRules
{
    private readonly HttpClient http;
    private readonly AppConfig config;

    public required GameTitle Game { get; set; }

    public IReadOnlyDictionary<string, object> QueryParameters { get; set; } = new Dictionary<string, object>();

    public static IReadOnlyDictionary<string, object> GetDefaultQueryParameters(GameTitle game) =>
        new Dictionary<string, object>
        {
            ["primarytype"] = 0,
        };

    public TmxRules(HttpClient http, AppConfig config)
    {
        this.http = http;
        this.config = config;
    }

    public string BuildQuery()
    {
        var parameters = QueryParameters.Count > 0
            ? QueryParameters
            : GetDefaultQueryParameters(Game);

        var b = new StringBuilder();

        var first = true;

        foreach (var (key, value) in parameters)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            if (!first)
            {
                b.Append('&');
            }

            first = false;

            b.Append(Uri.EscapeDataString(key));
            b.Append('=');
            b.Append(Uri.EscapeDataString(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty));
        }

        return b.ToString();
    }

    public string GetRandomTrackUrl() => $"https://{GetSiteUrl()}/trackrandom";
    public string GetTrackGbxUrl(string trackId) => $"https://{GetSiteUrl()}/trackgbx/{trackId}";

    public string GetSiteUrl() => Game switch
    {
        GameTitle.TMNF => "tmnf.exchange",
        GameTitle.TMUF => "tmuf.exchange",
        GameTitle.TMN => "nations.tm-exchange.com",
        GameTitle.TMS => "sunrise.tm-exchange.com",
        GameTitle.TMO => "original.tm-exchange.com",
        _ => throw new ArgumentOutOfRangeException(nameof(Game), Game, "Unknown game title."),
    };

    public static SearchValues<char> InvalidFileNameCharSearchValues { get; } = SearchValues.Create([
        '\"', '<', '>', '|', '\0',
        (char)1, (char)2, (char)3, (char)4, (char)5, (char)6, (char)7, (char)8, (char)9, (char)10,
        (char)11, (char)12, (char)13, (char)14, (char)15, (char)16, (char)17, (char)18, (char)19, (char)20,
        (char)21, (char)22, (char)23, (char)24, (char)25, (char)26, (char)27, (char)28, (char)29, (char)30,
        (char)31, ':', '*', '?', '\\', '/'
    ]);

    public async Task<InMemoryFile> NextChallengeGbxAsync(CancellationToken cancellationToken = default)
    {
        var tmxRandomUrl = $"{GetRandomTrackUrl()}?{config.TmxQuery ?? BuildQuery()}";

        using var request = new HttpRequestMessage(HttpMethod.Head, tmxRandomUrl);
        using var response = await http.SendAsync(request, cancellationToken);

        var trackRelativePath = response.Headers.Location?.OriginalString ?? throw new Exception("Failed to get track relative path.");
        var trackId = trackRelativePath.Substring(trackRelativePath.LastIndexOf('/') + 1);

        Console.WriteLine("Next challenge track ID: " + trackId);

        using var trackResponse = await http.GetAsync(GetTrackGbxUrl(trackId), cancellationToken);
        var gbxBytes = await trackResponse.Content.ReadAsByteArrayAsync(cancellationToken);
        var fileName = trackResponse.Content.Headers.ContentDisposition?.FileNameStar
            ?? trackResponse.Content.Headers.ContentDisposition?.FileName ?? $"{trackId}.Gbx";

        return new InMemoryFile(GetValidFileName(fileName), gbxBytes);
    }

    private static string GetValidFileName(string fileName)
    {
        var buffer = ArrayPool<char>.Shared.Rent(fileName.Length);
        var bufferIndex = 0;

        foreach (var c in fileName)
        {
            buffer[bufferIndex++] = InvalidFileNameCharSearchValues.Contains(c) ? '_' : c;
        }

        var result = new string(buffer, 0, bufferIndex);
        ArrayPool<char>.Shared.Return(buffer);

        return result;
    }
}
