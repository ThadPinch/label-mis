using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;

namespace LabelsMis.Domain.Tests.Entities;

public class QuickBooksConnectionTests
{
    private static readonly Guid User = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTime Now = new(2026, 11, 10, 15, 0, 0, DateTimeKind.Utc);

    private static QuickBooksConnection WithCredentials(
        QuickBooksEnvironment environment = QuickBooksEnvironment.Sandbox, string clientId = "client-1")
    {
        var connection = QuickBooksConnection.Create(User, Now);
        connection.SetCredentials(environment, clientId, "enc-secret", "cret", User, Now);
        return connection;
    }

    private static QuickBooksConnection Connected(DateTime? at = null)
    {
        var when = at ?? Now;
        var connection = WithCredentials();
        connection.Connect("9341", "enc-access", when.AddHours(1), "enc-refresh", when.AddDays(100), when.AddYears(5), User, when);
        return connection;
    }

    [Fact]
    public void Create_StartsWithNoCredentialsInTheSandbox()
    {
        var connection = QuickBooksConnection.Create(User, Now);

        connection.Should().Satisfy<QuickBooksConnection>(c =>
        {
            c.Id.Should().Be(QuickBooksConnection.SingletonId);
            c.State.Should().Be(QuickBooksConnectionState.NoCredentials);
            c.Environment.Should().Be(QuickBooksEnvironment.Sandbox);
            c.IsConnected.Should().BeFalse();
        });
    }

