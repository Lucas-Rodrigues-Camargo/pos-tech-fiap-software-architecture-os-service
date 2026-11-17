using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
namespace Platform;
public sealed class QueueWorker(IServiceProvider services, IAmazonSQS sqs, IConfiguration config, ILogger<QueueWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Dispatch(stoppingToken);
                var input = config["Messaging:InputQueue"] ?? throw new InvalidOperationException("Input queue required.");
                var batch = await sqs.ReceiveMessageAsync(new ReceiveMessageRequest
                {
                    QueueUrl = input, MaxNumberOfMessages = 10, WaitTimeSeconds = 10, VisibilityTimeout = 120
                }, stoppingToken);
                foreach (var record in batch.Messages ?? [])
                {
                    try
                    {
                        var message = JsonSerializer.Deserialize<Message>(record.Body, JsonDefaults.Options)!;
                        if (message == null || message.Destination != config["Service:Name"])
                            throw new ArgumentException("Message destination does not match this service.");
                        await services.GetRequiredService<Processor>().Handle(message, stoppingToken);
                        await sqs.DeleteMessageAsync(input, record.ReceiptHandle, stoppingToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    { logger.LogError(ex, "Message processing failed {MessageId}; SQS will retry or DLQ", record.MessageId); }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Messaging unavailable");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
    public async Task Dispatch(CancellationToken ct)
    {
        var store = services.GetRequiredService<IStateStore>();
        foreach (var state in await store.Pending(ct))
        {
            var version = state.Version;
            foreach (var message in state.Outbox.ToArray())
            {
                var queue = config[$"Messaging:Queues:{message.Destination}"]
                    ?? throw new InvalidOperationException($"Queue for {message.Destination} missing");
                await sqs.SendMessageAsync(queue, JsonSerializer.Serialize(message, JsonDefaults.Options), ct);
                state.Outbox.Remove(message);
                logger.LogInformation("Event published {EventType} {OrderId} {CorrelationId}", message.Type, message.OrderId, message.CorrelationId);
            }
            try { await store.Save(state, version, ct); }
            catch (ConflictException) { /* A concurrent write retains outbox; duplicate delivery is safe through inbox. */ }
        }
    }
}
