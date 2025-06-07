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

## Manual Instrumentation Setup

The ADO.NET instrumentation works by wrapping your existing `DbConnection` objects. You need to explicitly instrument your connections using the `AdoNetInstrumentation.InstrumentConnection` method.

### 1. Enable ADO.NET Instrumentation in your TracerProvider and MeterProvider

First, you need to add ADO.NET instrumentation to your `TracerProvider` and `MeterProvider` configurations.

```csharp
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics; // For MeterProvider
using System.Data.Common; // For DbConnection
using Microsoft.Data.Sqlite; // Example provider

// ...

public static class TelemetryConfig
{
    public static TracerProvider? MyTracerProvider;
    public static MeterProvider? MyMeterProvider;

    public static void Initialize()
    {
        // Configure options once, can be shared or customized per use case
        Action<AdoNetInstrumentationOptions> configureAdoNetOptions = options =>
        {
            options.SetDbStatementForText = true;
            options.RecordException = true;
            options.EmitMetrics = true; // Ensure metrics are enabled
        };

        MyTracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddSource("MyApplicationActivitySource") // Your application's activity source
            .AddAdoNetInstrumentation(configureAdoNetOptions) // Configure default options for traces & metrics
            // Add other sources, resource configurations, and exporters (e.g., Console, OTLP)
            .AddConsoleExporter()
            .Build();

        MyMeterProvider = Sdk.CreateMeterProviderBuilder()
            .AddAdoNetInstrumentationMetrics() // Enable the ADO.NET meter
            // Add other meters and exporters
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

        // Instrument the connection.
        // If AddAdoNetInstrumentation was called with options, those are used by default.
        using var instrumentedConnection = AdoNetInstrumentation.InstrumentConnection(originalConnection);
        // Or, provide specific options here to override defaults for this instance:
        // using var instrumentedConnection = AdoNetInstrumentation.InstrumentConnection(originalConnection,
        //     new AdoNetInstrumentationOptions { DbSystem = "custom_sqlite", EmitMetrics = false });


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

## Automatic Instrumentation Setup (DI and DbProviderFactories)

While connections can always be manually instrumented using `AdoNetInstrumentation.InstrumentConnection()`, this library also provides helpers for more integrated setup, especially when using Dependency Injection (DI) or `DbProviderFactories`.

### Using with Dependency Injection (`Microsoft.Extensions.DependencyInjection`)

If you are using `Microsoft.Extensions.DependencyInjection`, you can register instrumented ADO.NET components.

**1. Configure ADO.NET Instrumentation Options:**
First, configure the default or named options for ADO.NET instrumentation in your `IServiceCollection`.

```csharp
// In Program.cs or Startup.cs
// using Microsoft.Extensions.DependencyInjection;
// using OpenTelemetry.Instrumentation.AdoNet;

services.ConfigureAdoNetInstrumentation(options =>
{
    options.SetDbStatementForText = true;
    options.RecordException = true;
    options.EmitMetrics = true;
    // options.DbSystem = "your_db_system"; // Optionally override db.system
});

// For named options:
// services.ConfigureAdoNetInstrumentation("MySpecificDatabase", options => { /* ... */ });
```

**2. Register Instrumented Components:**

*   **Instrumented `DbConnection`:**
    Register a factory delegate that provides an instrumented `DbConnection`. This is useful when you resolve `DbConnection` directly.

    ```csharp
    // using System.Data.Common;
    // using Microsoft.Data.Sqlite;
    // using Microsoft.Extensions.Options; // For Options.DefaultName

    services.AddInstrumentedDbConnection(
        sp => new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:"), // Your factory for the original connection
        optionsName: Microsoft.Extensions.Options.Options.DefaultName, // Or your named options instance
        lifetime: ServiceLifetime.Scoped // Typical lifetime for DbConnection
    );

    // Example usage in a service:
    // public class MyService(DbConnection connection) { ... }
    ```

*   **Instrumented `DbProviderFactory`:**
    Register an instrumented version of a `DbProviderFactory` for a specific provider invariant name.

    ```csharp
    // using System.Data.Common;
    // using Microsoft.Extensions.Options; // For Options.DefaultName

    // Ensure the underlying provider factory is registered if needed by your application or DbProviderFactories.
    // For some providers like Microsoft.Data.Sqlite, SqliteFactory.Instance can be used.
    // try { DbProviderFactories.RegisterFactory("Microsoft.Data.Sqlite", Microsoft.Data.Sqlite.SqliteFactory.Instance); } catch (ArgumentException) { /* May already be registered */ }


    services.AddInstrumentedDbProviderFactory(
        "Microsoft.Data.Sqlite", // Provider Invariant Name
        optionsName: Microsoft.Extensions.Options.Options.DefaultName, // Or your named options
        lifetime: ServiceLifetime.Singleton // Typical lifetime for DbProviderFactory
    );

    // Example usage in a service:
    // public class MyFactoryUserService(DbProviderFactory factory)
    // {
    //     public void DoWork()
    //     {
    //         using var connection = factory.CreateConnection(); // This will be an instrumented connection
    //         connection.ConnectionString = "Data Source=:memory:";
    //         // ...
    //     }
    // }
    ```

**3. Ensure OpenTelemetry is Configured:**
Remember to also add the ADO.NET instrumentation to your OpenTelemetry `TracerProvider` and `MeterProvider` setup:

```csharp
// using OpenTelemetry.Trace;
// using OpenTelemetry.Metrics;

