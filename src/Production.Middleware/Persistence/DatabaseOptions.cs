namespace Beats.Production.Middleware.Persistence;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public string Provider { get; set; } = "MySql";
    public string? ConnectionString { get; set; }
    public string? ConnectionStringEnvironmentVariable { get; set; }

    public string GetConnectionString()
    {
        if (!string.IsNullOrWhiteSpace(ConnectionStringEnvironmentVariable))
        {
            var value = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable);

            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        if (!string.IsNullOrWhiteSpace(ConnectionString))
        {
            return ConnectionString;
        }

        throw new InvalidOperationException("A database connection string is required.");
    }
}
