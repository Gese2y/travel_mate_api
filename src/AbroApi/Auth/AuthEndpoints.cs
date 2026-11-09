using System.Security.Cryptography;
using System.Text.RegularExpressions;
using AbroApi.Common;
using AbroApi.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AbroApi.Auth;

public record SendOtpRequest(string Phone, string? Channel);
public record VerifyOtpRequest(string Phone, string Code);
public record RefreshRequest(string RefreshToken);

public static partial class AuthEndpoints
{
    // Ethiopian mobile numbers: +251 then 9 or 7, then 8 digits.
    [GeneratedRegex(@"^\+251[79]\d{8}$")]
    private static partial Regex PhoneRegex();

    public static void MapAuth(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/auth").WithTags("Auth");

        g.MapPost("/otp/send", async (
            SendOtpRequest req, AbroDbContext db, ISmsSender sms,
            IOptions<OtpOptions> opts, CancellationToken ct) =>
        {
            var o = opts.Value;
            if (!PhoneRegex().IsMatch(req.Phone))
                return ApiError.Validation("phone", "Enter a valid Ethiopian mobile number.");

            var recent = await db.OtpCodes
                .Where(c => c.Phone == req.Phone)
                .OrderByDescending(c => c.CreatedAt)
                .FirstOrDefaultAsync(ct);
            if (recent is not null &&
                recent.CreatedAt > DateTime.UtcNow.AddSeconds(-o.ResendSeconds))
                return ApiError.Result(429, "rate_limited", "Please wait before requesting another code.");

            var code = o.FixedCode ?? RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
            db.OtpCodes.Add(new OtpCode
            {
                Phone = req.Phone,
                CodeHash = TokenService.Hash(req.Phone + code),
                ExpiresAt = DateTime.UtcNow.AddMinutes(o.ExpiryMinutes),
            });
            await db.SaveChangesAsync(ct);

            if (o.FixedCode is null)
                await sms.SendAsync(req.Phone, $"Your Abro code is {code}", ct);
            return Results.NoContent();
        });

        g.MapPost("/otp/verify", async (
            VerifyOtpRequest req, AbroDbContext db, TokenService tokens,
            IOptions<OtpOptions> opts, CancellationToken ct) =>
        {
            var o = opts.Value;
            var otp = await db.OtpCodes
                .Where(c => c.Phone == req.Phone && c.ConsumedAt == null)
                .OrderByDescending(c => c.CreatedAt)
                .FirstOrDefaultAsync(ct);

            if (otp is null || otp.ExpiresAt < DateTime.UtcNow || otp.Attempts >= o.MaxAttempts)
                return ApiError.Result(400, "invalid_code", "That code is wrong or has expired.");

            otp.Attempts++;
            if (otp.CodeHash != TokenService.Hash(req.Phone + req.Code))
            {
                await db.SaveChangesAsync(ct);
                return ApiError.Result(400, "invalid_code", "That code is wrong or has expired.");
            }
            otp.ConsumedAt = DateTime.UtcNow;

            var user = await db.Users.FirstOrDefaultAsync(u => u.Phone == req.Phone, ct);
            var isNew = user is null;
            if (user is null)
            {
                user = new User { Phone = req.Phone };
                db.Users.Add(user);
            }
            await db.SaveChangesAsync(ct);

            var pair = await tokens.IssueAsync(user, ct);
            return Results.Ok(new
            {
                pair.AccessToken, pair.RefreshToken, pair.ExpiresIn, IsNewUser = isNew,
            });
        });

        g.MapPost("/refresh", async (
            RefreshRequest req, AbroDbContext db, TokenService tokens, CancellationToken ct) =>
        {
            var hash = TokenService.Hash(req.RefreshToken);
            var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
            if (stored is null || stored.RevokedAt != null || stored.ExpiresAt < DateTime.UtcNow)
                return ApiError.Result(401, "unauthorized", "Please sign in again.");

            stored.RevokedAt = DateTime.UtcNow; // rotate
            var user = await db.Users.FindAsync([stored.UserId], ct);
            if (user is null)
                return ApiError.Result(401, "unauthorized", "Please sign in again.");

            var pair = await tokens.IssueAsync(user, ct);
            return Results.Ok(new
            {
                pair.AccessToken, pair.RefreshToken, pair.ExpiresIn, IsNewUser = false,
            });
        });

        g.MapPost("/logout", async (RefreshRequest req, AbroDbContext db, CancellationToken ct) =>
        {
            var hash = TokenService.Hash(req.RefreshToken);
            var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
            if (stored is not null && stored.RevokedAt == null)
            {
                stored.RevokedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
            }
            return Results.NoContent();
        });
    }
}
