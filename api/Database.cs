using Npgsql;

public static class Database
{
    public static string ConnectionString(IConfiguration configuration) =>
        new NpgsqlConnectionStringBuilder
        {
            Host = configuration["POSTGRES_HOST"] ?? throw new InvalidOperationException("POSTGRES_HOST is required."),
            Port = int.Parse(configuration["POSTGRES_PORT"] ?? throw new InvalidOperationException("POSTGRES_PORT is required.")),
            Database = configuration["POSTGRES_DB"] ?? throw new InvalidOperationException("POSTGRES_DB is required."),
            Username = configuration["POSTGRES_USER"] ?? throw new InvalidOperationException("POSTGRES_USER is required."),
            Password = configuration["POSTGRES_PASSWORD"] ?? throw new InvalidOperationException("POSTGRES_PASSWORD is required.")
        }.ConnectionString;
}
