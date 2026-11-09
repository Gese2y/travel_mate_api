using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using AbroApi.Data;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace AbroApi.Auth;

public record TokenPair(string AccessToken, string RefreshToken, int ExpiresIn);

public class TokenService(IOptions<JwtOptions> options, AbroDbContext db)
{
    private readonly JwtOptions _o = options.Value;

    public static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public async Task<TokenPair> IssueAsync(User user, CancellationToken ct)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_o.Key));
        var jwt = new JwtSecurityToken(
            _o.Issuer,
            _o.Audience,
            [new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString())],
            expires: DateTime.UtcNow.AddMinutes(_o.AccessMinutes),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        var refresh = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = Hash(refresh),
            ExpiresAt = DateTime.UtcNow.AddDays(_o.RefreshDays),
        });
        await db.SaveChangesAsync(ct);

        return new TokenPair(
            new JwtSecurityTokenHandler().WriteToken(jwt), refresh, _o.AccessMinutes * 60);
    }
}
