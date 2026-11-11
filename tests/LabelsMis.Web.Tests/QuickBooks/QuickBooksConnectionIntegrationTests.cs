using LabelsMis.Domain.Enums;
using LabelsMis.Domain.QuickBooks;
using LabelsMis.Web.Background;
using LabelsMis.Web.Services.QuickBooks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace LabelsMis.Web.Tests.QuickBooks;

[Collection(QuickBooksConnectionRowCollection.Name)]
public class QuickBooksConnectionServiceTests : QuickBooksTestHost
{
    [Fact]
    public async Task BeginConnectAsync_WithKeysSaved_BuildsTheAuthorizeUrlForThatClient()
    {
        await SaveKeysAsync();

        var url = await WithConnectionServiceAsync(s => s.BeginConnectAsync(RedirectUri, "state-1"));

        url.Should().Be($"https://intuit.test/authorize?client_id=client-1&redirect_uri={RedirectUri}&state=state-1");
    }

    [Fact]
    public async Task BeginConnectAsync_WithoutKeys_SaysWhatIsMissing()
    {
        var act = () => WithConnectionServiceAsync(s => s.BeginConnectAsync(RedirectUri, "state-1"));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Save the Intuit client ID and secret*");
    }

    [Fact]
    public async Task CompleteConnectAsync_StoresEncryptedTokensTheCompanyAndItsCheck()
    {
        await SaveKeysAsync();

        var result = await WithConnectionServiceAsync(s => s.CompleteConnectAsync("code-1", RealmId, RedirectUri));

        result.Should().Be(new QuickBooksConnectResult("Sandbox Company US ef89", null));
        Auth.Exchanges.Should().Equal(("client-1", "secret-0123456789", "code-1", RedirectUri));
        var connection = await ReadConnectionAsync();
        connection!.Should().Satisfy<Domain.Entities.QuickBooksConnection>(c =>
        {
            c.State.Should().Be(QuickBooksConnectionState.Connected);
            c.RealmIdProtected.Should().StartWith("v1:").And.NotContain(RealmId);
            Protector.Unprotect(c.RealmIdProtected!).Should().Be(RealmId);
            c.CompanyName.Should().Be("Sandbox Company US ef89");
            c.ConnectedById.Should().Be(TestUserId);
            c.AccessTokenProtected.Should().StartWith("v1:").And.NotContain("access-1");
            c.RefreshTokenProtected.Should().StartWith("v1:").And.NotContain("refresh-1");
            Protector.Unprotect(c.AccessTokenProtected!).Should().Be("access-1");
            Protector.Unprotect(c.RefreshTokenProtected!).Should().Be("refresh-1");
            c.AccessTokenExpiresAt.Should().Be(Now.AddHours(1));
            c.RefreshTokenExpiresAt.Should().Be(Now.AddSeconds(8_726_400));
            c.RefreshTokenHardExpiresAt.Should().Be(Now.AddSeconds(157_680_000));
            QuickBooksCompanyCheck.FromJson(c.CompanyCheckJson)!.ReadyForInvoices.Should().BeTrue();
            c.CompanyCheckedAt.Should().Be(Now);
        });
    }

    [Fact]
    public async Task CompleteConnectAsync_LogsTheConnectAndBothReads()
    {
        await ConnectAsync();

        var log = await ReadLogAsync();

        log.Select(l => (l.Operation, l.Succeeded, l.Automatic)).Should().BeEquivalentTo(new[]
        {
            (QuickBooksOperations.Connect, true, false),
            (QuickBooksOperations.CompanyRead, true, false),
            (QuickBooksOperations.PreferencesRead, true, false)
        });
        log.Should().OnlyContain(l => l.CreatedById == TestUserId && l.IntuitTid != null);
        log.Should().NotContain(l => (l.Message ?? "").Contains("access-") || (l.Message ?? "").Contains("refresh-"));
    }

    [Fact]
    public async Task CompleteConnectAsync_WhenTheCompanyCannotBeReadYet_StillConnectsAndWarns()
    {
        await SaveKeysAsync();
        // Stands in for a read that still failed after the real client's own retries.
        Api.ReadFailures.Enqueue(QuickBooksApiException.FromResponse(503, "", "tid-503"));

        var result = await WithConnectionServiceAsync(s => s.CompleteConnectAsync("code-1", RealmId, RedirectUri));

        result.CompanyName.Should().BeNull();
        result.Warning.Should().Be("QuickBooks answered 503.");
        (await ReadConnectionAsync())!.State.Should().Be(QuickBooksConnectionState.Connected);
    }

