using Npgsql;

namespace Restaurant.Web.Persistence;

public static class Database
{
    public static string ConnectionString(string value)
    {
        if (!value.StartsWith("postgres://") && !value.StartsWith("postgresql://")) return value;
        var uri = new Uri(value);
        var credentials = uri.UserInfo.Split(':', 2);
        return new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host, Port = uri.IsDefaultPort ? 5432 : uri.Port,
            Username = Uri.UnescapeDataString(credentials[0]),
            Password = credentials.Length > 1 ? Uri.UnescapeDataString(credentials[1]) : "",
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
            SslMode = SslMode.VerifyFull, MaxPoolSize = 10, Timeout = 30,
            GssEncryptionMode = GssEncryptionMode.Disable,
            ApplicationName = "green-tasty-marathon"
        }.ConnectionString;
    }

    public static async Task Migrate(NpgsqlDataSource source)
    {
        await using var connection=await source.OpenConnectionAsync();
        await using var transaction=await connection.BeginTransactionAsync();
        await using var setup=new NpgsqlCommand("SELECT pg_advisory_xact_lock(7102027); CREATE TABLE IF NOT EXISTS app_schema_migrations(version text PRIMARY KEY, applied_at timestamptz NOT NULL DEFAULT now());",connection,transaction);
        await setup.ExecuteNonQueryAsync();
        var assembly=typeof(Database).Assembly;
        foreach(var name in assembly.GetManifestResourceNames().Where(n=>n.Contains(".Migrations.")&&n.EndsWith(".sql")).Order())
        {
            await using var check=new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM app_schema_migrations WHERE version=$1)",connection,transaction);
            check.Parameters.AddWithValue(name);
            if((bool)(await check.ExecuteScalarAsync())!)continue;
            using var stream=assembly.GetManifestResourceStream(name)!;
            using var reader=new StreamReader(stream);
            await using var command=new NpgsqlCommand(await reader.ReadToEndAsync(),connection,transaction);
            await command.ExecuteNonQueryAsync();
            await using var record=new NpgsqlCommand("INSERT INTO app_schema_migrations(version) VALUES($1)",connection,transaction);
            record.Parameters.AddWithValue(name);await record.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();
    }
}
