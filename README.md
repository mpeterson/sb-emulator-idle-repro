# Service Bus emulator — idle session-receiver drop repro

Minimal reproduction for
[Azure/azure-service-bus-emulator-installer#142](https://github.com/Azure/azure-service-bus-emulator-installer/issues/142).

A `ServiceBusSessionProcessor` accepts one session message and the handler
simulates long-running work by sleeping. The emulator tears down the AMQP
link while the handler is still actively holding the session lock; the real
Service Bus service does not.

## What you need

- Docker
- .NET 10 SDK

## Run it

```bash
cp .env.example .env       # accept the emulator EULA
docker compose up -d       # start emulator + sqledge
dotnet run                 # run the repro
docker compose down -v     # cleanup
```

## What you should see

The handler picks up the session, sleeps for ~6 minutes with no broker I/O,
and is interrupted by an `AmqpException` reporting that the connection was
closed because there were no active links in the past 300 000 ms — even
though the handler is actively processing the locked session and the SDK's
background session-lock renewal is firing on the same client.

Expected exit code: `2` (reproduced).

## What the connection string means

The emulator accepts a fixed dev connection string:

```
Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true
```

`UseDevelopmentEmulator=true` tells the .NET SDK to skip TLS and connect to
`localhost:5672` over plain AMQP TCP, which is what `docker-compose.yml`
exposes.
