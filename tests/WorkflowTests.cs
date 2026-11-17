using Platform;
namespace Tests;

public class WorkflowTests
{
    [Fact] public void NullCpfIsRejected() => Assert.False(Cpf.IsValid(null));
    [Theory]
    [InlineData("CompensationPending")]
    [InlineData("Canceled")]
    [InlineData("ManualIntervention")]
    public async Task LateEventsCannotRestartCompensatedSaga(string status)
    {
        var state = new State { Id = Guid.NewGuid(), Status = status };
        foreach (var type in new[] { "DiagnosisCompleted", "BudgetGenerated", "BudgetApproved", "PaymentConfirmed", "BudgetRejected" })
            await Apply(state, type);
        Assert.Equal(status, state.Status);
        Assert.Empty(state.Outbox);
    }
    [Fact]
    public async Task RepairStartRacingCancellationRequiresIntervention()
    {
        var state = new State { Id = Guid.NewGuid(), Status = "CompensationPending" };
        await Apply(state, "RepairStarted");
        Assert.Equal("ManualIntervention", state.Status);
    }
    readonly ServiceWorkflow workflow = new();
    static Message M(State s, string type, Payload? data = null) => Message.Create(s.Id, type, "os", data ?? new());
    async Task Apply(State s, string type, Payload? data = null) => await workflow.Apply(s, M(s, type, data), default);
    [Fact]
    public async Task FullSaga()
    {
        var s = new State { Id = Guid.NewGuid() };
        await Apply(s, "OpenOrder", new(Cpf: "52998224725", Plate: "BRA2E19", Description: "Leak"));
        await Apply(s, "DiagnosisCompleted", new(Amount: 100, Diagnosis: "Seal"));
        await Apply(s, "BudgetGenerated"); await Apply(s, "BudgetApproved"); await Apply(s, "PaymentConfirmed");
        await Apply(s, "RepairStarted"); await Apply(s, "RepairFinished");
        Assert.Equal("Finished", s.Status);
        Assert.Equal(new[] { "CreateJob", "GenerateBudget", "AuthorizeRepair" }, s.Outbox.Select(x => x.Type));
    }
    [Theory]
    [InlineData("CancelOrder")]
    [InlineData("BudgetRejected")]
    public async Task CompensationIsOrdered(string trigger)
    {
        var s = new State { Id = Guid.NewGuid(), Status = "WaitingApproval" };
        await Apply(s, trigger);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Apply(s, "BudgetCanceled"));
        await Apply(s, "JobCanceled"); await Apply(s, "BudgetCanceled");
        Assert.Equal("Canceled", s.Status); Assert.True(s.BillingCompensated); Assert.True(s.ExecutionCompensated);
    }
    [Fact]
    public async Task RepairAlreadyStartedRequiresManualIntervention()
    {
        var s = new State { Id = Guid.NewGuid(), Status = "CompensationPending" };
        await Apply(s, "CompensationFailed"); Assert.Equal("ManualIntervention", s.Status);
        await Assert.ThrowsAsync<ArgumentException>(() => Apply(s, "Unknown"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Apply(s, "RepairFinished"));
    }
    [Theory]
    [InlineData("11111111111")]
    [InlineData("abc")]
    [InlineData("52998224724")]
    [InlineData("1234567890x")]
    public async Task InvalidCpfRejected(string cpf)
    {
        Assert.False(Cpf.IsValid(cpf));
        await Assert.ThrowsAsync<ArgumentException>(() => Apply(new State(), "OpenOrder", new(Cpf: cpf)));
    }
}
