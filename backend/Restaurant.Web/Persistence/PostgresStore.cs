using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Amazon.DynamoDBv2.DataModel;
using Npgsql;
using NpgsqlTypes;

namespace Restaurant.Web.Persistence;

// JSONB preserves the original domain models during the cloud migration. All reads
// and writes go to PostgreSQL; no DynamoDB client is involved.
public sealed class PostgresStore(NpgsqlDataSource source)
{
    internal static readonly JsonSerializerOptions Json = CreateJsonOptions();
    private static JsonSerializerOptions CreateJsonOptions()
    {
        var resolver=new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            // Some report identifiers are hidden from API responses but must be
            // retained in persistence for a faithful round trip.
            foreach(var field in info.Type.GetProperties().Where(p=>p.GetCustomAttributes(false).Any(a=>a.GetType()==typeof(DynamoDBHashKeyAttribute))))
            {
                var name=field.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? JsonNamingPolicy.CamelCase.ConvertName(field.Name);
                var property=info.Properties.FirstOrDefault(p=>p.Name==name);
                if(property is null){property=info.CreateJsonPropertyInfo(field.PropertyType,name);info.Properties.Add(property);}
                property.Get=field.GetValue;property.Set=field.SetValue;property.ShouldSerialize=null;
            }
        });
        return new(JsonSerializerDefaults.Web) { TypeInfoResolver=resolver };
    }
    private readonly AsyncLocal<Operation?> current = new();
    private readonly SemaphoreSlim writer = new(1,1);
    public Operation BeginOperation()
    {
        var operation=new Operation(source,writer);
        current.Value=operation;
        return operation;
    }
    private NpgsqlCommand Command(string sql) => current.Value?.Connection is { } connection
        ? new NpgsqlCommand(sql,connection,current.Value.Transaction) : source.CreateCommand(sql);
    private async Task<IDisposable?> Acquire()
    {
        if(current.Value is not { } operation)return null;
        await operation.Commands.WaitAsync();return new Release(operation.Commands);
    }
    private sealed class Release(SemaphoreSlim gate) : IDisposable { public void Dispose()=>gate.Release(); }

    // Serialize demo writes across app instances so read/validate/write workflows
    // (booking overlaps, guest cart updates) are atomic without rewriting services.
    public sealed class Operation(NpgsqlDataSource source,SemaphoreSlim writer) : IAsyncDisposable
    {
        internal SemaphoreSlim Commands { get; } = new(1,1);
        private bool writerAcquired;
        internal NpgsqlConnection? Connection { get; private set; }
        internal NpgsqlTransaction? Transaction { get; private set; }
        public async Task Begin(CancellationToken token)
        {
            await writer.WaitAsync(token);writerAcquired=true;
            Connection=await source.OpenConnectionAsync(token);
            Transaction=await Connection.BeginTransactionAsync(token);
            await using var command=new NpgsqlCommand("SELECT pg_advisory_xact_lock(7102026)",Connection,Transaction);
            await command.ExecuteNonQueryAsync(token);
        }
        public Task Commit(CancellationToken token) => Transaction!.CommitAsync(token);
        public async ValueTask DisposeAsync()
        {
            if(Transaction is not null)await Transaction.DisposeAsync();
            if(Connection is not null)await Connection.DisposeAsync();
            if(writerAcquired)writer.Release();
        }
    }

    public async Task<List<T>> Find<T>(string? property = null, string? value = null)
    {
        using var lease=await Acquire();
        await using var command = Command("SELECT payload::text FROM app_documents WHERE kind = $1" +
            (property is null ? "" : " AND payload ->> $2 = $3"));
        command.Parameters.AddWithValue(typeof(T).Name);
        if (property is not null)
        {
            var field = typeof(T).GetProperty(property) ?? throw new ArgumentException("Unknown model property", nameof(property));
            command.Parameters.AddWithValue(field.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? JsonNamingPolicy.CamelCase.ConvertName(property));
            command.Parameters.AddWithValue(value ?? "");
        }
        await using var reader = await command.ExecuteReaderAsync();
        var result = new List<T>();
        while (await reader.ReadAsync()) result.Add(JsonSerializer.Deserialize<T>(reader.GetString(0), Json)!);
        return result;
    }

    public async Task<T?> Get<T>(string id)
    {
        using var lease=await Acquire();
        await using var command = Command("SELECT payload::text FROM app_documents WHERE kind=$1 AND id=$2");
        command.Parameters.AddWithValue(typeof(T).Name);
        command.Parameters.AddWithValue(id);
        var result = await command.ExecuteScalarAsync();
        return result is string json ? JsonSerializer.Deserialize<T>(json, Json) : default;
    }

    public async Task Save<T>(T entity, bool insertOnly = false)
    {
        using var lease=await Acquire();
        await using var command = Command("INSERT INTO app_documents(kind,id,payload) VALUES($1,$2,$3) ON CONFLICT(kind,id) " +
            (insertOnly ? "DO NOTHING" : "DO UPDATE SET payload=EXCLUDED.payload"));
        command.Parameters.AddWithValue(typeof(T).Name);
        command.Parameters.AddWithValue(Key(entity!));
        command.Parameters.AddWithValue(NpgsqlDbType.Jsonb, JsonSerializer.Serialize(entity, Json));
        await command.ExecuteNonQueryAsync();
    }

    public async Task Delete<T>(T entity)
    {
        using var lease=await Acquire();
        await using var command = Command("DELETE FROM app_documents WHERE kind=$1 AND id=$2");
        command.Parameters.AddWithValue(typeof(T).Name);
        command.Parameters.AddWithValue(Key(entity!));
        await command.ExecuteNonQueryAsync();
    }

    private static string Key(object entity) => string.Join("|", entity.GetType().GetProperties()
        .Where(p => p.GetCustomAttributes(false).Any(a => a.GetType() == typeof(DynamoDBHashKeyAttribute) || a.GetType() == typeof(DynamoDBRangeKeyAttribute)))
        .Select(p => Convert.ToString(p.GetValue(entity), System.Globalization.CultureInfo.InvariantCulture)));

    public async Task<bool> CheckConnection(CancellationToken cancellationToken)
    {
        await using var command = source.CreateCommand("SELECT 1");
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }
}
