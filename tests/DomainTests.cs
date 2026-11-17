using Orders;
namespace Tests;
public class DomainTests
{
    [Fact] public void CompleteFlow()
    {
        var order = new ServiceOrder("52998224725", "BRA2E19", "Oil leak");
        order.BudgetGenerated(); order.Approve(); order.PaymentConfirmed(); order.StartExecution(); order.Finish();
        Assert.Equal(OrderStatus.Finished, order.Status);
        Assert.Equal(6, order.History.Count);
    }
    [Fact] public void CannotExecuteBeforePayment()
    {
        var order = new ServiceOrder("cpf", "plate", "repair");
        Assert.Throws<InvalidOperationException>(() => order.StartExecution());
    }
    [Fact] public void CompensationMustCompleteBeforeCancellation()
    {
        var order = new ServiceOrder("cpf", "plate", "repair");
        Assert.Throws<InvalidOperationException>(() => order.CompleteCompensation());
        order.RequestCompensation(); order.CompleteCompensation();
        Assert.Equal(OrderStatus.Canceled, order.Status);
        Assert.Throws<InvalidOperationException>(() => order.RequestCompensation());
    }
    [Theory]
    [InlineData("", "plate", "repair")]
    [InlineData("cpf", "", "repair")]
    [InlineData("cpf", "plate", "")]
    public void RequiredData(string cpf, string plate, string description)
        => Assert.Throws<ArgumentException>(() => new ServiceOrder(cpf, plate, description));
}

