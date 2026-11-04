using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using NexusMods.Abstractions.Steam;
using SteamKit2;
using Xunit;

namespace NexusMods.Networking.Steam.Tests;

public class MISTSteamSessionStateTests
{
    private const BindingFlags Private =
        BindingFlags.Instance | BindingFlags.NonPublic;

    private static Session MakeSession() =>
        new(
            NullLogger<Session>.Instance,
            new FakeHandler(),
            new FakeStorage());

    private static TaskCompletionSource<bool> BeginFakeLogin(
        Session session)
    {
        var ready = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        Set(session, "_loginReady", ready);
        Set(session, "_connecting", true);

        return ready;
    }

    private static void Set(Session session, string field, object value)
    {
        var info = typeof(Session).GetField(field, Private)
            ?? throw new Exception($"Missing field: {field}");

        info.SetValue(session, value);
    }

    private static T Get<T>(Session session, string field)
    {
        var info = typeof(Session).GetField(field, Private)
            ?? throw new Exception($"Missing field: {field}");

        return (T)info.GetValue(session)!;
    }

    private static void Invoke(
        Session session,
        string method,
        object? argument)
    {
        var info = typeof(Session).GetMethod(method, Private)
            ?? throw new Exception($"Missing method: {method}");

        info.Invoke(session, [argument]);
    }

    [Fact]
    public void SuccessfulLoginCompletesPendingAttempt()
    {
        var session = MakeSession();
        var ready = BeginFakeLogin(session);

        Invoke(session, "ApplyLoginResult", EResult.OK);

        Assert.True(ready.Task.IsCompletedSuccessfully);
        Assert.True(Get<bool>(session, "_isLoggedOn"));
        Assert.False(Get<bool>(session, "_connecting"));
    }

    [Fact]
    public void FailedLoginRejectsPendingAttempt()
    {
        var session = MakeSession();
        var ready = BeginFakeLogin(session);

        Invoke(session, "ApplyLoginResult", EResult.Fail);

        Assert.True(ready.Task.IsFaulted);
        Assert.False(Get<bool>(session, "_isLoggedOn"));
        Assert.False(Get<bool>(session, "_connecting"));
    }

    [Fact]
    public void DisconnectFailsPendingLogin()
    {
        var session = MakeSession();
        var ready = BeginFakeLogin(session);

        Invoke(session, "DisconnectedCallback", null);

        Assert.True(ready.Task.IsFaulted);
        Assert.IsType<IOException>(
            ready.Task.Exception?.InnerException);

        Assert.False(Get<bool>(session, "_isLoggedOn"));
        Assert.False(Get<bool>(session, "_connecting"));
    }

    [Fact]
    public void LateLoginSuccessMustNotResurrectDisconnectedSession()
    {
        var session = MakeSession();
        var ready = BeginFakeLogin(session);

        // Simulate a disconnection before authentication completes.
        Invoke(session, "DisconnectedCallback", null);

        Assert.True(ready.Task.IsFaulted);

        // A delayed success callback from the old attempt arrives.
        Invoke(session, "ApplyLoginResult", EResult.OK);

        // The session must remain disconnected.
        Assert.False(Get<bool>(session, "_isLoggedOn"));
        Assert.False(Get<bool>(session, "_connecting"));
    }

    [Fact]
    public void LateFailureMustNotUndoSuccessfulLogin()
    {
        var session = MakeSession();
        var ready = BeginFakeLogin(session);

        Invoke(session, "ApplyLoginResult", EResult.OK);

        Assert.True(ready.Task.IsCompletedSuccessfully);
        Assert.True(Get<bool>(session, "_isLoggedOn"));

        // A delayed failure callback arrives after login succeeded.
        Invoke(session, "ApplyLoginResult", EResult.Fail);

        // It must not invalidate the established session.
        Assert.True(Get<bool>(session, "_isLoggedOn"));
        Assert.False(Get<bool>(session, "_connecting"));
        Assert.True(ready.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task DisconnectedSessionCannotReconnect()
    {
        var session = MakeSession();
        var ready = BeginFakeLogin(session);

        Invoke(session, "DisconnectedCallback", null);

        Assert.True(ready.Task.IsFaulted);

        await Assert.ThrowsAsync<IOException>(
            () => session.Connect(CancellationToken.None));
    }

    [Fact]
    public async Task RejectedLoginRetiresSession()
    {
        var session = MakeSession();
        var ready = BeginFakeLogin(session);

        Invoke(session, "ApplyLoginResult", EResult.Fail);

        Assert.True(ready.Task.IsFaulted);

        await Assert.ThrowsAsync<IOException>(
            () => session.Connect(CancellationToken.None));
    }

    private sealed class FakeHandler : IAuthInterventionHandler
    {
        public void ShowQRCode(Uri uri, CancellationToken token)
            => throw new InvalidOperationException(
                "Offline tests must not request authentication.");
    }

    private sealed class FakeStorage : IAuthStorage
    {
        public Task<(bool Success, byte[] Data)> TryLoad()
            => Task.FromResult((false, Array.Empty<byte>()));

        public Task SaveAsync(byte[] data)
            => Task.CompletedTask;
    }
}