    [Fact]
    public async Task CompleteConnectAsync_ForTheSameCompanyAgain_KeepsWhatWasKnownAboutIt()
    {
        await ConnectAsync();
        // The reads after the second connect fail, so anything still on the row was kept, not re-read.
        Api.ReadFailures.Enqueue(QuickBooksApiException.FromResponse(503, "", "tid-503"));

        await WithConnectionServiceAsync(s => s.CompleteConnectAsync("code-2", RealmId, RedirectUri));

        (await ReadConnectionAsync())!.Should().Satisfy<Domain.Entities.QuickBooksConnection>(c =>
        {
            c.State.Should().Be(QuickBooksConnectionState.Connected);
            c.CompanyName.Should().Be("Sandbox Company US ef89");
            c.CompanyCheckJson.Should().NotBeNull();
        });
    }

    [Fact]
    public async Task CompleteConnectAsync_ForADifferentCompany_ForgetsTheOldOne()
    {
        await ConnectAsync();
        Api.ReadFailures.Enqueue(QuickBooksApiException.FromResponse(503, "", "tid-503"));

        await WithConnectionServiceAsync(s => s.CompleteConnectAsync("code-2", "4620816365000000001", RedirectUri));

        (await ReadConnectionAsync())!.Should().Satisfy<Domain.Entities.QuickBooksConnection>(c =>
        {
            c.State.Should().Be(QuickBooksConnectionState.Connected);
            Protector.Unprotect(c.RealmIdProtected!).Should().Be("4620816365000000001");
            c.CompanyName.Should().BeNull();
            c.CompanyCheckJson.Should().BeNull();
        });
    }

    [Fact]
    public async Task CompleteConnectAsync_WithNoCompanyInTheAnswer_SpendsNoCode()
    {
        await SaveKeysAsync();

        var act = () => WithConnectionServiceAsync(s => s.CompleteConnectAsync("code-1", "  ", RedirectUri));

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*which company*");
        Auth.Exchanges.Should().BeEmpty();
    }

    [Fact]
    public async Task GetViewAsync_ShowsTheCompanyIdInTheClearToThePage()
    {
        await ConnectAsync();

        var view = await WithConnectionServiceAsync(s => s.GetViewAsync());

        view.RealmId.Should().Be(RealmId);
    }

    [Fact]
    public async Task CompleteConnectAsync_WhenIntuitRefusesTheCode_ConnectsNothingAndLogsIt()
    {
        await SaveKeysAsync();
        Auth.ExchangeFailure = InvalidGrant();

        var act = () => WithConnectionServiceAsync(s => s.CompleteConnectAsync("code-1", RealmId, RedirectUri));

        await act.Should().ThrowAsync<QuickBooksAuthException>();
        (await ReadConnectionAsync())!.State.Should().Be(QuickBooksConnectionState.NotConnected);
        (await ReadLogAsync()).Should().ContainSingle()
            .Which.Should().Satisfy<Domain.Entities.QuickBooksSyncLog>(l =>
            {
                l.Operation.Should().Be(QuickBooksOperations.Connect);
                l.Succeeded.Should().BeFalse();
                l.ErrorCode.Should().Be("invalid_grant");
                l.HttpStatus.Should().Be(400);
            });
    }

    [Fact]
    public async Task TestAsync_ReadsTheCompanySettingsAgain()
    {
        await ConnectAsync();
        Api.Preferences = """{"Preferences":{"SalesFormsPrefs":{"CustomTxnNumbers":false}}}""";
        Time.Advance(TimeSpan.FromMinutes(5));

        var result = await WithConnectionServiceAsync(s => s.TestAsync());

        result.Check.CustomTransactionNumbers.Should().BeFalse();
        var connection = await ReadConnectionAsync();
        QuickBooksCompanyCheck.FromJson(connection!.CompanyCheckJson)!.ReadyForInvoices.Should().BeFalse();
        connection.CompanyCheckedAt.Should().Be(Now);
    }

