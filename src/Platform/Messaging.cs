using System.Text.Json;
using System.Diagnostics;
namespace Platform;
public sealed record Message(Guid Id, Guid OrderId, string Type, string Destination, string CorrelationId, Payload Data, int Version = 1, string TraceParent = "")
{
    public static Message Create(Guid orderId, string type, string destination, Payload data, string? correlation = null)
        => new(Guid.NewGuid(), orderId, type, destination, correlation ?? Guid.NewGuid().ToString("N"), data, TraceParent: Activity.Current?.Id ?? "");
}
public sealed record Payload(string Cpf = "", string Plate = "", string Description = "", decimal Amount = 0,
    string PaymentId = "", string Diagnosis = "", string CheckoutUrl = "", bool Approved = true);
public sealed class State
{
    public Guid Id { get; set; }
    public long Version { get; set; }
    public string Status { get; set; } = "New";
    public Payload Data { get; set; } = new();
    public List<Guid> Inbox { get; set; } = [];
    public List<Message> Outbox { get; set; } = [];
    public List<Transition> History { get; set; } = [];
    public bool BillingCompensated { get; set; }
    public bool ExecutionCompensated { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public void Move(string next)
    {
        History.Add(new(Status, next, DateTime.UtcNow));
        Status = next; UpdatedAt = DateTime.UtcNow;
    }
    public void Require(params string[] expected)
    {
        if (!expected.Contains(Status)) throw new InvalidOperationException($"Cannot perform operation in {Status}.");
    }
    public void Emit(Message cause, string type, string destination, Payload? data = null)
        => Outbox.Add(Message.Create(Id, type, destination, data ?? Data, cause.CorrelationId));
}
public sealed record Transition(string From, string To, DateTime At);
public interface IStateStore
{
    Task<State?> Get(Guid id, CancellationToken ct = default);
    Task Save(State state, long expectedVersion, CancellationToken ct = default);
    Task<IReadOnlyList<State>> Pending(CancellationToken ct = default);
}
public interface IWorkflow { Task Apply(State state, Message message, CancellationToken ct); }
public sealed class ConflictException : Exception;
public sealed class Processor(IStateStore store, IWorkflow workflow, ILogger<Processor>? logger = null)
{
    private static readonly ActivitySource Traces = new("Oficina.Workflow");
    public async Task<State> Handle(Message message, CancellationToken ct = default)
    {
        ActivityContext.TryParse(message.TraceParent, null, out var parent);
        using var activity = Traces.StartActivity(message.Type, ActivityKind.Consumer, parent);
        activity?.SetTag("saga.id", message.OrderId.ToString());
        activity?.SetTag("correlation.id", message.CorrelationId);
        if (message.Version != 1 || message.Id == Guid.Empty || message.OrderId == Guid.Empty)
            throw new ArgumentException("Invalid event envelope.");
        var state = await store.Get(message.OrderId, ct) ?? new State { Id = message.OrderId };
        if (state.Inbox.Contains(message.Id)) return state;
        var expected = state.Version;
        var fromStatus = state.Status;
        var duration = (DateTime.UtcNow - state.UpdatedAt).TotalSeconds;
        await workflow.Apply(state, message, ct);
        state.Inbox.Add(message.Id);
        await store.Save(state, expected, ct);
        if (fromStatus != state.Status)
            logger?.LogInformation("Workflow transition {EventType} {FromStatus} {Status} {DurationSeconds} {OrderId} {CorrelationId}",
                message.Type, fromStatus, state.Status, duration, state.Id, message.CorrelationId);
        return state;
    }
}
public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
