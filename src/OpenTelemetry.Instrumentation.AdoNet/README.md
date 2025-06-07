# OpenTelemetry ADO.NET Instrumentation

This library provides automatic instrumentation for ADO.NET database interactions, allowing you to capture telemetry data for database calls made through any ADO.NET-compliant provider.

**Package Name:** `OpenTelemetry.Instrumentation.AdoNet` (This will be the NuGet package name once published)

## Supported .NET Versions

*   .NET Standard 2.0
*   .NET 6.0 and later

## Installation

Add the NuGet package to your project:

```sh
dotnet add package OpenTelemetry.Instrumentation.AdoNet
```

## Usage

The ADO.NET instrumentation works by wrapping your existing `DbConnection` objects. You need to explicitly instrument your connections using the `AdoNetInstrumentation.InstrumentConnection` method.

### 1. Enable ADO.NET Instrumentation in your TracerProvider

First, you need to add ADO.NET instrumentation to your `TracerProvider` configuration.

```csharp
using OpenTelemetry.Trace;
using System.Data.Common; // For DbConnection
using Microsoft.Data.Sqlite; // Example provider

// ...

public static class TelemetryConfig
{
    public static TracerProvider? MyTracerProvider;

    public static void Initialize()
    {
        MyTracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddSource("MyApplicationActivitySource") // Your application's activity source
            .AddAdoNetInstrumentation(options =>
            {
                // Configure options (see below)
                options.SetDbStatementForText = true;
                options.RecordException = true;
            })
            // Add other sources, resource configurations, and exporters (e.g., Console, OTLP)
            .AddConsoleExporter()
            .Build();
    }
}
```

### 2. Instrument your DbConnection

Wherever you create a `DbConnection`, wrap it using `AdoNetInstrumentation.InstrumentConnection()`.

```csharp
using System.Data.Common;
using Microsoft.Data.Sqlite; // Example: using Sqlite
using OpenTelemetry.Instrumentation.AdoNet; // Required for InstrumentConnection

// ...

public class MyDataAccessClass
{
    private readonly string _connectionString;

    public MyDataAccessClass(string connectionString)
    {
        _connectionString = connectionString;
    }

    public void CreateTableAndInsertData()
    {
        // Create your original connection
        using var originalConnection = new SqliteConnection(_connectionString);

        // Instrument the connection
        using var instrumentedConnection = AdoNetInstrumentation.InstrumentConnection(originalConnection);
        // Or, if you configured options via AddAdoNetInstrumentation, this is enough:
        // using var instrumentedConnection = AdoNetInstrumentation.InstrumentConnection(originalConnection);
        // Or, provide specific options here:
        // using var instrumentedConnection = AdoNetInstrumentation.InstrumentConnection(originalConnection, new AdoNetInstrumentationOptions { DbSystem = "custom_sqlite" });


        instrumentedConnection.Open();

        using var command = instrumentedConnection.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS MyTable (Id INTEGER PRIMARY KEY, Name TEXT);";
        command.ExecuteNonQuery();

        command.CommandText = "INSERT INTO MyTable (Name) VALUES ('Test Name');";
        command.ExecuteNonQuery();
    }

    public string? ReadData()
    {
        using var originalConnection = new SqliteConnection(_connectionString);
        using var instrumentedConnection = AdoNetInstrumentation.InstrumentConnection(originalConnection);

        instrumentedConnection.Open();
        using var command = instrumentedConnection.CreateCommand();
        command.CommandText = "SELECT Name FROM MyTable WHERE Id = 1;";

        using var reader = command.ExecuteReader();
        if (reader.Read())
        {
            return reader.GetString(0);
        }
        return null;
    }
}
```

### 3. Integrating with Dependency Injection

If you are using a dependency injection (DI) container, you can register your `DbConnection` in such a way that it's automatically instrumented when resolved. Here's an example using `Microsoft.Extensions.DependencyInjection`:

