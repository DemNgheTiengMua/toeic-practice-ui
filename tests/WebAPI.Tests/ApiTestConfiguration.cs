using Microsoft.Extensions.Configuration;

namespace WebAPI.Tests;

/// <summary>JWT configuration that satisfies AddApiAuthentication's startup guards. Nothing here
/// signs or verifies a token; the throwaway 32-byte key only gets the service registered.</summary>
internal static class ApiTestConfiguration
{
    public static IConfiguration Build() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:SigningKey"] = Convert.ToBase64String(new byte[32]),
                ["Jwt:Issuer"] = "webapi-tests",
                ["Jwt:Audience"] = "webapi-tests",
            })
            .Build();
}
