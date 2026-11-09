using Microsoft.EntityFrameworkCore;

namespace AbroApi.Data;

public class AbroDbContext(DbContextOptions<AbroDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<OtpCode> OtpCodes => Set<OtpCode>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Ride> Rides => Set<Ride>();
    public DbSet<RideStop> RideStops => Set<RideStop>();
    public DbSet<RideCostItem> RideCostItems => Set<RideCostItem>();
    public DbSet<SeatRequest> SeatRequests => Set<SeatRequest>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasPostgresExtension("postgis");

        b.Entity<User>(e =>
        {
            e.HasIndex(u => u.Phone).IsUnique();
            e.Property(u => u.Phone).HasMaxLength(20);
        });
        b.Entity<OtpCode>(e => e.HasIndex(o => new { o.Phone, o.CreatedAt }));
        b.Entity<RefreshToken>(e => e.HasIndex(t => t.TokenHash).IsUnique());

        b.Entity<Ride>(e =>
        {
            e.HasIndex(r => r.DepartureAt);
            e.HasOne(r => r.Driver).WithMany().HasForeignKey(r => r.DriverId);
            e.HasMany(r => r.Stops).WithOne().HasForeignKey(s => s.RideId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(r => r.CostItems).WithOne().HasForeignKey(c => c.RideId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(r => r.Requests).WithOne().HasForeignKey(q => q.RideId).OnDelete(DeleteBehavior.Cascade);
            e.Ignore(r => r.TotalCost).Ignore(r => r.PricePerSeat)
             .Ignore(r => r.SeatsTaken).Ignore(r => r.SeatsFree);
        });
        b.Entity<RideStop>(e =>
        {
            e.Property(s => s.Location).HasColumnType("geography (point)");
            e.HasIndex(s => s.Location).HasMethod("gist");
        });
        b.Entity<SeatRequest>(e => e.HasIndex(q => new { q.RideId, q.UserId }));
    }
}
