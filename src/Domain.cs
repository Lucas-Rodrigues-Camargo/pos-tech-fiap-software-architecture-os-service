namespace Orders;
public enum OrderStatus { Received, WaitingApproval, WaitingPayment, ReadyForExecution, InExecution, Finished, CompensationPending, Canceled }
public sealed class ServiceOrder
{
    public Guid Id { get; } = Guid.NewGuid();
    public string CustomerCpf { get; }
    public string VehiclePlate { get; }
    public string Description { get; }
    public OrderStatus Status { get; private set; } = OrderStatus.Received;
    public List<string> History { get; } = ["Received"];
    public ServiceOrder(string customerCpf, string vehiclePlate, string description)
    {
        if (string.IsNullOrWhiteSpace(customerCpf) || string.IsNullOrWhiteSpace(vehiclePlate) || string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Customer, vehicle and description are required.");
        CustomerCpf = customerCpf; VehiclePlate = vehiclePlate; Description = description;
    }
    public void BudgetGenerated() => Move(OrderStatus.Received, OrderStatus.WaitingApproval);
    public void Approve() => Move(OrderStatus.WaitingApproval, OrderStatus.WaitingPayment);
    public void PaymentConfirmed() => Move(OrderStatus.WaitingPayment, OrderStatus.ReadyForExecution);
    public void StartExecution() => Move(OrderStatus.ReadyForExecution, OrderStatus.InExecution);
    public void Finish() => Move(OrderStatus.InExecution, OrderStatus.Finished);
    public void RequestCompensation()
    {
        if (Status is OrderStatus.Finished or OrderStatus.Canceled) throw new InvalidOperationException("Terminal order.");
        Status = OrderStatus.CompensationPending; History.Add(Status.ToString());
    }
    public void CompleteCompensation() => Move(OrderStatus.CompensationPending, OrderStatus.Canceled);
    private void Move(OrderStatus expected, OrderStatus next)
    {
        if (Status != expected) throw new InvalidOperationException($"Expected {expected}, got {Status}.");
        Status = next; History.Add(next.ToString());
    }
}

