using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using NexusMods.Abstractions.Steam;
using NexusMods.Abstractions.Steam.DTOs;
using NexusMods.Abstractions.Steam.Values;
using NexusMods.Networking.Steam.DTOs;
using NexusMods.Networking.Steam.Exceptions;
using NexusMods.Paths;
using NexusMods.Sdk.IO;
using Polly;
using Polly.Retry;
using SteamKit2;
using SteamKit2.Authentication;
using SteamKit2.CDN;

namespace NexusMods.Networking.Steam;

/// <summary>
/// Base class for a Steam session. Steam works in a rather strange way. Internally it communicates
/// via websockets. This means that many of the operations require sending a message, then listening
/// on a separate callback handler for the response. This class attempts to abstract this process and
/// provide a cleaner interface for other parts of the app.
/// </summary>
public class Session : ISteamSession, IAsyncDisposable
{
    private bool _isLoggedOn = false;

    // MIST-004C.2: One callback pump and one shared login attempt.
    private readonly object _connectionGate = new();
    private readonly CancellationTokenSource _callbackStop = new();
    private Task? _callbackTask;
    private TaskCompletionSource<bool>? _loginReady;
    private bool _connecting;
    private bool _disposed;
    private bool _retired;

    internal readonly ILogger<Session> _logger;
    private readonly IAuthInterventionHandler _handler;

    /// <summary>
    /// Base steam component, this is used for communicating with the Steam network.
    /// </summary>
    private readonly SteamClient _steamClient;
    
    /// <summary>
    /// The component used for user related operations.
    /// </summary>
    private readonly SteamUser _steamUser;
    
    /// <summary>
    /// The component used for getting information about apps (games)
    /// </summary>
    private readonly SteamApps _steamApps;
    
    /// <summary>
    /// Component for getting content information (actual game data)
    /// </summary>
    private readonly SteamContent _steamContent;
    
    /// <summary>
    /// CDN data, used to get download locations for game data.
    /// </summary>
    private readonly Client _cdnClient;

    private readonly CallbackManager _callbacks;
    private readonly IAuthStorage _authStorage;
    private readonly CDNPool _cdnPool;

    private ConcurrentDictionary<(AppId, DepotId), byte[]> _depotKeys = new();
    private ConcurrentDictionary<(AppId, DepotId, ManifestId, string Branch), ulong> _manifestRequestCodes = new();
    internal readonly ResiliencePipeline _pipeline;