```csharp
// In your Program.cs or Startup.cs

// using Microsoft.Extensions.DependencyInjection;
// using Microsoft.Extensions.Configuration; // For IConfiguration
// using System.Data.Common;
// using Microsoft.Data.Sqlite; // Example provider
// using OpenTelemetry.Instrumentation.AdoNet;

// ...

// public void ConfigureServices(IServiceCollection services) // Or similar method
// {
//     // Assuming you have IConfiguration available for connection strings
//     var configuration = services.BuildServiceProvider().GetRequiredService<IConfiguration>();
//     var connectionString = configuration.GetConnectionString("DefaultConnection");

//     // Register your DbConnection (e.g., SqliteConnection)
//     services.AddScoped<DbConnection>(sp =>
//     {
//         var originalConnection = new SqliteConnection(connectionString);

//         // Get options configured via AddAdoNetInstrumentation, or create new ones.
//         // Note: AdoNetInstrumentation.DefaultOptions is internal.
//         // For a cleaner approach with DI-configured options, future enhancements to the library might be needed
//         // to expose options more directly for this scenario.
//         // For now, you can pass new options or rely on options set via AddAdoNetInstrumentation if they are globally available
//         // (though direct access to AdoNetInstrumentation.DefaultOptions isn't public).
//         // The simplest is to let AddAdoNetInstrumentation handle global defaults.
//         // If AddAdoNetInstrumentation was called, its configured options (if any) will be used by default.

//         var instrumentedConnection = AdoNetInstrumentation.InstrumentConnection(originalConnection /*, pass explicit options here if needed */);

//         // It's important that the DI container does NOT dispose the instrumentedConnection
//         // if the underlying originalConnection is also managed/disposed by the DI container elsewhere
//         // or if the scope of originalConnection is meant to be different.
//         // Typically, for DbConnection, you resolve it, use it, and dispose it within a short scope.
//         // The example above assumes the DbConnection is resolved and used per-scope (e.g., per HTTP request).
//         // The `using var instrumentedConnection = ...` pattern shown in earlier examples is often within a method scope.

//         return instrumentedConnection;
//     });

//     // You could also register a specific type like SqliteConnection:
//     // services.AddScoped<SqliteConnection>(sp =>
//     // {
//     //     var connectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection");
//     //     var originalConnection = new SqliteConnection(connectionString);
//     //     // AdoNetInstrumentation.InstrumentConnection returns DbConnection, so cast if needed,
//     //     // or ensure your InstrumentedDbConnection can be cast or is derived appropriately if you need the specific type.
//     //     // However, AdoNetInstrumentation.InstrumentConnection returns the base DbConnection type.
//     //     // So, for specific types, you might need a slightly different approach or accept DbConnection.
//     //     return (SqliteConnection)AdoNetInstrumentation.InstrumentConnection(originalConnection);
//     // });
//     // For the above SqliteConnection example, it's better to register as DbConnection
//     // and resolve DbConnection in your services, as InstrumentConnection returns a DbConnection.

//     // Your other services...
// }
```

When your services resolve `DbConnection`, they will receive an instrumented version if it was configured as above.

**Important Considerations for DI:**
*   **Connection Lifetime:** Be mindful of the lifetime of your `DbConnection` in the DI container (`Scoped`, `Transient`, `Singleton`). `Scoped` (e.g., per HTTP request) is common for database connections. The `InstrumentedDbConnection` will wrap the original connection; ensure their lifetimes are managed correctly. The instrumented connection should generally have the same lifetime as the original connection it wraps.
*   **Options with DI:** If you configure `AdoNetInstrumentationOptions` via `AddAdoNetInstrumentation()`, those will be the default options used by `InstrumentConnection()` if no options are explicitly passed to it. If you need different options for connections resolved via DI, you can pass an `AdoNetInstrumentationOptions` instance directly to `InstrumentConnection` within your DI setup code.
*   **Disposal:** The `InstrumentedDbConnection` disposes the wrapped `DbConnection` when it itself is disposed. Ensure your DI container's disposal behavior aligns with this. If the DI container disposes the `DbConnection`, the instrumented wrapper will handle disposing the underlying connection.

This example shows one way to integrate. Depending on your DI framework and patterns, other approaches might also be suitable. The key is that `AdoNetInstrumentation.InstrumentConnection()` is called with the `DbConnection` instance you want to instrument.

## Configuration Options (`AdoNetInstrumentationOptions`)

You can configure the instrumentation behavior using `AdoNetInstrumentationOptions`. These options can be set when calling `AddAdoNetInstrumentation()` on the `TracerProviderBuilder` or directly when calling `AdoNetInstrumentation.InstrumentConnection()`. Options passed directly to `InstrumentConnection` take precedence.

*   **`DbSystem`**: `string?` (Default: auto-detected based on connection type, e.g., "mssql", "postgresql", "sqlite")
    *   Allows you to explicitly set the value for the `db.system` semantic tag. Useful if auto-detection is not accurate for your provider.
    *   Example: `options.DbSystem = "custom_db";`

*   **`SetDbStatementForText`**: `bool` (Default: `true`)
    *   If `true`, the `DbCommand.CommandText` is captured in the `db.statement` tag for commands with `CommandType.Text` or `CommandType.StoredProcedure`.
    *   Example: `options.SetDbStatementForText = false;`

