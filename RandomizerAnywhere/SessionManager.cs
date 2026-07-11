using ManiaAPI.XmlRpc;

namespace RandomizerAnywhere;

internal sealed class SessionManager
{
    private readonly XmlRpcClient client;
    private readonly TmxRules tmxRules;

    public SessionManager(XmlRpcClient client, TmxRules tmxRules)
    {
        this.client = client;
        this.tmxRules = tmxRules;
    }

    public async Task NextRandomChallengeAsync(CancellationToken cancellationToken)
    {
        var nextChallenge = await tmxRules.NextChallengeGbxAsync(cancellationToken);
        await client.CallAsync<bool>("WriteFile", [nextChallenge.FileName, nextChallenge.Data], cancellationToken);
        await client.CallAsync<bool>("InsertChallenge", [nextChallenge.FileName], cancellationToken);
        await client.CallAsync<bool>("NextChallenge", cancellationToken);
    }
}
