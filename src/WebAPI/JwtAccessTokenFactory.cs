using System.IdentityModel.Tokens.Jwt;
using Contracts;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace WebAPI;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "";

    public string Audience { get; set; } = "";

    public string SigningKey { get; set; } = "";
}

/// <summary>Mints the one token shape this system has: HS256 over the shared claim names,
/// 8-hour lifetime, no refresh token (issue #4). Only this app holds the signing key.</summary>
public sealed class JwtAccessTokenFactory(IOptions<JwtOptions> options) : IAccessTokenFactory
{
    private readonly JwtOptions _options = options.Value;

    public AccessToken Create(int userId, string email, string role, string? fullName, string sessionStamp)
    {
        var key = new SymmetricSecurityKey(Convert.FromBase64String(_options.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expires = DateTimeOffset.UtcNow.AddHours(IAccessTokenFactory.LifetimeHours);

        // The session stamp rides only in the JWT; the MVC cookie identity never needs it.
        var claims = AuthClaims.For(userId, email, role, fullName);
        claims.Add(new System.Security.Claims.Claim(IAccessTokenFactory.ClaimSessionStamp, sessionStamp));

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