services.AddOpenTelemetry()
    .WithTracing(builder => builder
        .AddAdoNetInstrumentation() // This adds the ActivitySource. Options are picked from DI by instrumented components.
        // ... other trace configurations ...
    )
    .WithMetrics(builder => builder
        .AddAdoNetInstrumentationMetrics() // This adds the Meter.
        // ... other metric configurations ...
    );
```
The DI-registered factories for `DbConnection` and `DbProviderFactory` will resolve the configured `AdoNetInstrumentationOptions` from the DI container and pass them to `AdoNetInstrumentation.InstrumentConnection()` or `InstrumentedDbProviderFactory` respectively. The `AddAdoNetInstrumentation()` (for tracing) and `AddAdoNetInstrumentationMetrics()` builder extensions primarily ensure the ActivitySource and Meter are enabled in the OpenTelemetry SDK.

### Using with `DbProviderFactories` (Manual Wrapping)

If your application retrieves `DbProviderFactory` instances directly using `DbProviderFactories.GetFactory(providerInvariantName)`, you can instrument these by wrapping the factory:

1.  **Get the original factory.**
2.  **Create an `AdoNetInstrumentationOptions` instance** (or retrieve one configured via DI if `IServiceProvider` is accessible, or use options configured via `AddAdoNetInstrumentation` on `TracerProviderBuilder` which sets `AdoNetInstrumentation.DefaultOptions`).
3.  **Create an `InstrumentedDbProviderFactory`**.

```csharp
using System.Data.Common;
using OpenTelemetry.Instrumentation.AdoNet;

// ...

string providerName = "Microsoft.Data.Sqlite"; // Or from config
DbProviderFactory originalFactory = DbProviderFactories.GetFactory(providerName);

// Configure options (e.g., programmatically or from IOptions if in DI context)
var adoNetOptions = new AdoNetInstrumentationOptions
{
    SetDbStatementForText = true,
    EmitMetrics = true
    // DbSystem can be set here to override auto-detection if needed
};
// Alternatively, if AddAdoNetInstrumentation(options => ...) was called for TracerProviderBuilder:
// var adoNetOptions = AdoNetInstrumentation.DefaultOptions ?? new AdoNetInstrumentationOptions();


DbProviderFactory instrumentedFactory = new InstrumentedDbProviderFactory(originalFactory, adoNetOptions);

