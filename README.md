> [!NOTE]
> **Archived — the bug this repository reproduces is fixed.**
> This repro targets emulator **2.0.0** (`sha256:a00c9626…`), which was
> `latest` when the repo was created. The bug is fixed in emulator
> **2.0.1** (`sha256:5a96d893…`, the current `latest`). This repository is
> archived and kept for historical reference only. See
> [Status](#status-fixed-in-emulator-201) below for details.

# Service Bus emulator — idle session-receiver drop repro

Minimal reproduction for
[Azure/azure-service-bus-emulator-installer#142](https://github.com/Azure/azure-service-bus-emulator-installer/issues/142).

A `ServiceBusSessionProcessor` accepts one session message and the handler
simulates long-running work by sleeping. The emulator tears down the AMQP
link while the handler is still actively holding the session lock; the real
Service Bus service does not.

## Status: fixed in emulator 2.0.1

This repro was created against emulator **2.0.0**
(`sha256:a00c9626c8960f6b9be6178aa91a7ac8f1a102c0d9deda7603a1ba0ac9d9ab51`),
which was the `latest` tag at the time. That version reproduces the bug
(handler is interrupted at ~4:51 with a `SessionLockLost` /
"no active links in the past 300000 ms" error; exit code `2`).

The bug is **fixed in emulator 2.0.1**
(`sha256:5a96d893b245031740f7d46e0fe5ff282d24b78c4b7d761dd57590f3f010a9b3`,
the current `latest`). Verified on a `linux/amd64` host: the handler sleeps
the full 6 minutes, completes the message, and exits `0` with no broker
disconnect. Note that 2.0.1 had no published release notes or changelog at
the time of writing.

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

> **Heads up: reproduction is architecture-dependent.** This repros on the
> `linux/amd64` image variant (Docker Desktop on Windows) but **not** on the
> `linux/arm64` variant (Docker Desktop on Apple Silicon) — same multi-arch
> manifest digest. On an arm64 host, force `platform: linux/amd64` on the
> emulator service in `docker-compose.yml` to exercise the buggy variant.

## What the connection string means

The emulator accepts a fixed dev connection string:

```
Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true
```

`UseDevelopmentEmulator=true` tells the .NET SDK to skip TLS and connect to
`localhost:5672` over plain AMQP TCP, which is what `docker-compose.yml`
exposes.