    [Fact]
    public async Task TestAsync_WhenNothingIsConnected_SaysSo()
    {
        await SaveKeysAsync();

        var act = () => WithConnectionServiceAsync(s => s.TestAsync());

        await act.Should().ThrowAsync<QuickBooksNotConnectedException>();
        Api.Reads.Should().BeEmpty();
    }

    [Fact]
    public async Task TestAsync_WhenAReconnectIsFlaggedButIntuitAcceptsTheSavedSignIn_BringsTheConnectionBack()
    {
        await ConnectAsync();
        await EndConnectionAsync();
        (await ReadConnectionAsync())!.State.Should().Be(QuickBooksConnectionState.NeedsReconnect);

        var result = await WithConnectionServiceAsync(s => s.TestAsync());

        result.CompanyName.Should().Be("Sandbox Company US ef89");
        (await ReadConnectionAsync())!.Should().Satisfy<Domain.Entities.QuickBooksConnection>(c =>
        {
            c.State.Should().Be(QuickBooksConnectionState.Connected);
            c.RefusedSince.Should().BeNull();
            c.LastError.Should().BeNull();
        });
    }

    [Fact]
    public async Task NeedsReconnectAsync_IsTrueOnlyOnceTheSignInHasEnded()
    {
        await ConnectAsync();
        (await WithConnectionServiceAsync(s => s.NeedsReconnectAsync())).Should().BeFalse();

        await EndConnectionAsync();

        (await WithConnectionServiceAsync(s => s.NeedsReconnectAsync())).Should().BeTrue();
    }

    [Fact]
    public async Task DisconnectAsync_RevokesTheRefreshTokenAndForgetsTheCompanyButKeepsTheKeys()
    {
        await ConnectAsync();

        var result = await WithConnectionServiceAsync(s => s.DisconnectAsync());

        result.Should().Be(new QuickBooksDisconnectResult(true, "Sandbox Company US ef89", null));
        Auth.Revoked.Should().Equal("refresh-1");
        (await ReadConnectionAsync())!.Should().Satisfy<Domain.Entities.QuickBooksConnection>(c =>
        {
            c.State.Should().Be(QuickBooksConnectionState.NotConnected);
            c.RealmIdProtected.Should().BeNull();
            c.AccessTokenProtected.Should().BeNull();
            c.RefreshTokenProtected.Should().BeNull();
            c.CompanyCheckJson.Should().BeNull();
            c.ClientId.Should().Be("client-1");
        });
        (await ReadLogAsync()).Should().Contain(l => l.Operation == QuickBooksOperations.Disconnect && l.Succeeded);
    }

    [Fact]
    public async Task DisconnectAsync_WhenIntuitCannotBeTold_StillForgetsTheTokensAndSaysSo()
    {
        await ConnectAsync();
        Auth.RevokeFailure = new QuickBooksAuthException("QuickBooks sign-in could not be reached.", null, null, null);

        var result = await WithConnectionServiceAsync(s => s.DisconnectAsync());

        result.RevokeProblem.Should().Be("QuickBooks sign-in could not be reached.");
        (await ReadConnectionAsync())!.RefreshTokenProtected.Should().BeNull();
        (await ReadLogAsync()).Should().Contain(l =>
            l.Operation == QuickBooksOperations.Disconnect && !l.Succeeded && l.Message!.StartsWith("Removed here"));
    }

    [Fact]
    public async Task DisconnectAsync_WhenNothingIsConnected_DoesNothing()
    {
        await SaveKeysAsync();

        var result = await WithConnectionServiceAsync(s => s.DisconnectAsync());

        result.WasConnected.Should().BeFalse();
        Auth.Revoked.Should().BeEmpty();
    }

    [Fact]
    public async Task GetViewAsync_CarriesTheStateAndTheCheckButNoSecret()
    {
        await ConnectAsync();

        var view = await WithConnectionServiceAsync(s => s.GetViewAsync());

        view.Should().Satisfy<QuickBooksConnectionView>(v =>
        {
            v.State.Should().Be(QuickBooksConnectionState.Connected);
            v.IsLinked.Should().BeTrue();
            v.CompanyName.Should().Be("Sandbox Company US ef89");
            v.ConnectedBy.Should().Be("test@labels-mis.local");
            v.ClientSecretTail.Should().Be("6789");
            v.CompanyCheck!.PoNumberField!.Name.Should().Be("P.O. Number");
            v.ToString().Should().NotContain("secret-0123456789").And.NotContain("access-1").And.NotContain("refresh-1");
        });
    }

