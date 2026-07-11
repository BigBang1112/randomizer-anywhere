using ManiaAPI.XmlRpc;

namespace RandomizerAnywhere;

internal sealed class SessionManager
{
    private readonly XmlRpcClient gbxRemote;
    private readonly TmxRules tmxRules;

    public SessionManager(XmlRpcClient gbxRemote, TmxRules tmxRules)
    {
        this.gbxRemote = gbxRemote;
        this.tmxRules = tmxRules;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        await gbxRemote.CallAsync<bool>("Authenticate", ["SuperAdmin", "SuperAdmin"], cancellationToken);
        await gbxRemote.CallAsync<bool>("EnableCallbacks", [true], cancellationToken);

        await foreach (var callback in gbxRemote.StreamCallbacksAsync(cancellationToken))
        {
            // Handle callbacks here
            Console.WriteLine($"{callback.MethodName} {string.Join(' ', callback.MethodParams)}");
        }

        //await NextRandomChallengeAsync();
    }

    public async Task NextRandomChallengeAsync(CancellationToken cancellationToken)
    {
        var nextChallenge = await tmxRules.NextChallengeGbxAsync(cancellationToken);
        await gbxRemote.CallAsync<bool>("WriteFile", [nextChallenge.FileName, nextChallenge.Data], cancellationToken);
        await gbxRemote.CallAsync<bool>("InsertChallenge", [nextChallenge.FileName], cancellationToken);
        await gbxRemote.CallAsync<bool>("NextChallenge", cancellationToken);
    }
}