// Now use instrumentedFactory to create connections
using (DbConnection connection = instrumentedFactory.CreateConnection())
{
    // This connection will be instrumented
    if (connection != null)
    {
        connection.ConnectionString = "Data Source=:memory:";
        // ...
    }
}
```
This approach still requires you to manage the `InstrumentedDbProviderFactory` instance. The DI approach above is generally preferred for applications already using DI.

## Configuration Options (`AdoNetInstrumentationOptions`)

You can configure the instrumentation behavior using `AdoNetInstrumentationOptions`. These options can be set when calling `AddAdoNetInstrumentation()` on the `TracerProviderBuilder` (which sets global `AdoNetInstrumentation.DefaultOptions`), via DI using `ConfigureAdoNetInstrumentation()`, or directly when calling `AdoNetInstrumentation.InstrumentConnection()`. Options passed directly to `InstrumentConnection` take precedence over DI-resolved options for that specific instance, which in turn take precedence over `AdoNetInstrumentation.DefaultOptions`.

*   **`DbSystem`**: `string?` (Default: auto-detected)
    *   Allows you to explicitly set the value for the `db.system` semantic tag.
    *   If not set, the instrumentation attempts to automatically determine the `db.system` value based on the `DbConnection` type. Recognized systems include:
        *   `mssql` (for System.Data.SqlClient and Microsoft.Data.SqlClient)
        *   `postgresql` (for Npgsql)
        *   `mysql` (for MySql.Data and MySqlConnector)
        *   `oracle` (for Oracle.ManagedDataAccess.Client)
        *   `sqlite` (for Microsoft.Data.Sqlite and System.Data.SQLite)
        *   `db2` (for IBM.Data.DB2 drivers)
        *   `firebird` (for FirebirdSql.Data.FirebirdClient)
    *   If the provider is not recognized, a heuristic based on the connection type name is used. If still unresolved, it defaults to `"other"`.
    *   Setting this property provides an explicit value, overriding any auto-detection.
    *   Example: `options.DbSystem = "custom_db";`

*   **`SetDbStatementForText`**: `bool` (Default: `true`)
    *   If `true`, the `DbCommand.CommandText` is captured in the `db.statement` tag for commands with `CommandType.Text` or `CommandType.StoredProcedure`.
    *   Example: `options.SetDbStatementForText = false;`

*   **`RecordException`**: `bool` (Default: `false`)
    *   If `true`, `DbException`s encountered during command execution will be recorded as an event on the activity, including exception type, message, and stack trace. The activity status will always be set to `Error` regardless of this option if an exception occurs.
    *   Example: `options.RecordException = true;`

*   **`EmitMetrics`**: `bool` (Default: `true`)
    *   Gets or sets a value indicating whether ADO.NET client metrics should be collected.
    *   Example: `options.EmitMetrics = false;`

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

1.  **Configure `AdoNetInstrumentationOptions`**: Ensure the `EmitMetrics` option is set to `true` (which is the default). You can configure this when calling `AddAdoNetInstrumentation()` for your `TracerProviderBuilder` or when using DI:
    ```csharp
    // Example with TracerProviderBuilder
    .AddAdoNetInstrumentation(options =>
    {
        options.EmitMetrics = true; // Default is true, but can be explicitly set
        // ... other trace options
    })

    // Example with DI (IServiceCollection)
    // services.ConfigureAdoNetInstrumentation(options => options.EmitMetrics = true);
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
        *   `db.system`: Auto-detected or user-configured identifier for the DBMS (see `DbSystem` option for details).
        *   `db.name`: Name of the database.
        *   `server.address`: Network address of the database server.
        *   `db.operation`: The name of the ADO.NET operation (e.g., `ExecuteNonQuery`, `ExecuteReader`).
        *   `error.type`: (Only if an error occurred) The type name of the exception (e.g., `SqliteException`).

*   **`db.client.calls`** (Counter, Unit: `{call}`)
    *   Description: Counts the number of database client calls.
    *   Tags/Attributes: (Same as `db.client.duration`)
        *   `db.system`: Auto-detected or user-configured identifier for the DBMS (see `DbSystem` option for details).
        *   `db.name`: Name of the database.
        *   `server.address`: Network address of the database server.
        *   `db.operation`: The name of the ADO.NET operation (e.g., `ExecuteNonQuery`, `ExecuteReader`).
        *   `error.type`: (Only if an error occurred) The type name of the exception (e.g., `SqliteException`).

## Captured Trace Attributes

This instrumentation aims to capture the following semantic conventions for database client spans:

*   **`db.system`**: An identifier for the database management system (DBMS) being used. The value is auto-detected for common providers (e.g., `mssql`, `postgresql`, `mysql`, `oracle`, `sqlite`, `db2`, `firebird`) and can be overridden using the `AdoNetInstrumentationOptions.DbSystem` setting. See the `DbSystem` option description for more details on recognized providers.
*   **`db.name`**: The name of the database being accessed.
*   **`db.statement`**: The database statement being executed (if `SetDbStatementForText` is true). For `CommandType.StoredProcedure`, this will be the name of the stored procedure.
*   **`db.operation`**: The name of the operation being performed (e.g., `ExecuteNonQuery`, `ExecuteReader`, `ExecuteScalar`).
*   **`net.peer.name`** / **`server.address`**: The hostname or network address of the database server (taken from `DbConnection.DataSource`).
*   **`server.port`**: (Future consideration, if reliably parsable from DataSource) The port number of the database server.
*   **Activity Status**: `Ok` on success, `Error` on exception.
*   **Exception information**: If `RecordException` is true, exception details are added as an activity event.

## Troubleshooting

*   **No telemetry is captured**:
    *   Ensure you have registered `AddAdoNetInstrumentation()` with your `TracerProviderBuilder` (for traces) and `AddAdoNetInstrumentationMetrics()` with your `MeterProviderBuilder` (for metrics).
    *   Verify that you are wrapping your `DbConnection` instances with `AdoNetInstrumentation.InstrumentConnection()` or using the DI registration helpers.
    *   Check if your `Filter` option might be excluding the commands you expect to see.
    *   Confirm your OpenTelemetry SDK is correctly configured with an exporter and is processing telemetry.
    *   If using DI, ensure `AdoNetInstrumentationOptions` (especially `EmitMetrics`) are correctly configured and resolved.
```