    /// <summary>Two refusals, the second after the grace period: the connection is flagged.</summary>
    private async Task EndConnectionAsync()
    {
        Auth.RefreshFailures.Enqueue(InvalidGrant());
        Auth.RefreshFailures.Enqueue(InvalidGrant());
        await Tokens.Invoking(t => t.RefreshAsync(TestUserId)).Should().ThrowAsync<QuickBooksAuthException>();
        Time.Advance(TimeSpan.FromMinutes(11));
        await Tokens.Invoking(t => t.RefreshAsync(TestUserId)).Should().ThrowAsync<QuickBooksNotConnectedException>();
    }
}

[Collection(QuickBooksConnectionRowCollection.Name)]
public class QuickBooksTokenServiceTests : QuickBooksTestHost
{
    [Fact]
    public async Task GetAccessAsync_WithAFreshToken_DoesNotCallIntuit()
    {
        await ConnectAsync();

        var access = await Tokens.GetAccessAsync(TestUserId);

        access.Should().Be(new QuickBooksCompanyAccess(QuickBooksEnvironment.Sandbox, RealmId, "access-1"));
        Auth.RefreshTokensUsed.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAccessAsync_WhenTheTokenHasRunOut_RefreshesAndStoresTheNewPairFirst()
    {
        await ConnectAsync();
        Time.Advance(TimeSpan.FromMinutes(59));

        var access = await Tokens.GetAccessAsync(TestUserId);

        access.AccessToken.Should().Be("access-2");
        Auth.RefreshTokensUsed.Should().Equal("refresh-1");
        (await ReadConnectionAsync())!.Should().Satisfy<Domain.Entities.QuickBooksConnection>(c =>
        {
            Protector.Unprotect(c.AccessTokenProtected!).Should().Be("access-2");
            Protector.Unprotect(c.RefreshTokenProtected!).Should().Be("refresh-2");
            c.LastRefreshedAt.Should().Be(Now);
            c.AccessTokenExpiresAt.Should().Be(Now.AddHours(1));
        });
        (await ReadLogAsync()).Should().ContainSingle(l => l.Operation == QuickBooksOperations.TokenRefresh)
            .Which.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task GetAccessAsync_FromManyCallersAtOnce_RefreshesOnlyOnce()
    {
        await ConnectAsync();
        Time.Advance(TimeSpan.FromHours(2));
        Auth.RefreshDelay = TimeSpan.FromMilliseconds(150);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => Tokens.GetAccessAsync(TestUserId))));

