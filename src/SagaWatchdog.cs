using Platform;
using Npgsql;
using System.Text.Json;
public sealed class SagaWatchdog(NpgsqlDataSource source, Processor processor, IConfiguration config, ILogger<SagaWatchdog> logger):BackgroundService
{
    public async Task Scan(CancellationToken ct)
    {
        await using var command=source.CreateCommand("""
            SELECT document::text FROM workflow_state
            WHERE document->>'status' IN ('Diagnosing','GeneratingBudget','WaitingApproval','WaitingPayment','ReadyForExecution')
            AND (document->>'updatedAt')::timestamptz < now() - ($1 * interval '1 minute')
            LIMIT 50
            """);
        command.Parameters.AddWithValue(config.GetValue<int?>("Saga:TimeoutMinutes")??1440);
        var states=new List<State>();
        await using(var reader=await command.ExecuteReaderAsync(ct))
            while(await reader.ReadAsync(ct))states.Add(JsonSerializer.Deserialize<State>(reader.GetString(0),JsonDefaults.Options)!);
        foreach(var state in states)
        {
            await processor.Handle(Message.Create(state.Id,"CancelOrder","os",new()),ct);
            logger.LogWarning("Saga timed out; compensation requested {OrderId}",state.Id);
        }
    }
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while(!ct.IsCancellationRequested)
        {
            try { await Scan(ct); }
            catch(Exception ex) when(ex is not OperationCanceledException){logger.LogError(ex,"Saga timeout scan failed");}
            await Task.Delay(TimeSpan.FromSeconds(30),ct);
        }
    }
}