    [Fact]
    public void SetCredentials_SavesKeysAndMovesToNotConnected()
    {
        var connection = QuickBooksConnection.Create(User, Now);

        var dropped = connection.SetCredentials(QuickBooksEnvironment.Production, "  client-1  ", "enc-secret", "cret", User, Now);

        dropped.Should().BeFalse();
        connection.Should().Satisfy<QuickBooksConnection>(c =>
        {
            c.ClientId.Should().Be("client-1");
            c.ClientSecretProtected.Should().Be("enc-secret");
            c.ClientSecretTail.Should().Be("cret");
            c.Environment.Should().Be(QuickBooksEnvironment.Production);
            c.State.Should().Be(QuickBooksConnectionState.NotConnected);
        });
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void SetCredentials_WithoutAClientId_Throws(string clientId)
    {
        var connection = QuickBooksConnection.Create(User, Now);

        var act = () => connection.SetCredentials(QuickBooksEnvironment.Sandbox, clientId, "enc-secret", "cret", User, Now);

        act.Should().Throw<ArgumentException>().WithMessage("Client ID is required.");
    }

    [Fact]
    public void SetCredentials_FirstTimeWithoutASecret_Throws()
    {
        var connection = QuickBooksConnection.Create(User, Now);

        var act = () => connection.SetCredentials(QuickBooksEnvironment.Sandbox, "client-1", null, null, User, Now);

        act.Should().Throw<ArgumentException>().WithMessage("Client secret is required.");
    }

    [Fact]
    public void SetCredentials_BlankSecret_KeepsTheOneOnFile()
    {
        var connection = WithCredentials();

        connection.SetCredentials(QuickBooksEnvironment.Sandbox, "client-1", "  ", null, User, Now);

        connection.ClientSecretProtected.Should().Be("enc-secret");
        connection.ClientSecretTail.Should().Be("cret");
    }

    [Fact]
    public void SetCredentials_NewSecretForTheSameApp_KeepsTheCompanyLink()
    {
        var connection = Connected();

        var dropped = connection.SetCredentials(QuickBooksEnvironment.Sandbox, "client-1", "enc-secret-2", "ret2", User, Now);

        dropped.Should().BeFalse();
        connection.IsConnected.Should().BeTrue();
        connection.ClientSecretProtected.Should().Be("enc-secret-2");
    }

    [Theory]
    [InlineData(QuickBooksEnvironment.Production, "client-1")]
    [InlineData(QuickBooksEnvironment.Sandbox, "client-2")]
    public void SetCredentials_ChangingAppOrEnvironment_DropsTheCompanyLink(QuickBooksEnvironment environment, string clientId)
    {
        var connection = Connected();

        var dropped = connection.SetCredentials(environment, clientId, null, null, User, Now);

        dropped.Should().BeTrue();
        connection.Should().Satisfy<QuickBooksConnection>(c =>
        {
            c.State.Should().Be(QuickBooksConnectionState.NotConnected);
            c.RealmId.Should().BeNull();
            c.RefreshTokenProtected.Should().BeNull();
            c.AccessTokenProtected.Should().BeNull();
        });
    }

    [Fact]
    public void Connect_WithoutCredentials_Throws()
    {
        var connection = QuickBooksConnection.Create(User, Now);

        var act = () => connection.Connect("9341", "a", Now.AddHours(1), "r", Now.AddDays(100), null, User, Now);

        act.Should().Throw<InvalidOperationException>().WithMessage("*client ID and secret*");
    }

    [Theory]
    [InlineData("", "a", "r", "*which company*")]
    [InlineData("9341", "", "r", "*access token*")]
    [InlineData("9341", "a", " ", "*refresh token*")]
    public void Connect_WithAnIncompleteAnswerFromQuickBooks_Throws(string realmId, string access, string refresh, string message)
    {
        var connection = WithCredentials();

        var act = () => connection.Connect(realmId, access, Now.AddHours(1), refresh, Now.AddDays(100), null, User, Now);

        act.Should().Throw<ArgumentException>().WithMessage(message);
    }

    [Fact]
    public void Connect_RecordsTheCompanyTokensAndWhoConnected()
    {
        var connection = Connected();

        connection.Should().Satisfy<QuickBooksConnection>(c =>
        {
            c.State.Should().Be(QuickBooksConnectionState.Connected);
            c.RealmId.Should().Be("9341");
            c.ConnectedAt.Should().Be(Now);
            c.ConnectedById.Should().Be(User);
            c.LastRefreshedAt.Should().Be(Now);
            c.AccessTokenExpiresAt.Should().Be(Now.AddHours(1));
            c.RefreshTokenExpiresAt.Should().Be(Now.AddDays(100));
            c.RefreshTokenHardExpiresAt.Should().Be(Now.AddYears(5));
        });
    }

    [Fact]
    public void Connect_AgainAfterNeedsReconnect_ClearsTheFlagAndTheError()
    {
        var connection = Connected();
        connection.MarkNeedsReconnect("QuickBooks refused the saved sign-in.", Now.AddDays(1));

        connection.Connect("9341", "enc-access-2", Now.AddDays(1).AddHours(1), "enc-refresh-2", Now.AddDays(101), null, User, Now.AddDays(1));

        connection.Should().Satisfy<QuickBooksConnection>(c =>
        {
            c.State.Should().Be(QuickBooksConnectionState.Connected);
            c.LastError.Should().BeNull();
            c.LastErrorAt.Should().BeNull();
        });
    }

    [Fact]
    public void Connect_ToADifferentCompany_ForgetsTheOldCompanyName()
    {
        var connection = Connected();
        connection.SetCompanyName("Sandbox Company US ef89");

        connection.Connect("7777", "a", Now.AddHours(1), "r", Now.AddDays(100), null, User, Now);

        connection.CompanyName.Should().BeNull();
    }

    [Fact]
    public void Connect_ToTheSameCompany_KeepsItsName()
    {
        var connection = Connected();
        connection.SetCompanyName("Sandbox Company US ef89");

        connection.Connect("9341", "a", Now.AddHours(1), "r", Now.AddDays(100), null, User, Now);

        connection.CompanyName.Should().Be("Sandbox Company US ef89");
    }

    [Fact]
    public void StoreRefreshedTokens_ReplacesBothTokensAndRollsTheDates()
    {
        var connection = Connected();
        var later = Now.AddHours(30);

        connection.StoreRefreshedTokens("enc-access-2", later.AddHours(1), "enc-refresh-2", later.AddDays(100), null, later);

        connection.Should().Satisfy<QuickBooksConnection>(c =>
        {
            c.AccessTokenProtected.Should().Be("enc-access-2");
            c.RefreshTokenProtected.Should().Be("enc-refresh-2");
            c.AccessTokenExpiresAt.Should().Be(later.AddHours(1));
            c.RefreshTokenExpiresAt.Should().Be(later.AddDays(100));
            c.LastRefreshedAt.Should().Be(later);
        });
    }

    [Fact]
    public void StoreRefreshedTokens_WithoutAHardExpiry_KeepsTheOneFromConnect()
    {
        var connection = Connected();

        connection.StoreRefreshedTokens("a2", Now.AddHours(2), "r2", Now.AddDays(101), null, Now.AddHours(1));

        connection.RefreshTokenHardExpiresAt.Should().Be(Now.AddYears(5));
    }

    [Fact]
    public void StoreRefreshedTokens_WhenNotLinked_Throws()
    {
        var connection = WithCredentials();

        var act = () => connection.StoreRefreshedTokens("a", Now.AddHours(1), "r", Now.AddDays(100), null, Now);

        act.Should().Throw<InvalidOperationException>().WithMessage("QuickBooks is not connected.");
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(57, true)]
    [InlineData(58, false)] // inside the two-minute skew
    [InlineData(61, false)]
    public void AccessTokenUsable_StopsTwoMinutesBeforeExpiry(int minutesAfterConnect, bool expected)
    {
        var connection = Connected();

        connection.AccessTokenUsable(Now.AddMinutes(minutesAfterConnect)).Should().Be(expected);
    }

    [Fact]
    public void AccessTokenUsable_WhenReconnectIsNeeded_IsFalse()
    {
        var connection = Connected();
        connection.MarkNeedsReconnect("refused", Now);

        connection.AccessTokenUsable(Now).Should().BeFalse();
    }

    [Theory]
    [InlineData(23, false)]
    [InlineData(24, true)]
    [InlineData(200, true)]
    public void KeepAliveDue_OnceADayHasPassedSinceTheLastRefresh(int hoursSince, bool expected)
    {
        var connection = Connected();

        connection.KeepAliveDue(Now.AddHours(hoursSince)).Should().Be(expected);
    }

    [Fact]
    public void KeepAliveDue_WhenNotConnected_IsFalse()
    {
        WithCredentials().KeepAliveDue(Now.AddDays(30)).Should().BeFalse();
    }

    [Fact]
    public void KeepAliveDue_WhenReconnectIsNeeded_IsFalse()
    {
        var connection = Connected();
        connection.MarkNeedsReconnect("refused", Now);

        connection.KeepAliveDue(Now.AddDays(30)).Should().BeFalse();
    }

    [Fact]
    public void MarkNeedsReconnect_KeepsTheCompanyButDropsTheAccessToken()
    {
        var connection = Connected();
        connection.SetCompanyName("Sandbox Company US ef89");

        connection.MarkNeedsReconnect("QuickBooks refused the saved sign-in.", Now.AddDays(3));

        connection.Should().Satisfy<QuickBooksConnection>(c =>
        {
            c.State.Should().Be(QuickBooksConnectionState.NeedsReconnect);
            c.IsConnected.Should().BeFalse();
            c.CompanyName.Should().Be("Sandbox Company US ef89");
            c.RealmId.Should().Be("9341");
            c.AccessTokenProtected.Should().BeNull();
            c.LastError.Should().Be("QuickBooks refused the saved sign-in.");
            c.LastErrorAt.Should().Be(Now.AddDays(3));
        });
    }

    [Fact]
    public void MarkNeedsReconnect_WhenNeverLinked_DoesNothing()
    {
        var connection = WithCredentials();

        connection.MarkNeedsReconnect("refused", Now);

        connection.State.Should().Be(QuickBooksConnectionState.NotConnected);
        connection.LastError.Should().BeNull();
    }

    [Fact]
    public void Disconnect_ForgetsTheCompanyAndKeepsTheKeys()
    {
        var connection = Connected();
        connection.SetCompanyName("Sandbox Company US ef89");

        connection.Disconnect(User, Now.AddDays(1));

        connection.Should().Satisfy<QuickBooksConnection>(c =>
        {
            c.State.Should().Be(QuickBooksConnectionState.NotConnected);
            c.RealmId.Should().BeNull();
            c.CompanyName.Should().BeNull();
            c.RefreshTokenProtected.Should().BeNull();
            c.ConnectedAt.Should().BeNull();
            c.ClientId.Should().Be("client-1");
            c.ClientSecretProtected.Should().Be("enc-secret");
        });
    }

    [Fact]
    public void RecordError_TruncatesAVeryLongMessage()
    {
        var connection = Connected();

        connection.RecordError(new string('x', 5000), Now);

        connection.LastError.Should().HaveLength(QuickBooksConnection.MaxErrorLength);
    }

    [Fact]
    public void ReconnectBy_IsWhicheverLimitComesFirst()
    {
        var connection = WithCredentials();
        connection.Connect("9341", "a", Now.AddHours(1), "r", Now.AddDays(100), Now.AddDays(40), User, Now);

        connection.ReconnectBy.Should().Be(Now.AddDays(40));
    }

    [Fact]
    public void ReconnectBy_WithoutAHardLimit_IsTheRollingExpiry()
    {
        var connection = WithCredentials();
        connection.Connect("9341", "a", Now.AddHours(1), "r", Now.AddDays(100), null, User, Now);

        connection.ReconnectBy.Should().Be(Now.AddDays(100));
    }

    [Fact]
    public void ReconnectBy_WhenNotConnected_IsNull()
    {
        WithCredentials().ReconnectBy.Should().BeNull();
    }
}
