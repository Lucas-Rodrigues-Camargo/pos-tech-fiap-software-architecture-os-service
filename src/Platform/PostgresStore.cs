using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
namespace Platform;
public sealed class PostgresStore(NpgsqlDataSource source) : IStateStore
{
    public async Task Initialize(CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT pg_advisory_xact_lock(406001);
            CREATE TABLE IF NOT EXISTS workflow_state (
                id uuid PRIMARY KEY, version bigint NOT NULL, document jsonb NOT NULL
            );
            CREATE INDEX IF NOT EXISTS workflow_pending ON workflow_state ((jsonb_array_length(document->'outbox')))
                WHERE jsonb_array_length(document->'outbox') > 0;
            """;
        await command.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
    }
    public async Task<State?> Get(Guid id, CancellationToken ct = default)
    {
        await using var command = source.CreateCommand("SELECT document::text FROM workflow_state WHERE id = $1");
        command.Parameters.AddWithValue(id);
        return await command.ExecuteScalarAsync(ct) is string json ? JsonSerializer.Deserialize<State>(json, JsonDefaults.Options) : null;
    }
    public async Task Save(State state, long expectedVersion, CancellationToken ct = default)
    {
        state.Version = expectedVersion + 1;
        var json = JsonSerializer.Serialize(state, JsonDefaults.Options);
        await using var command = source.CreateCommand(expectedVersion == 0
            ? "INSERT INTO workflow_state VALUES ($1,$2,$3) ON CONFLICT DO NOTHING"
            : "UPDATE workflow_state SET version=$2,document=$3 WHERE id=$1 AND version=$4");
        command.Parameters.AddWithValue(state.Id);
        command.Parameters.AddWithValue(state.Version);
        command.Parameters.AddWithValue(NpgsqlDbType.Jsonb, json);
        if (expectedVersion != 0) command.Parameters.AddWithValue(expectedVersion);
        if (await command.ExecuteNonQueryAsync(ct) != 1) throw new ConflictException();
    }
    public async Task<IReadOnlyList<State>> Pending(CancellationToken ct = default)
    {
        await using var command = source.CreateCommand("SELECT document::text FROM workflow_state WHERE jsonb_array_length(document->'outbox') > 0 LIMIT 50");
        await using var reader = await command.ExecuteReaderAsync(ct);
        var result = new List<State>();
        while (await reader.ReadAsync(ct)) result.Add(JsonSerializer.Deserialize<State>(reader.GetString(0), JsonDefaults.Options)!);
        return result;
    }
}
