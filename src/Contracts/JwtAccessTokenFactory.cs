using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Contracts;

public sealed record AccessToken(string Token, DateTimeOffset ExpiresAt);

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "";

    public string Audience { get; set; } = "";

    public string SigningKey { get; set; } = "";
}

/// <summary>Mints the one token shape this system has. Lives in Contracts so both apps share the
/// claim names; only the API holds the signing key. The lifetime is fixed at 8 hours — there is
/// no refresh token by design (issue #4).</summary>
public interface IAccessTokenFactory
{
    public const int LifetimeHours = 8;

    public const string ClaimUserId = "uid";
    public const string ClaimEmail = "email";
    public const string ClaimRole = "role";
    public const string ClaimName = "name";

    AccessToken Create(int userId, string email, string role, string? fullName);
}

public sealed class JwtAccessTokenFactory(IOptions<JwtOptions> options) : IAccessTokenFactory
{
    private readonly JwtOptions _options = options.Value;

    public AccessToken Create(int userId, string email, string role, string? fullName)
    {
        var key = new SymmetricSecurityKey(Convert.FromBase64String(_options.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expires = DateTimeOffset.UtcNow.AddHours(IAccessTokenFactory.LifetimeHours);

        var claims = new List<Claim>
        {
            new(IAccessTokenFactory.ClaimUserId, userId.ToString()),
            new(IAccessTokenFactory.ClaimEmail, email),
            new(IAccessTokenFactory.ClaimRole, role),
        };
        if (!string.IsNullOrEmpty(fullName))
        {
            claims.Add(new Claim(IAccessTokenFactory.ClaimName, fullName));
        }

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expires.UtcDateTime,
            signingCredentials: credentials);

        return new AccessToken(new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
