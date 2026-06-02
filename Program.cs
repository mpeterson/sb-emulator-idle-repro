using System.Diagnostics;
using Azure.Messaging.ServiceBus;

// Minimal reproduction for:
// https://github.com/Azure/azure-service-bus-emulator-installer/issues/142
//
// Demonstrates that the Service Bus emulator tears down the AMQP link of an
// idle ServiceBusSessionProcessor while the message handler is still actively
// processing the locked session, by simulating long-running work with
// Task.Delay. The real Service Bus service does not exhibit this behaviour.

const string ConnectionString =
    "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;" +
    "SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true";

const string QueueName = "idle-repro";

// Idle window the handler will sit through. Set generously above the emulator's
// observed drop threshold (~60s in some builds, advertised as 300_000ms).
var idleWindow = TimeSpan.FromMinutes(6);

var sw = Stopwatch.StartNew();
void Log(string msg) =>
    Console.WriteLine($"[{sw.Elapsed:hh\\:mm\\:ss\\.fff}] {msg}");

var clientOptions = new ServiceBusClientOptions
{
    TransportType = ServiceBusTransportType.AmqpTcp,
    RetryOptions = new ServiceBusRetryOptions { MaxRetries = 0 },
};

await using var client = new ServiceBusClient(ConnectionString, clientOptions);

// Seed one session message so the processor accepts a session and locks it.
Log("Seeding one session message ...");
await using (var sender = client.CreateSender(QueueName))
{
    await sender.SendMessageAsync(new ServiceBusMessage("payload")
    {
        SessionId = "repro-session",
        MessageId = Guid.NewGuid().ToString(),
    });
}
Log("Seeded.");

var processor = client.CreateSessionProcessor(QueueName, new ServiceBusSessionProcessorOptions
{
    MaxConcurrentSessions = 1,
    AutoCompleteMessages = false,
    SessionIdleTimeout = TimeSpan.FromHours(1),
});

var handlerStarted = new TaskCompletionSource();
var observedException = new TaskCompletionSource<Exception>();

processor.ProcessMessageAsync += async args =>
{
    Log($"Handler received session='{args.SessionId}' messageId='{args.Message.MessageId}'");
    handlerStarted.TrySetResult();

    try
    {
        Log($"Sleeping for {idleWindow.TotalSeconds:F0}s with no broker I/O ...");
        await Task.Delay(idleWindow, args.CancellationToken);
        Log("Sleep finished; attempting CompleteMessageAsync ...");
        await args.CompleteMessageAsync(args.Message);
        Log("Completed.");
    }
    catch (Exception ex)
    {
        Log($"Handler threw {ex.GetType().FullName}: {ex.Message}");
        observedException.TrySetResult(ex);
        throw;
    }
};

processor.ProcessErrorAsync += args =>
{
    Log($"ProcessErrorAsync source={args.ErrorSource} ex={args.Exception.GetType().FullName}: {args.Exception.Message}");
    observedException.TrySetResult(args.Exception);
    return Task.CompletedTask;
};

Log("Starting processor ...");
await processor.StartProcessingAsync();

await handlerStarted.Task;

// Surface whichever happens first: the handler completing the sleep, or
// the broker tearing down the link.
var firstFailure = await Task.WhenAny(
    observedException.Task,
    Task.Delay(idleWindow + TimeSpan.FromSeconds(30)));

if (firstFailure == observedException.Task)
{
    Log("REPRODUCED: broker dropped the link before the handler finished.");
    Environment.ExitCode = 2;
}
else
{
    Log("NO REPRODUCTION: handler completed without a broker disconnect.");
    Environment.ExitCode = 0;
}

await processor.StopProcessingAsync();
