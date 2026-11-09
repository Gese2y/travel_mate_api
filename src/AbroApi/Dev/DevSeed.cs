using AbroApi.Data;
using AbroApi.Rides;
using Microsoft.EntityFrameworkCore;

namespace AbroApi.Dev;

/// Development sample data: three drivers and rides on the next Sunday.
public static class DevSeed
{
    public static async Task RunAsync(AbroDbContext db)
    {
        if (await db.Rides.AnyAsync()) return;

        var today = DateTime.UtcNow.Date;
        var sunday = today.AddDays(((int)DayOfWeek.Sunday - (int)today.DayOfWeek + 7) % 7);
        if (sunday <= today) sunday = sunday.AddDays(7);
        // 7:30 Addis = 04:30 UTC
        DateTime At(int h, int m) => sunday.AddHours(h).AddMinutes(m).AddHours(-3);

        User Driver(string phone, string first, string last, double rating) =>
            db.Users.FirstOrDefault(u => u.Phone == phone) ?? db.Users.Add(new User
            {
                Phone = phone, FirstName = first, LastName = last, Rating = rating,
                IdStatus = "approved", Area = "Addis Ababa",
            }).Entity;

        var dawit = Driver("+251911000001", "Dawit", "Bekele", 4.9);
        var kidus = Driver("+251911000002", "Kidus", "Alemu", 4.7);
        var hana = Driver("+251911000003", "Hana", "Tesfaye", 4.8);

        RideStop S(int order, string name, string kind, DateTime t, double lat, double lng) =>
            new() { Order = order, Name = name, Kind = kind, Time = t, Location = RideMapper.Pt(lat, lng) };

        var lake = (lat: 8.7440, lng: 38.9780); // Lake Hora, Bishoftu

        db.Rides.AddRange(
            new Ride
            {
                Driver = dawit, Car = "White Corolla 2018", DepartureAt = At(7, 30), SeatsTotal = 3,
                Stops =
                [
                    S(0, "Edna Mall, Bole", StopKind.Start, At(7, 30), 8.9950, 38.7870),
                    S(1, "Megenagna", StopKind.Pickup, At(7, 45), 9.0060, 38.7930),
                    S(2, "Kality toll gate", StopKind.Pickup, At(8, 5), 8.9000, 38.7800),
                    S(3, "Lake Hora, Bishoftu", StopKind.DropOff, At(8, 35), lake.lat, lake.lng),
                ],
                CostItems = [new() { Label = "Fuel", Amount = 1900 },
                             new() { Label = "Expressway toll", Amount = 300 },
                             new() { Label = "Parking", Amount = 200 }],
            },
            new Ride
            {
                Driver = kidus, Car = "Vitz 2015", DepartureAt = At(8, 0), SeatsTotal = 3,
                Stops =
                [
                    S(0, "Megenagna", StopKind.Start, At(8, 0), 9.0060, 38.7930),
                    S(1, "Lake Hora, Bishoftu", StopKind.DropOff, At(8, 50), lake.lat, lake.lng),
                ],
                CostItems = [new() { Label = "Fuel", Amount = 1700 },
                             new() { Label = "Expressway toll", Amount = 300 }],
            },
            new Ride
            {
                Driver = hana, Car = "Silver Yaris 2019", DepartureAt = At(8, 30), SeatsTotal = 3,
                Stops =
                [
                    S(0, "Bole Medhanialem", StopKind.Start, At(8, 30), 8.9890, 38.7850),
                    S(1, "Lake Hora, Bishoftu", StopKind.DropOff, At(9, 20), lake.lat, lake.lng),
                ],
                CostItems = [new() { Label = "Fuel", Amount = 1300 },
                             new() { Label = "Parking", Amount = 300 }],
            });
        await db.SaveChangesAsync();
    }
}
