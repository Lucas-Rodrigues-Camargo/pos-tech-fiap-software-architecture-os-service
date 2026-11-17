using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
namespace Platform;
public sealed class PostgresStore(NpgsqlDataSource source) : IStateStore
{
    public async Task Initialize(CancellationToken ct)
    {
        await using var command = source.CreateCommand("""
            CREATE TABLE IF NOT EXISTS workflow_state (
                id uuid PRIMARY KEY, version bigint NOT NULL, document jsonb NOT NULL
            );
            CREATE INDEX IF NOT EXISTS workflow_pending ON workflow_state ((jsonb_array_length(document->'outbox')))
                WHERE jsonb_array_length(document->'outbox') > 0;
            """);
        await command.ExecuteNonQueryAsync(ct);
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
public sealed class DynamoStore(IAmazonDynamoDB db, string table) : IStateStore
{
    private static Dictionary<string, AttributeValue> Key(Guid id) => new() { ["id"] = new AttributeValue { S = id.ToString() } };
    public async Task<State?> Get(Guid id, CancellationToken ct = default)
    {
        var result = await db.GetItemAsync(new GetItemRequest { TableName = table, Key = Key(id), ConsistentRead = true }, ct);
        return result.Item?.TryGetValue("document", out var json) == true ? JsonSerializer.Deserialize<State>(json.S, JsonDefaults.Options) : null;
    }
    public async Task Save(State state, long expectedVersion, CancellationToken ct = default)
    {
        state.Version = expectedVersion + 1;
        var item = Key(state.Id);
        item["version"] = new() { N = state.Version.ToString() };
        item["pending"] = new() { N = state.Outbox.Count.ToString() };
        item["document"] = new() { S = JsonSerializer.Serialize(state, JsonDefaults.Options) };
        try
        {
            await db.PutItemAsync(new PutItemRequest
            {
                TableName = table, Item = item,
                ConditionExpression = expectedVersion == 0 ? "attribute_not_exists(id)" : "#v = :expected",
                ExpressionAttributeNames = expectedVersion == 0 ? null : new() { ["#v"] = "version" },
                ExpressionAttributeValues = expectedVersion == 0 ? null : new() { [":expected"] = new() { N = expectedVersion.ToString() } }
            }, ct);
        }
        catch (ConditionalCheckFailedException) { throw new ConflictException(); }
    }
    public async Task<IReadOnlyList<State>> Pending(CancellationToken ct = default)
    {
        var states = new List<State>();
        Dictionary<string, AttributeValue>? cursor = null;
        do
        {
            var page = await db.ScanAsync(new ScanRequest
            {
                TableName = table, FilterExpression = "pending > :zero",
                ExpressionAttributeValues = new() { [":zero"] = new() { N = "0" } },
                ExclusiveStartKey = cursor, ConsistentRead = true
            }, ct);
            states.AddRange((page.Items ?? []).Select(item => JsonSerializer.Deserialize<State>(item["document"].S, JsonDefaults.Options)!));
            cursor = page.LastEvaluatedKey;
        } while (cursor?.Count > 0 && states.Count < 50);
        return states;
    }
}
