using Services;

namespace Services.Tests;

/// <summary>
/// ADR-0018: the guards that stop a misconfigured relay from being discovered one failed
/// registration at a time. These are pure option checks — no socket is opened — so they run without
/// a database and without touching the network.
/// </summary>
public sealed class SmtpEmailOptionsTests
{
    private static SmtpEmailOptions Complete() => new()
    {
        Host = "smtp-relay.example.com",
        Port = 587,
        UserName = "login",
        Password = "key",
    };

    [Fact]
    public void A_fully_configured_relay_is_accepted()
    {
        var options = Complete();

        Assert.True(options.IsConfigured);
        Assert.False(options.UsesUnsupportedPort);
    }

    [Theory]
    [InlineData("", "login", "key")]
    [InlineData("host", "", "key")]
    [InlineData("host", "login", "")]
    [InlineData("host", "login", "   ")]
    public void A_missing_value_is_not_configured(string host, string user, string password)
    {
        var options = new SmtpEmailOptions { Host = host, UserName = user, Password = password };

        Assert.False(options.IsConfigured);
    }

    [Fact]
    public void Port_465_is_reported_unsupported_because_SmtpClient_cannot_do_implicit_tls()
    {
        // Microsoft documents SmtpClient.EnableSsl as RFC 3207 STARTTLS only, and says the SMTPS
        // (implicit TLS, port 465) connection method is not supported. Left to run, every send would
        // hang and time out, so the boot refuses it instead.
        var options = Complete();
        options.Port = 465;

        Assert.True(options.UsesUnsupportedPort);
    }

    [Theory]
    [InlineData(25)]
    [InlineData(587)]
    [InlineData(2525)]
    public void Other_ports_are_not_flagged(int port)
    {
        var options = Complete();
        options.Port = port;

        Assert.False(options.UsesUnsupportedPort);
    }
}
