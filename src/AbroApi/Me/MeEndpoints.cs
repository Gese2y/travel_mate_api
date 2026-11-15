using System.Security.Claims;
using AbroApi.Common;
using AbroApi.Data;

namespace AbroApi.Me;

public record LocationRequest(double Lat, double Lng);

public record UpdateProfileRequest(
    string FirstName, string LastName, string? Area, List<string>? Interests);

public static class MeEndpoints
{
    public static void MapMe(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/me").WithTags("Me").RequireAuthorization();

        g.MapGet("/", async (ClaimsPrincipal p, AbroDbContext db, CancellationToken ct) =>
        {
            var user = await db.Users.FindAsync([UserId(p)], ct);
            return user is null ? ApiError.Result(404, "not_found", "User not found.") : Results.Ok(Dto(user));
        });

        // The app shares where the user is so we can tell them about new rides
        // nearby. Rounded to 3 decimals (~100 m); only the latest is kept.
        g.MapPut("/location", async (
            LocationRequest req, ClaimsPrincipal p, AbroDbContext db, CancellationToken ct) =>
        {
            if (req.Lat is < -90 or > 90 || req.Lng is < -180 or > 180)
                return ApiError.Validation("lat", "Invalid coordinates.");
            var user = await db.Users.FindAsync([UserId(p)], ct);
            if (user is null) return ApiError.Result(404, "not_found", "User not found.");
            user.LastLocation = AbroApi.Rides.RideMapper.Pt(
                Math.Round(req.Lat, 3), Math.Round(req.Lng, 3));
            user.LocationUpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        g.MapPut("/", async (
            UpdateProfileRequest req, ClaimsPrincipal p, AbroDbContext db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.FirstName))
                return ApiError.Validation("firstName", "First name is required.");
            if (string.IsNullOrWhiteSpace(req.LastName))
                return ApiError.Validation("lastName", "Last name is required.");

            var user = await db.Users.FindAsync([UserId(p)], ct);
            if (user is null) return ApiError.Result(404, "not_found", "User not found.");

            user.FirstName = req.FirstName.Trim();
            user.LastName = req.LastName.Trim();
            user.Area = req.Area?.Trim() ?? "";
            user.Interests = req.Interests ?? [];
            await db.SaveChangesAsync(ct);
            return Results.Ok(Dto(user));
        });
    }

    private static Guid UserId(ClaimsPrincipal p) =>
        Guid.Parse(p.FindFirstValue(ClaimTypes.NameIdentifier) ?? p.FindFirstValue("sub")!);

    private static object Dto(User u) => new
    {
        u.Id, u.FirstName, u.LastName, u.PhotoUrl, u.Area, u.Interests,
        PhoneVerified = true,
        u.IdStatus,
        Stats = new { Trips = 0, Rating = 0.0, Reviews = 0 }, // TODO: real stats
        JoinedAt = u.CreatedAt,
    };
}
