using System;
using Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using WebAPI.Extensions;
using Xunit;

namespace WebAPI.Tests;

/// <summary>
/// The signing key that shipped in 7aad51e is public: it is in this repository's history and in
/// every clone anyone took before the rotation. Removing it from tracked files is not enough on its
/// own — an instance that still has it configured in user-secrets or an environment variable would
/// keep trusting forged tokens. The boot guard must refuse it.
/// </summary>
public sealed class RetiredSigningKeyTests
{
    private const string RetiredKey = "DuFiytO9xnewMYC1REpIt5xQIfqc7k2Ybv9JxKcUDTk=";

    [Fact]
    public void A_re_wrapped_copy_of_the_retired_key_is_also_refused()
    {
        // Base64 tolerates whitespace. A copy of the leaked key pasted with a line break decodes to
        // the same 32 bytes and signs the same tokens, so the guard must hash the bytes, not the text.
        const string wrapped = "DuFiytO9xnewMYC1RE\npIt5xQIfqc7k2Ybv9JxKcUDTk=";

        Assert.Throws<InvalidOperationException>(() => BuildServices(wrapped));
    }

    [Fact]
    public void Booting_with_the_retired_key_is_refused_with_a_message_that_names_the_fix()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => BuildServices(RetiredKey));

        Assert.Contains("leaked", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("user-secrets", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Booting_with_a_current_key_is_allowed()
    {
        // 32 random bytes, base64 — the shape the setup script and CI produce.
        var fresh = Convert.ToBase64String(RandomNumberGenerator32());

        var services = BuildServices(fresh);

        Assert.Contains(services, d => d.ServiceType == typeof(IAccessTokenFactory));
    }

    [Fact]
    public void Booting_with_no_key_is_still_refused_by_the_existing_guard()
    {
        Assert.Throws<InvalidOperationException>(() => BuildServices(string.Empty));
    }

    private static IServiceCollection BuildServices(string signingKey)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:SigningKey"] = signingKey,
                ["Jwt:Issuer"] = "extra-efficient",
                ["Jwt:Audience"] = "extra-efficient",
            })
            .Build();

        return new ServiceCollection().AddApiAuthentication(configuration);
    }

    private static byte[] RandomNumberGenerator32()
    {
        var bytes = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return bytes;
    }
}
