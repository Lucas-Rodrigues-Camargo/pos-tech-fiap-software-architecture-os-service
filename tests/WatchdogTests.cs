using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Platform;
namespace Tests;
public class WatchdogTests
{
    [Fact] public async Task ExpiredSagaStartsCompensationWithoutCancelingUnpaidWorkPrematurely()
    {
        await using var source=NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("TEST_POSTGRES")!);
        var store=new PostgresStore(source); await store.Initialize(default);
        var state=new State{Id=Guid.NewGuid(),Status="Diagnosing",UpdatedAt=DateTime.UtcNow.AddDays(-2)};
        await store.Save(state,0);
        var processor=new Processor(store,new ServiceWorkflow());
        var worker=new SagaWatchdog(source,processor,new ConfigurationBuilder().Build(),NullLogger<SagaWatchdog>.Instance);
        await worker.Scan(default);
        var saved=(await store.Get(state.Id))!;
        Assert.Equal("CompensationPending",saved.Status);
        Assert.Equal("CancelJob",saved.Outbox.Single().Type);
        await worker.StartAsync(default);
        await worker.StopAsync(default);
    }
}
