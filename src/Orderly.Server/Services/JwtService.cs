using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Orderly.Server.Models;

namespace Orderly.Server.Services;

public sealed class JwtService : IJwtService
{
    private readonly ServerOptions _options;
    private readonly SymmetricSecurityKey _signingKey;

    public JwtService(ServerOptions options)
    {
        _options = options;
        var keyBytes = Encoding.UTF8.GetBytes(options.JwtSigningKey);
        if (keyBytes.Length < 32)
            throw new InvalidOperationException("JWT signing key must be at least 32 bytes.");
        _signingKey = new SymmetricSecurityKey(keyBytes);
    }

    public string GenerateAccessToken(Guid userId, string username, string displayName, int tokenVersion, string deviceId)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(JwtRegisteredClaimNames.UniqueName, username),
            new Claim(JwtRegisteredClaimNames.Name, displayName),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new Claim("token_version", tokenVersion.ToString()),
            new Claim("device_id", deviceId),
        };

        var creds = new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _options.JwtIssuer,
            audience: _options.JwtAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_options.AccessTokenLifetimeMinutes),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public ClaimsPrincipal? ValidateAccessToken(string token)
    {
        var handler = new JwtSecurityTokenHandler();
        try
        {
            var principal = handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = _options.JwtIssuer,
                ValidAudience = _options.JwtAudience,
                IssuerSigningKey = _signingKey,
                ClockSkew = TimeSpan.FromMinutes(1)
            }, out _);
            return principal;
        }
        catch
        {
            return null;
        }
    }
}
