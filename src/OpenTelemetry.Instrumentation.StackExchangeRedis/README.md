# StackExchange.Redis Instrumentation for OpenTelemetry

| Status      |           |
| ----------- | --------- |
| Stability   | [Beta](../../README.md#beta) |
| Code Owners | [@matt-hensley](https://github.com/matt-hensley) |

[![NuGet version badge](https://img.shields.io/nuget/v/OpenTelemetry.Instrumentation.StackExchangeRedis)](https://www.nuget.org/packages/OpenTelemetry.Instrumentation.StackExchangeRedis)
[![NuGet download count badge](https://img.shields.io/nuget/dt/OpenTelemetry.Instrumentation.StackExchangeRedis)](https://www.nuget.org/packages/OpenTelemetry.Instrumentation.StackExchangeRedis)
[![codecov.io](https://codecov.io/gh/open-telemetry/opentelemetry-dotnet-contrib/branch/main/graphs/badge.svg?flag=unittests-Instrumentation.StackExchangeRedis)](https://app.codecov.io/gh/open-telemetry/opentelemetry-dotnet-contrib?flags[0]=unittests-Instrumentation.StackExchangeRedis)

This is an
[Instrumentation Library](https://github.com/open-telemetry/opentelemetry-specification/blob/main/specification/glossary.md#instrumentation-library),
which instruments
[StackExchange.Redis](https://www.nuget.org/packages/StackExchange.Redis/)
and collects traces about outgoing calls to Redis.

> [!NOTE]
> This component is based on the OpenTelemetry semantic conventions for
[traces](https://github.com/open-telemetry/semantic-conventions/blob/main/docs/database/redis.md).
These conventions are
[Experimental](https://github.com/open-telemetry/opentelemetry-specification/blob/main/specification/document-status.md),
and hence, this package is a [pre-release](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/VERSIONING.md#pre-releases).
Until a [stable
version](https://github.com/open-telemetry/opentelemetry-specification/blob/main/specification/telemetry-stability.md)
is released, there can be breaking changes.

## Steps to enable OpenTelemetry.Instrumentation.StackExchangeRedis

## Step 1: Install Package

Add a reference to the
[`OpenTelemetry.Instrumentation.StackExchangeRedis`](https://www.nuget.org/packages/OpenTelemetry.Instrumentation.StackExchangeRedis)
package. Also, add any other instrumentations & exporters you will need.

```shell
dotnet add package OpenTelemetry.Instrumentation.StackExchangeRedis
```

## Step 2: Enable StackExchange.Redis Instrumentation at application startup

StackExchange.Redis instrumentation must be enabled at application startup.
`AddRedisInstrumentation` method on `TracerProviderBuilder` must be called to
enable Redis instrumentation, passing the `IConnectionMultiplexer` instance used
to make Redis calls. Only those Redis calls made using the same instance of the
`IConnectionMultiplexer` will be instrumented.

The following example demonstrates adding StackExchange.Redis instrumentation to
a console application. This example also sets up the OpenTelemetry Console
exporter, which requires adding the package
[`OpenTelemetry.Exporter.Console`](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/src/OpenTelemetry.Exporter.Console/README.md)
to the application.

```csharp
using OpenTelemetry.Trace;

public class Program
{
    public static void Main(string[] args)
    {
        // Connect to the server.
        using var connection = ConnectionMultiplexer.Connect("localhost:6379");

        using var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddRedisInstrumentation(connection)
            .AddConsoleExporter()
            .Build();
    }
}
```

For an ASP.NET Core application, adding instrumentation is typically done in
the `ConfigureServices` of your `Startup` class. Refer to documentation for
[OpenTelemetry.Instrumentation.AspNetCore](../OpenTelemetry.Instrumentation.AspNetCore/README.md).

For an ASP.NET application, adding instrumentation is typically done in the
`Global.asax.cs`. Refer to documentation for [OpenTelemetry.Instrumentation.AspNet](../OpenTelemetry.Instrumentation.AspNet/README.md).

## Specify the Redis connection

The following sections cover different ways to specify the `StackExchange.Redis`
connection(s) that will be instrumented and captured by OpenTelemetry.

### Pass a Redis connection when calling AddRedisInstrumentation

The simplest thing to do is pass a created connection to the
`AddRedisInstrumentation` extension method:

```csharp
using var connection = ConnectionMultiplexer.Connect("localhost:6379");

using var tracerProvider = Sdk.CreateTracerProviderBuilder()
    .AddRedisInstrumentation(connection)
    .Build();
```

Whatever connection is specified will be collected by OpenTelemetry.

### Use the application IServiceProvider

Users using the `OpenTelemetry.Extensions.Hosting` package may prefer to manage
the Redis connection via the application `IServiceCollection`. To support this
scenario, if a connection is not passed to the `AddRedisInstrumentation`
extension manually one will be resolved one using the `IServiceProvider`:

```csharp
appBuilder.Services.AddSingleton<IConnectionMultiplexer>(
    sp => MyRedisConnectionHelper.CreateConnection(sp));

appBuilder.Services
    .AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddRedisInstrumentation());
```

Whatever connection is found in the `IServiceProvider` will be collected by
OpenTelemetry.

### Interact with StackExchangeRedisInstrumentation directly

For full control of the Redis connection(s) being instrumented the
`ConfigureRedisInstrumentation` extension is provided to expose the
`StackExchangeRedisInstrumentation` class directly:

```csharp
StackExchangeRedisInstrumentation redisInstrumentation = null;

using var tracerProvider = Sdk.CreateTracerProviderBuilder()
    .AddRedisInstrumentation()
    .ConfigureRedisInstrumentation(instrumentation => redisInstrumentation = instrumentation)
    .Build();

using var connection1 = ConnectionMultiplexer.Connect("localhost:6379");
redisInstrumentation.AddConnection(connection1);

using var connection2 = ConnectionMultiplexer.Connect("localhost:6380");
redisInstrumentation.AddConnection(connection2);
```

Connections may be added or removed at any time.

## Advanced configuration

This instrumentation can be configured to change the default behavior by using
`StackExchangeRedisCallsInstrumentationOptions`.

### FlushInterval

StackExchange.Redis has its own internal profiler. OpenTelemetry converts each
profiled command from the internal profiler to an Activity for collection. By
default, this conversion process flushes profiled commands on a 10 second
interval. The `FlushInterval` option can be used to adjust this interval.

The following example shows how to use `FlushInterval`.

```csharp
using var tracerProvider = Sdk.CreateTracerProviderBuilder()
    .AddRedisInstrumentation(
        connection,
        options => options.FlushInterval = TimeSpan.FromSeconds(5))
    .AddConsoleExporter()
    .Build();
```

### SetVerboseDatabaseStatements

StackExchange.Redis by default does not give detailed database statements like
what key or script was used during an operation. The `SetVerboseDatabaseStatements`
option can be used to enable gathering this more detailed information.

The following example shows how to use `SetVerboseDatabaseStatements`.

```csharp
using var tracerProvider = Sdk.CreateTracerProviderBuilder()
    .AddRedisInstrumentation(
        connection,
        options => options.SetVerboseDatabaseStatements = true)
    .AddConsoleExporter()
    .Build();
```

## Enrich

This option allows one to enrich the activity with additional information from the
raw `IProfiledCommand` object. The `Enrich` action is called only when
`activity.IsAllDataRequested` is `true`. It contains the activity itself (which can
be enriched), and the source profiled command object.

The following code snippet shows how to add additional tags using `Enrich`.

```csharp
using var tracerProvider = Sdk.CreateTracerProviderBuilder()
    .AddRedisInstrumentation(opt => opt.Enrich = (activity, command) =>
    {
        if (command.ElapsedTime < TimeSpan.FromMilliseconds(100))
        {
            activity.SetTag("is_fast", true);
        }
    })
    .Build();
```

## References

* [OpenTelemetry Project](https://opentelemetry.io/)
* [StackExchange.Redis Profiling](https://stackexchange.github.io/StackExchange.Redis/Profiling_v1.html)
