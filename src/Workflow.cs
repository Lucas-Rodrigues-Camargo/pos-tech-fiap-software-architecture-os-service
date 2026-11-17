namespace Platform;
public sealed class ServiceWorkflow : IWorkflow
{
    public Task Apply(State s, Message m, CancellationToken ct)
    {
        // Late forward events cannot restart work after compensation has begun.
        if (s.Status is "CompensationPending" or "Canceled" or "ManualIntervention"
            && m.Type is "DiagnosisCompleted" or "BudgetGenerated" or "BudgetApproved" or "PaymentConfirmed" or "BudgetRejected" or "RepairQueued")
            return Task.CompletedTask;
        if (s.Status == "CompensationPending" && m.Type == "RepairStarted")
        {
            s.Move("ManualIntervention");
            return Task.CompletedTask;
        }
        switch (m.Type)
        {
            case "OpenOrder":
                s.Require("New");
                if (!Cpf.IsValid(m.Data.Cpf) || string.IsNullOrWhiteSpace(m.Data.Plate) || string.IsNullOrWhiteSpace(m.Data.Description))
                    throw new ArgumentException("Valid CPF, vehicle and description required.");
                s.Data = m.Data; s.Move("Diagnosing"); s.Emit(m, "CreateJob", "execution");
                break;
            case "DiagnosisCompleted":
                s.Require("Diagnosing"); s.Data = s.Data with { Diagnosis = m.Data.Diagnosis, Amount = m.Data.Amount };
                s.Move("GeneratingBudget"); s.Emit(m, "GenerateBudget", "billing");
                break;
            case "BudgetGenerated": s.Require("GeneratingBudget"); s.Move("WaitingApproval"); break;
            case "BudgetApproved": s.Require("WaitingApproval"); s.Move("WaitingPayment"); break;
            case "PaymentConfirmed":
                s.Require("WaitingPayment"); s.Move("ReadyForExecution"); s.Emit(m, "AuthorizeRepair", "execution");
                break;
            case "RepairQueued": s.Require("ReadyForExecution"); break;
            case "RepairStarted": s.Require("ReadyForExecution"); s.Move("InExecution"); break;
            case "RepairFinished": s.Require("InExecution"); s.Move("Finished"); break;
            case "CancelOrder":
            case "BudgetRejected":
                s.Require("Diagnosing", "GeneratingBudget", "WaitingApproval", "WaitingPayment", "ReadyForExecution");
                s.Move("CompensationPending"); s.Emit(m, "CancelJob", "execution");
                break;
            case "JobCanceled":
                s.Require("CompensationPending"); s.ExecutionCompensated = true;
                s.Emit(m, "CancelBudget", "billing");
                break;
            case "BudgetCanceled":
                s.Require("CompensationPending");
                if (!s.ExecutionCompensated) throw new InvalidOperationException("Execution compensation must complete first.");
                s.BillingCompensated = true; s.Move("Canceled"); break;
            case "CompensationFailed":
                s.Require("CompensationPending"); s.Move("ManualIntervention"); break;
            default: throw new ArgumentException("Unknown event type.");
        }
        return Task.CompletedTask;
    }
}
public static class Cpf
{
    public static bool IsValid(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length != 11 || value.Any(c => !char.IsAsciiDigit(c)) || value.Distinct().Count() == 1) return false;
        for (var length = 9; length <= 10; length++)
        {
            var sum = Enumerable.Range(0, length).Sum(i => (value[i]-'0') * (length+1-i));
            var digit = (sum*10)%11; if (digit == 10) digit = 0;
            if (value[length]-'0' != digit) return false;
        }
        return true;
    }
}
