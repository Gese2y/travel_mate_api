using AbroApi.Data;

namespace AbroApi.Trips;

public record TripCostInput(string Label, int Amount);
public record CreateTripRequest(
    string Title, string Kind, string Destination, string MeetingPoint,
    DateTime LeavesAt, DateTime BackAt, int PricePerPerson, int Capacity,
    string? Description, string? JoinPolicy, List<TripCostInput>? Included);
public record TripJoinInput(string? Note);
public record TripDecision(string Status);

public static class TripMapper
{
    public static readonly string[] Kinds = ["Day trip", "Weekend"];

    /// Shapes a trip like docs/api-contract.md. Phone numbers are never included.
    public static object ToDto(Trip t, Guid me, bool detail = false)
    {
        var mine = t.Requests
            .Where(r => r.UserId == me && r.Status is RequestStatus.Pending or RequestStatus.Accepted)
            .OrderByDescending(r => r.CreatedAt).FirstOrDefault();
        return new
        {
            t.Id,
            t.Title,
            t.Kind,
            t.LeavesAt,
            t.BackAt,
            t.Destination,
            t.MeetingPoint,
            Organiser = new
            {
                t.Organiser.Id,
                Name = $"{t.Organiser.FirstName} {t.Organiser.LastName}".Trim(),
                t.Organiser.Rating,
                TripsCount = 0, // TODO: completed trips once trips can finish
            },
            t.PricePerPerson,
            t.Capacity,
            GoingCount = t.GoingCount,
            Included = t.CostItems.Select(c => new { c.Label, c.Amount }),
            t.Description,
            t.JoinPolicy,
            CoverUrl = t.CoverUpdatedAt is null
                ? null
                : $"/api/v1/trips/{t.Id}/cover?v={t.CoverUpdatedAt.Value.Ticks}",
            MyRequest = mine is null ? null : new { mine.Id, mine.Status },
            Going = detail
                ? t.Requests.Where(r => r.Status == RequestStatus.Accepted).Take(5)
                    .Select(r => new { r.User.Id, Name = $"{r.User.FirstName} {r.User.LastName}".Trim() })
                    .ToList()
                : null,
        };
    }

    public static object ToDto(TripRequest q) => new
    {
        q.Id, q.TripId,
        User = new
        {
            q.User.Id,
            Name = $"{q.User.FirstName} {q.User.LastName}".Trim(),
            q.User.Rating,
            IdVerified = q.User.IdStatus == "approved",
        },
        q.Note, q.Status, q.CreatedAt,
    };
}