*   **`RecordException`**: `bool` (Default: `false`)
    *   If `true`, `DbException`s encountered during command execution will be recorded as an event on the activity, including exception type, message, and stack trace. The activity status will always be set to `Error` regardless of this option if an exception occurs.
    *   Example: `options.RecordException = true;`

*   **`Filter`**: `Func<DbCommand, bool>?` (Default: `null`, all commands are instrumented)
    *   A predicate that is called before a command is instrumented. If the predicate returns `false`, the command will not be instrumented (no activity will be created).
    *   Example: `options.Filter = (command) => !command.CommandText.StartsWith("SELECT * FROM SensitiveTable");`

*   **`Enrich`**: `Action<Activity, DbCommand>?` (Default: `null`)
    *   An action that is called after an activity for a command has been created and basic tags have been added, but before the command is executed. This allows you to add custom tags to the activity.
    *   Example:
        ```csharp
        options.Enrich = (activity, command) =>
        {
            activity.SetTag("my.custom.tag", "important_value");
            if (command.Parameters.Contains("@UserId"))
            {
                activity.SetTag("user.id.from.param", command.Parameters["@UserId"].Value?.ToString());
            }
        };
        ```

## Metrics

In addition to traces, this instrumentation can also collect metrics about ADO.NET client operations.

### Enabling Metrics

To enable metrics collection, you need to:

1.  **Configure `AdoNetInstrumentationOptions`**: Ensure the `EmitMetrics` option is set to `true` (which is the default). You can configure this when calling `AddAdoNetInstrumentation()` for your `TracerProviderBuilder`:
    ```csharp
    // In your TracerProvider setup
    .AddAdoNetInstrumentation(options =>
    {
        options.EmitMetrics = true; // Default is true, but can be explicitly set
        // ... other trace options
    })
    ```

2.  **Add ADO.NET Instrumentation to `MeterProviderBuilder`**:
    In your `MeterProvider` setup, call the `AddAdoNetInstrumentationMetrics()` extension method:
    ```csharp
    // In your MeterProvider setup
    Sdk.CreateMeterProviderBuilder()
        .AddAdoNetInstrumentationMetrics()
        // Add other meters and exporters (e.g., Console, OTLP)
        .AddConsoleExporter()
        .Build();
    ```

### Collected Metrics

The following metrics are collected if `EmitMetrics` is enabled:

*   **`db.client.duration`** (Histogram, Unit: `ms`)
    *   Description: Measures the duration of database client operations.
    *   Tags/Attributes:
        *   `db.system`: (e.g., `sqlite`, `mssql`)
        *   `db.name`: Name of the database.
        *   `server.address`: Network address of the database server.
        *   `db.operation`: The name of the ADO.NET operation (e.g., `ExecuteNonQuery`, `ExecuteReader`).
        *   `error.type`: (Only if an error occurred) The type name of the exception (e.g., `SqliteException`).

*   **`db.client.calls`** (Counter, Unit: `{call}`)
    *   Description: Counts the number of database client calls.
    *   Tags/Attributes: (Same as `db.client.duration`)
        *   `db.system`
        *   `db.name`
        *   `server.address`
        *   `db.operation`
        *   `error.type` (Only if an error occurred)

## Captured Trace Attributes

This instrumentation aims to capture the following semantic conventions for database client spans:

*   **`db.system`**: An identifier for the database management system (DBMS) being used (e.g., `mssql`, `postgresql`, `mysql`, `sqlite`).
*   **`db.name`**: The name of the database being accessed.
*   **`db.statement`**: The database statement being executed (if `SetDbStatementForText` is true). For `CommandType.StoredProcedure`, this will be the name of the stored procedure.
*   **`db.operation`**: The name of the operation being performed (e.g., `ExecuteNonQuery`, `ExecuteReader`, `ExecuteScalar`).
*   **`net.peer.name`** / **`server.address`**: The hostname or network address of the database server (taken from `DbConnection.DataSource`).
*   **`server.port`**: (Future consideration, if reliably parsable from DataSource) The port number of the database server.
*   **Activity Status**: `Ok` on success, `Error` on exception.
*   **Exception information**: If `RecordException` is true, exception details are added as an activity event.

## Troubleshooting

*   **No telemetry is captured**:
    *   Ensure you have registered `AddAdoNetInstrumentation()` with your `TracerProviderBuilder`.
    *   Verify that you are wrapping your `DbConnection` instances with `AdoNetInstrumentation.InstrumentConnection()`.
    *   Check if your `Filter` option might be excluding the commands you expect to see.
    *   Confirm your OpenTelemetry SDK is correctly configured with an exporter and is processing telemetry.
```
