using System.Text.Json;
using Platform;
namespace Tests;
public sealed class MemoryStore : IStateStore
{
    readonly Dictionary<Guid,string> records = new();
    public Task<State?> Get(Guid id, CancellationToken ct = default)
        => Task.FromResult(records.TryGetValue(id, out var json) ? JsonSerializer.Deserialize<State>(json, JsonDefaults.Options) : null);
    public Task Save(State state, long expectedVersion, CancellationToken ct = default)
    {
        if (records.TryGetValue(state.Id, out var json) && JsonSerializer.Deserialize<State>(json, JsonDefaults.Options)!.Version != expectedVersion)
            throw new ConflictException();
        state.Version = expectedVersion + 1;
        records[state.Id] = JsonSerializer.Serialize(state, JsonDefaults.Options);
        return Task.CompletedTask;
    }
    public Task<IReadOnlyList<State>> Pending(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<State>>(records.Values.Select(x => JsonSerializer.Deserialize<State>(x,JsonDefaults.Options)!).Where(x => x.Outbox.Count > 0).ToArray());
}
public class ProcessorTests
{
    sealed class Workflow : IWorkflow
    {
        public int Calls;
        public Task Apply(State state, Message message, CancellationToken ct)
        { Calls++; state.Move("Applied"); state.Emit(message,"Next","os"); return Task.CompletedTask; }
    }
    [Fact] public async Task AtomicInboxOutboxAndDuplicateDetection()
    {
        var store = new MemoryStore(); var workflow = new Workflow(); var processor = new Processor(store, workflow);
        var message = Message.Create(Guid.NewGuid(),"Test","os",new());
        var first = await processor.Handle(message); var again = await processor.Handle(message);
        Assert.Equal(1, workflow.Calls); Assert.Single(again.Inbox); Assert.Single(again.Outbox);
        Assert.Single(again.History); Assert.Equal(1, again.Version);
        Assert.Equal(message.CorrelationId, again.Outbox[0].CorrelationId);
        Assert.Single(await store.Pending());
        Assert.Throws<InvalidOperationException>(() => first.Require("Other"));
        first.Require("Applied");
    }
    [Fact] public async Task InvalidEnvelopeRejected()
    {
        var processor = new Processor(new MemoryStore(),new Workflow());
        foreach(var message in new [] {
            new Message(Guid.Empty,Guid.NewGuid(),"x","os","c",new()),
            new Message(Guid.NewGuid(),Guid.Empty,"x","os","c",new()),
            new Message(Guid.NewGuid(),Guid.NewGuid(),"x","os","c",new(),2) })
            await Assert.ThrowsAsync<ArgumentException>(()=>processor.Handle(message));
    }
}
