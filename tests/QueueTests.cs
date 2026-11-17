using Amazon.SQS;
using Message = Platform.Message;
using Amazon.SQS.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Platform;
using System.Text.Json;
namespace Tests;
public class QueueTests
{
    [Fact] public async Task PublishesOutboxAndConsumesInbox()
    {
        var store=new MemoryStore(); var id=Guid.NewGuid();
        var state=new State{Id=id};
        state.Outbox.Add(Message.Create(id,"Outbound","os",new()));
        await store.Save(state,0);
        var received=Message.Create(Guid.NewGuid(),"Inbound","os",new());
        var workflow=new SignalWorkflow();
        var sqs=new Mock<IAmazonSQS>(); var calls=0;
        sqs.Setup(x=>x.SendMessageAsync(It.IsAny<string>(),It.IsAny<string>(),It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SendMessageResponse());
        sqs.Setup(x=>x.DeleteMessageAsync(It.IsAny<string>(),It.IsAny<string>(),It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteMessageResponse());
        sqs.Setup(x=>x.ReceiveMessageAsync(It.IsAny<ReceiveMessageRequest>(),It.IsAny<CancellationToken>()))
            .Returns(async (ReceiveMessageRequest _,CancellationToken ct)=>{
                if(Interlocked.Increment(ref calls)==1) return new ReceiveMessageResponse {
                    Messages=[new Amazon.SQS.Model.Message{Body=JsonSerializer.Serialize(received,JsonDefaults.Options),ReceiptHandle="handle",MessageId="id"}] };
                await Task.Delay(Timeout.Infinite,ct); return new ReceiveMessageResponse();
            });
        var services=new ServiceCollection().AddSingleton<IStateStore>(store).AddSingleton<IWorkflow>(workflow).AddSingleton<Processor>().BuildServiceProvider();
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{
            ["Messaging:InputQueue"]="input",["Messaging:Queues:os"]="output"}).Build();
        var worker=new QueueWorker(services,sqs.Object,config,NullLogger<QueueWorker>.Instance);
        await worker.StartAsync(default);
        await workflow.Signal.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await worker.StopAsync(default);
        Assert.Empty((await store.Get(id))!.Outbox);
        sqs.Verify(x=>x.SendMessageAsync("output",It.IsAny<string>(),It.IsAny<CancellationToken>()),Times.Once);
        Assert.NotNull(await store.Get(received.OrderId));
    }
    sealed class SignalWorkflow:IWorkflow
    {
        public readonly TaskCompletionSource Signal=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Apply(State state,Platform.Message message,CancellationToken ct) { state.Move("Done");Signal.TrySetResult();return Task.CompletedTask; }
    }
}
