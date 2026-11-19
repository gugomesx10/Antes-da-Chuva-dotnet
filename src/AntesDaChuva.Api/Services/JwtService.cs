using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AntesDaChuva.Api.Contracts;
using AntesDaChuva.Api.Domain;
using Microsoft.IdentityModel.Tokens;

namespace AntesDaChuva.Api.Services;

public sealed record JwtSettings(string Key, string Issuer, string Audience, int ExpirationMinutes);

public sealed class JwtService(JwtSettings settings)
{
    public AuthResponse Generate(AppUser user)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Email),
            new Claim("uid", user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role.ToString())
        };
        var token = new JwtSecurityToken(
            issuer: settings.Issuer,
            audience: settings.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(settings.ExpirationMinutes),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Convert.FromBase64String(settings.Key)),
                SecurityAlgorithms.HmacSha256));
        return new AuthResponse(new JwtSecurityTokenHandler().WriteToken(token), "Bearer",
            settings.ExpirationMinutes * 60, UserResponse.From(user));
    }

    public static int CurrentUserId(ClaimsPrincipal user)
    {
        return int.TryParse(user.FindFirst("uid")?.Value, out int id) ? id : 0;
    }
}