    public Session(ILogger<Session> logger, IAuthInterventionHandler handler, IAuthStorage storage)
    {
        _logger = logger;
        _handler = handler;
        _authStorage = storage;

        var steamConfiguration = SteamConfiguration.Create(configurator =>
        {
            // The client will dispose of these on its own
            configurator.WithHttpClientFactory(_ => new HttpClient());
        });
        _steamClient = new SteamClient(steamConfiguration);
        _steamUser = _steamClient.GetHandler<SteamUser>()!;
        _steamApps = _steamClient.GetHandler<SteamApps>()!;
        _steamContent = _steamClient.GetHandler<SteamContent>()!;
        _cdnClient = new Client(_steamClient);
        _cdnPool = new CDNPool(this);
        
        // Some parts of this interface use callbacks instead of more natural async methods. So we need to register
        // those callbacks here.
        _callbacks = new CallbackManager(_steamClient);
        _callbacks.Subscribe<SteamClient.ConnectedCallback>(
            callback => { _ = RunConnectedCallbackSafely(callback); });
        _callbacks.Subscribe<SteamClient.DisconnectedCallback>(DisconnectedCallback);
        _callbacks.Subscribe<SteamUser.LoggedOnCallback>(LoggedOnCallback);
        _callbacks.Subscribe<SteamApps.LicenseListCallback>(_ => { });

        _pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions())
            .AddTimeout(TimeSpan.FromSeconds(10))
            .Build();
    }

    /// <summary>
    /// The Steam Content module
    /// </summary>
    internal SteamContent Content => _steamContent;
    
    /// <summary>
    /// The CDN client, used for downloading game data.
    /// </summary>
    internal Client CDNClient => _cdnClient;
    
    /// <summary>
    /// The CDN pool, used for downloading game data.
    /// </summary>
    internal CDNPool CDNPool => _cdnPool;

    private void LoggedOnCallback(SteamUser.LoggedOnCallback callback)
        => ApplyLoginResult(callback.Result);

    // Kept separate so connection transitions can be tested offline.
    private void ApplyLoginResult(EResult result)
    {
        TaskCompletionSource<bool>? ready;

        lock (_connectionGate)
        {
            // Neither a late success nor a late failure may
            // modify a session without a pending login.
            if (_disposed || !_connecting)
                return;

            _connecting = false;
            _isLoggedOn = result == EResult.OK;
            if (result != EResult.OK)
                _retired = true;
            ready = _loginReady;
        }

        if (result == EResult.OK)
        {
            ready?.TrySetResult(true);
            _logger.LogInformation("Logged on to Steam network.");
        }
        else
        {
            var exception = new InvalidOperationException(
                $"Steam login failed: {result}");

            ready?.TrySetException(exception);

            _logger.LogWarning(
                "Steam login rejected: {Result}", result);
        }
    }

    private void DisconnectedCallback(SteamClient.DisconnectedCallback callback)
    {
        TaskCompletionSource<bool>? ready;
        lock (_connectionGate)
        {
            _retired = true;
            _isLoggedOn = false;
            _connecting = false;
            ready = _loginReady;
        }

        ready?.TrySetException(
            new IOException("Steam disconnected before login completed."));

        _logger.LogWarning("Disconnected from Steam network.");
    }

    private void FailConnection(Exception exception)
    {
        TaskCompletionSource<bool>? ready;
        lock (_connectionGate)
        {
            _retired = true;
            _isLoggedOn = false;
            _connecting = false;
            ready = _loginReady;
        }

        ready?.TrySetException(exception);
        _logger.LogError(exception, "Steam connection failed.");
    }

    private bool CanAuthenticate()
    {
        lock (_connectionGate)
        {
            return !_disposed && !_retired && _connecting;
        }
    }

    private async Task RunConnectedCallbackSafely(
        SteamClient.ConnectedCallback callback)
    {
        try
        {
            await ConnectedCallback(callback);
        }
        catch (Exception exception)
        {
            FailConnection(exception);
        }
    }

    private async Task ConnectedCallback(SteamClient.ConnectedCallback callback)
    {
        lock (_connectionGate)
        {
            if (_disposed || _retired || !_connecting) return;
        }

        var (success, data) = await _authStorage.TryLoad();

        if (!CanAuthenticate())
            return;
        if (success)
        {
            _logger.LogInformation("Using saved auth data to log in.");
            var authData = AuthData.Load(data);
            
            if (!CanAuthenticate()) return;
            _steamUser.LogOn(new SteamUser.LogOnDetails
                {
                    Username = authData.Username,
                    AccessToken = authData.RefreshToken,
                }
            );
        }
        else if (Environment.GetEnvironmentVariable("STEAM_USER") != null)
        {
            _logger.LogInformation("Using environment variables to log in.");
            var authSession = await _steamClient.Authentication.BeginAuthSessionViaCredentialsAsync(new AuthSessionDetails
            {
                Username = Environment.GetEnvironmentVariable("STEAM_USER"),
                Password = Environment.GetEnvironmentVariable("STEAM_PASS"),
            });
            
            var pollResponse = await authSession.PollingWaitForResultAsync();
            if (!CanAuthenticate()) return;
            await _authStorage.SaveAsync(new AuthData
            {
                Username = pollResponse.AccountName,
                RefreshToken = pollResponse.RefreshToken,
            }.Save());
            
            if (!CanAuthenticate()) return;
            _steamUser.LogOn(new SteamUser.LogOnDetails
                {
                    Username = pollResponse.AccountName,
                    AccessToken = pollResponse.RefreshToken,
                }
            );
        }
        else
        {
            _logger.LogInformation("No saved auth data, logging in via QR code.");
            var authSession = await _steamClient.Authentication.BeginAuthSessionViaQRAsync(new AuthSessionDetails());

            authSession.ChallengeURLChanged = () => { if (CanAuthenticate()) _handler.ShowQRCode(new Uri(authSession.ChallengeURL, UriKind.Absolute), CancellationToken.None); };

            if (!CanAuthenticate()) return;
            _handler.ShowQRCode(new Uri(authSession.ChallengeURL, UriKind.Absolute), CancellationToken.None);
            var pollResponse = await authSession.PollingWaitForResultAsync();
            if (!CanAuthenticate()) return;

            await _authStorage.SaveAsync(new AuthData
            {
                Username = pollResponse.AccountName,
                RefreshToken = pollResponse.RefreshToken,
            }.Save());
            
            if (!CanAuthenticate()) return;
            _steamUser.LogOn(new SteamUser.LogOnDetails
                {
                    Username = pollResponse.AccountName,
                    AccessToken = pollResponse.RefreshToken,
                }
            );
        }
    }

    /// <summary>
    /// Wraps an async action in a synchronous action.
    /// </summary>
    /// <inheritdoc />
    public async Task Connect(CancellationToken token)
    {
        await ConnectedAsync(token);
    }

    public async Task<ProductInfo> GetProductInfoAsync(AppId appId, CancellationToken cancellationToken = default)
    {
        await ConnectedAsync(cancellationToken);

        var jobs = await _steamApps.PICSGetProductInfo(new SteamApps.PICSRequest(appId.Value), null);
        if (jobs.Failed) throw new Exception($"Failed to get product info for app `{appId}`");

        var results = jobs.Results;
        if (results is null || results.Count == 0) throw new Exception($"Found no product info for app `{appId}`");

        return ProductInfoParser.Parse(results[0]);
    }

    /// <summary>
    /// Performs a login if required, returns once the login is complete
    /// </summary>
    private async Task PumpCallbacksAsync()
    {
        try
        {
            while (!_callbackStop.IsCancellationRequested)
            {
                await _callbacks.RunWaitCallbackAsync(
                    _callbackStop.Token);
            }
        }
        catch (OperationCanceledException)
            when (_callbackStop.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        catch (Exception exception)
        {
            FailConnection(exception);
        }
    }

    private async Task ConnectedAsync(CancellationToken cancellationToken)
    {
        Task readyTask;

        lock (_connectionGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_retired)
                throw new IOException(
                    "Steam session is no longer usable; create a new session.");

            if (_isLoggedOn)
                return;

            if (_callbackTask is null || _callbackTask.IsCompleted)
                _callbackTask = Task.Run(PumpCallbacksAsync);

            if (!_connecting)
            {
                _connecting = true;
                _loginReady = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

                try
                {
                    _steamClient.Connect();
                }
                catch (Exception exception)
                {
                    _connecting = false;
                    _loginReady.TrySetException(exception);
                }
            }

            readyTask = _loginReady!.Task;
        }

        // Cancellation stops this caller's wait, not the shared
        // Steam connection attempt.
        await readyTask.WaitAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        Task? pump;

        lock (_connectionGate)
        {
            if (_disposed) return;
            _disposed = true;
            _loginReady?.TrySetException(
                new ObjectDisposedException(nameof(Session)));
            pump = _callbackTask;
        }

        _callbackStop.Cancel();

        try
        {
            _steamClient.Disconnect();
        }
        finally
        {
            if (pump is not null)
                await pump;

            _callbackStop.Dispose();
        }
    }

    public async Task<ulong> GetManifestRequestCodeAsync(AppId appId, DepotId depotId, ManifestId manifestId, string branch)
    {
        if (_manifestRequestCodes.TryGetValue((appId, depotId, manifestId, branch), out var found))
            return found;
        await ConnectedAsync(CancellationToken.None);
        
        var requestCodeResult = await _steamContent.GetManifestRequestCode(depotId.Value, appId.Value, manifestId.Value, branch);
        if (requestCodeResult == 0)
        {
            _logger.LogWarning("Failed to get request code for depot {0} manifest {1}", depotId.Value, manifestId.Value);
            throw new FailedToGetRequestCode(appId, depotId, manifestId);
        }

        _logger.LogInformation("Got request code depot {1} manifest {2}", depotId.Value, manifestId.Value);

        _manifestRequestCodes.TryAdd((appId, depotId, manifestId, branch), requestCodeResult);
        return requestCodeResult;
    }
    
    /// <summary>
    /// Get a depot decryption key for a given depot.
    /// </summary>
    public async Task<byte[]> GetDepotKey(AppId appId, DepotId depotId)
    {
        // Try to get the cached key first
        
        if (_depotKeys.TryGetValue((appId, depotId), out var keyBytes))
            return keyBytes;
        await ConnectedAsync(CancellationToken.None);
        
        var key = await _steamApps.GetDepotDecryptionKey(depotId.Value, appId.Value);
        if (key.Result != EResult.OK)
        {
            _logger.LogWarning("Failed to get depot key for depot `{DepotId}`", depotId.Value);
            throw new Exception($"Failed to get depot key for depot `{depotId.Value}`");
        }

        _logger.LogInformation("Got depot key for depot `{DepotId}`", depotId.Value);
        _depotKeys.TryAdd((appId, depotId), key.DepotKey);
        
        return key.DepotKey;
    }

    public async Task<Manifest> GetManifestContents(AppId appId, DepotId depotId, ManifestId manifestId, string branch, CancellationToken token = default)
    {
        await ConnectedAsync(token);
        return await _pipeline.ExecuteAsync(async token => await _cdnPool.GetManifestContents(appId, depotId, manifestId,
                branch, token
            ), token
        );
    }

    public Stream GetFileStream(AppId appId, Manifest manifest, RelativePath file)
    {
        var chunkedProvider = new DepotChunkProvider(this, appId, manifest.DepotId,
            manifest, file
        );
        // 48 1MB chunks, 32 preloaded
        return new ChunkedStream<DepotChunkProvider>(chunkedProvider, capacity: 48, preFetch: 32);
    }
}
