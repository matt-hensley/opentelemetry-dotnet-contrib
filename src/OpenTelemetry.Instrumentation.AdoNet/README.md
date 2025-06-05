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

## Captured Telemetry

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
