using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace DocuMind.Api.Auth;

public record JwtSettings(string Issuer, string Audience, string Key, int ExpiresMinutes = 30)
{
    public SymmetricSecurityKey SigningKey => new(Encoding.UTF8.GetBytes(Key));
}

public record DemoUser(string Username, string Password, string Role);

public sealed class TokenService(JwtSettings settings)
{
    public (string Token, DateTime ExpiresAt) Create(DemoUser user)
    {
        var expiresAt = DateTime.UtcNow.AddMinutes(settings.ExpiresMinutes);
        var token = new JwtSecurityToken(
            issuer: settings.Issuer,
            audience: settings.Audience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Username),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Role, user.Role),
            ],
            expires: expiresAt,
            signingCredentials: new SigningCredentials(settings.SigningKey, SecurityAlgorithms.HmacSha256));

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