        results.Select(r => r.AccessToken).Should().OnlyContain(token => token == "access-2");
        Auth.RefreshTokensUsed.Should().Equal("refresh-1");
    }

    [Fact]
    public async Task GetAccessAsync_FromTwoCopiesOfTheAppAtOnce_RefreshesOnlyOnce()
    {
        await ConnectAsync();
        Time.Advance(TimeSpan.FromHours(2));
        Auth.RefreshDelay = TimeSpan.FromMilliseconds(400);
        await using var secondApp = BuildApp();
        var otherCopy = secondApp.GetRequiredService<QuickBooksTokenService>();

        var results = await Task.WhenAll(
            Task.Run(() => Tokens.GetAccessAsync(TestUserId)),
            Task.Run(() => otherCopy.GetAccessAsync(TestUserId)));

        // Each copy has its own in-process gate, so only the row lock stands between them.
        results.Select(r => r.AccessToken).Should().OnlyContain(token => token == "access-2");
        Auth.RefreshTokensUsed.Should().Equal("refresh-1");
    }

    [Fact]
    public async Task GetAccessAsync_WhenNothingIsConnected_Throws()
    {
        await SaveKeysAsync();

        var act = () => Tokens.GetAccessAsync(TestUserId);

        await act.Should().ThrowAsync<QuickBooksNotConnectedException>().WithMessage("QuickBooks is not connected*");
    }

    [Fact]
    public async Task RefreshAsync_WhenAnotherCallerAlreadyReplacedTheRefusedToken_ReturnsTheirsWithoutCallingIntuit()
    {
        await ConnectAsync();
        Time.Advance(TimeSpan.FromHours(2));
        await Tokens.GetAccessAsync(TestUserId);

        var access = await Tokens.RefreshAsync(TestUserId, refusedAccessToken: "access-1");

        access.AccessToken.Should().Be("access-2");
        Auth.RefreshTokensUsed.Should().HaveCount(1);
    }

    [Fact]
    public async Task RefreshAsync_WhenTheRefusedTokenIsTheOneOnFile_RefreshesIt()
    {
        await ConnectAsync();

        var access = await Tokens.RefreshAsync(TestUserId, refusedAccessToken: "access-1");

        access.AccessToken.Should().Be("access-2");
        Auth.RefreshTokensUsed.Should().Equal("refresh-1");
    }

    [Fact]
    public async Task RefreshAsync_WhenIntuitRefusesOnce_KeepsTheConnectionAndStartsTheClock()
    {
        await ConnectAsync();
        Auth.RefreshFailures.Enqueue(InvalidGrant());

        var act = () => Tokens.RefreshAsync(TestUserId, automatic: true);

        (await act.Should().ThrowAsync<QuickBooksAuthException>()).Which.IsInvalidGrant.Should().BeTrue();
        (await ReadConnectionAsync())!.Should().Satisfy<Domain.Entities.QuickBooksConnection>(c =>
        {
            c.State.Should().Be(QuickBooksConnectionState.Connected);
            c.RefusedSince.Should().Be(Now);
            c.LastError.Should().Contain("invalid_grant");
            Protector.Unprotect(c.RefreshTokenProtected!).Should().Be("refresh-1");
        });
        (await ReadLogAsync()).Should().ContainSingle(l => l.Operation == QuickBooksOperations.TokenRefresh)
            .Which.Should().Satisfy<Domain.Entities.QuickBooksSyncLog>(l =>
            {
                l.Succeeded.Should().BeFalse();
                l.Automatic.Should().BeTrue();
                l.ErrorCode.Should().Be("invalid_grant");
                l.IntuitTid.Should().Be("tid-refused");
            });
    }

    [Fact]
    public async Task RefreshAsync_WhenIntuitStillRefusesAfterTheGrace_FlagsAReconnectAndStopsAskingIntuit()
    {
        await ConnectAsync();
        Auth.RefreshFailures.Enqueue(InvalidGrant());
        Auth.RefreshFailures.Enqueue(InvalidGrant());
        await Tokens.Invoking(t => t.RefreshAsync(TestUserId)).Should().ThrowAsync<QuickBooksAuthException>();
        Time.Advance(TimeSpan.FromMinutes(11));

        var act = () => Tokens.RefreshAsync(TestUserId);

        await act.Should().ThrowAsync<QuickBooksNotConnectedException>().WithMessage("*Connect again*");
        (await ReadConnectionAsync())!.Should().Satisfy<Domain.Entities.QuickBooksConnection>(c =>
        {
            c.State.Should().Be(QuickBooksConnectionState.NeedsReconnect);
            c.AccessTokenProtected.Should().BeNull();
            Protector.Unprotect(c.RealmIdProtected!).Should().Be(RealmId);
        });

        await Tokens.Invoking(t => t.GetAccessAsync(TestUserId)).Should().ThrowAsync<QuickBooksNotConnectedException>();
        Auth.RefreshTokensUsed.Should().HaveCount(2);
    }

    [Fact]
    public async Task RefreshAsync_WhenIntuitCannotBeReached_RecordsTheProblemWithoutStartingTheClock()
    {
        await ConnectAsync();
        Auth.RefreshFailures.Enqueue(new QuickBooksAuthException("QuickBooks sign-in could not be reached.", null, null, null));

        var act = () => Tokens.RefreshAsync(TestUserId);

        (await act.Should().ThrowAsync<QuickBooksAuthException>()).Which.IsTransient.Should().BeTrue();
        (await ReadConnectionAsync())!.Should().Satisfy<Domain.Entities.QuickBooksConnection>(c =>
        {
            c.State.Should().Be(QuickBooksConnectionState.Connected);
            c.RefusedSince.Should().BeNull();
            c.LastError.Should().Be("QuickBooks sign-in could not be reached.");
        });
    }

    [Fact]
    public async Task RefreshAsync_AfterARefusal_ASuccessClearsIt()
    {
        await ConnectAsync();
        Auth.RefreshFailures.Enqueue(InvalidGrant());
        await Tokens.Invoking(t => t.RefreshAsync(TestUserId)).Should().ThrowAsync<QuickBooksAuthException>();
        Time.Advance(TimeSpan.FromMinutes(11));

        await Tokens.RefreshAsync(TestUserId);

        (await ReadConnectionAsync())!.Should().Satisfy<Domain.Entities.QuickBooksConnection>(c =>
        {
            c.State.Should().Be(QuickBooksConnectionState.Connected);
            c.RefusedSince.Should().BeNull();
            c.LastError.Should().BeNull();
        });
    }
}

