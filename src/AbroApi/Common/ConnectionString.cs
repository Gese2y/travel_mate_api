namespace AbroApi.Common;

public static class ConnectionString
{
    /// Accepts either an Npgsql string or a Render-style
    /// postgres://user:pass@host:port/db URL and returns an Npgsql string.
    public static string Resolve(IConfiguration config)
    {
        var raw = config["DATABASE_URL"] ?? config.GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "Set DATABASE_URL or ConnectionStrings:Default.");

        if (!raw.StartsWith("postgres://") && !raw.StartsWith("postgresql://"))
            return raw;

        var uri = new Uri(raw);
        var parts = uri.UserInfo.Split(':', 2);
        var port = uri.Port > 0 ? uri.Port : 5432;
        return $"Host={uri.Host};Port={port};Database={uri.AbsolutePath.TrimStart('/')};" +
               $"Username={Uri.UnescapeDataString(parts[0])};" +
               $"Password={Uri.UnescapeDataString(parts.Length > 1 ? parts[1] : "")};" +
               "SSL Mode=Prefer;Trust Server Certificate=true";
    }
}