[Collection(QuickBooksConnectionRowCollection.Name)]
public class QuickBooksGatewayTests : QuickBooksTestHost
{
    [Fact]
    public async Task ReadAsync_UsesTheSavedTokenAndLogsTheCall()
    {
        await ConnectAsync();
        Api.Reads.Clear();

        var response = await WithGatewayAsync(g => g.ReadAsync("preferences", QuickBooksOperations.PreferencesRead));

        response.StatusCode.Should().Be(200);
        Api.Reads.Should().Equal(("preferences", "access-1"));
        (await ReadLogAsync()).Count(l => l.Operation == QuickBooksOperations.PreferencesRead && l.Succeeded && l.DurationMs == 12)
            .Should().Be(2, "the connect read it once and this call once");
    }

    [Fact]
    public async Task ReadAsync_WhenQuickBooksAnswers401_RefreshesAndTriesOnceMore()
    {
        await ConnectAsync();
        Api.Reads.Clear();
        Api.ReadFailures.Enqueue(Unauthorized());

        var response = await WithGatewayAsync(g => g.ReadAsync("preferences", QuickBooksOperations.PreferencesRead));

        response.StatusCode.Should().Be(200);
        Api.Reads.Should().Equal(("preferences", "access-1"), ("preferences", "access-2"));
        Auth.RefreshTokensUsed.Should().Equal("refresh-1");
    }

    [Fact]
    public async Task ReadAsync_WhenQuickBooksAnswers401Twice_GivesUpAndLogsTheFailure()
    {
        await ConnectAsync();
        Api.ReadFailures.Enqueue(Unauthorized());
        Api.ReadFailures.Enqueue(Unauthorized());

        var act = () => WithGatewayAsync(g => g.ReadAsync("preferences", QuickBooksOperations.PreferencesRead));

        (await act.Should().ThrowAsync<QuickBooksApiException>()).Which.Kind.Should().Be(QuickBooksErrorKind.Unauthorized);
        Auth.RefreshTokensUsed.Should().HaveCount(1);
        (await ReadLogAsync()).Should().Contain(l =>
            l.Operation == QuickBooksOperations.PreferencesRead && !l.Succeeded && l.HttpStatus == 401 && l.IntuitTid == "tid-401");
    }

    [Fact]
    public async Task ReadAsync_WhenNothingIsConnected_ThrowsAndLogsTheAttempt()
    {
        await SaveKeysAsync();

        var act = () => WithGatewayAsync(g => g.ReadAsync("preferences", QuickBooksOperations.PreferencesRead));

        await act.Should().ThrowAsync<QuickBooksNotConnectedException>();
        Api.Reads.Should().BeEmpty();
        (await ReadLogAsync()).Should().ContainSingle()
            .Which.Should().Satisfy<Domain.Entities.QuickBooksSyncLog>(l =>
            {
                l.Succeeded.Should().BeFalse();
                l.Message.Should().StartWith("QuickBooks is not connected");
            });
    }

    [Fact]
    public async Task ReadAsync_ForBackgroundWork_FilesTheLogUnderTheGivenCallerAsAutomatic()
    {
        await ConnectAsync();

        await WithGatewayAsync(g => g.ReadAsync(
            "preferences", QuickBooksOperations.PreferencesRead, new QuickBooksCaller(TestUserId, Automatic: true)));

        (await ReadLogAsync()).Should().ContainSingle(l => l.Automatic)
            .Which.Operation.Should().Be(QuickBooksOperations.PreferencesRead);
    }
}

[Collection(QuickBooksConnectionRowCollection.Name)]
public class QuickBooksKeepAliveServiceTests : QuickBooksTestHost
{
    private QuickBooksKeepAliveService Service() =>
        new(ScopeFactory, Tokens, Time, NullLogger<QuickBooksKeepAliveService>.Instance);

    [Fact]
    public async Task RunOnceAsync_WhenNothingIsConnected_DoesNotCallIntuit()
    {
        (await Service().RunOnceAsync(CancellationToken.None)).Should().BeFalse();

        Auth.RefreshTokensUsed.Should().BeEmpty();
    }

    [Fact]
    public async Task RunOnceAsync_WithinADayOfTheLastRefresh_DoesNotCallIntuit()
    {
        await ConnectAsync();
        Time.Advance(TimeSpan.FromHours(23));

        (await Service().RunOnceAsync(CancellationToken.None)).Should().BeFalse();

        Auth.RefreshTokensUsed.Should().BeEmpty();
    }

    [Fact]
    public async Task RunOnceAsync_ADayAfterTheLastRefresh_RefreshesAndLogsItAsAutomatic()
    {
        await ConnectAsync();
        Time.Advance(TimeSpan.FromHours(24));

        (await Service().RunOnceAsync(CancellationToken.None)).Should().BeTrue();

        Auth.RefreshTokensUsed.Should().Equal("refresh-1");
        (await ReadConnectionAsync())!.LastRefreshedAt.Should().Be(Now);
        (await ReadLogAsync()).Should().ContainSingle(l => l.Operation == QuickBooksOperations.TokenRefresh)
            .Which.Should().Satisfy<Domain.Entities.QuickBooksSyncLog>(l =>
            {
                l.Automatic.Should().BeTrue();
                l.Succeeded.Should().BeTrue();
                l.CreatedById.Should().Be(TestUserId);
            });
    }

    [Fact]
    public async Task RunOnceAsync_AfterARefusalAndTheGrace_TriesAgainAndFlagsAReconnectWhenRefusedAgain()
    {
        await ConnectAsync();
        Time.Advance(TimeSpan.FromHours(24));
        Auth.RefreshFailures.Enqueue(InvalidGrant());
        Auth.RefreshFailures.Enqueue(InvalidGrant());

        await Service().Invoking(s => s.RunOnceAsync(CancellationToken.None)).Should().ThrowAsync<QuickBooksAuthException>();
        (await ReadConnectionAsync())!.State.Should().Be(QuickBooksConnectionState.Connected);
        Time.Advance(QuickBooksKeepAliveService.CheckInterval);

        (await Service().RunOnceAsync(CancellationToken.None)).Should().BeTrue();

        (await ReadConnectionAsync())!.State.Should().Be(QuickBooksConnectionState.NeedsReconnect);
        Time.Advance(QuickBooksKeepAliveService.CheckInterval);
        (await Service().RunOnceAsync(CancellationToken.None)).Should().BeFalse("a flagged connection waits for a person");
        Auth.RefreshTokensUsed.Should().HaveCount(2);
    }
}

[Collection(QuickBooksConnectionRowCollection.Name)]
public class QuickBooksSyncLoggerTests : QuickBooksTestHost
{
    [Fact]
    public async Task GetPageAsync_ListsNewestFirstWithWhoDidItAndCanShowProblemsOnly()
    {
        var logger = new QuickBooksSyncLogger(ScopeFactory, Time, NullLogger<QuickBooksSyncLogger>.Instance);
        await logger.WriteAsync(QuickBooksLogEntry.Ok(QuickBooksOperations.Connect, TestUserId));
        Time.Advance(TimeSpan.FromMinutes(1));
        await logger.WriteAsync(QuickBooksLogEntry.Failed(
            QuickBooksOperations.TokenRefresh, TestUserId, InvalidGrant(), automatic: true));

        var everything = await logger.GetPageAsync(1, failuresOnly: false);
        var problems = await logger.GetPageAsync(1, failuresOnly: true);

        everything.Rows.Select(r => r.Operation).Should().Equal(QuickBooksOperations.TokenRefresh, QuickBooksOperations.Connect);
        everything.Rows.Should().OnlyContain(r => r.Who == "test@labels-mis.local");
        everything.Total.Should().Be(2);
        problems.Rows.Should().ContainSingle().Which.Should().Satisfy<QuickBooksLogRow>(r =>
        {
            r.Succeeded.Should().BeFalse();
            r.Automatic.Should().BeTrue();
            r.ErrorCode.Should().Be("invalid_grant");
            r.HttpStatus.Should().Be(400);
        });
    }
}
