// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System.Data;
using System.Data.Common;
using System.Diagnostics;
using Microsoft.Data.Sqlite;
using OpenTelemetry.Trace;
using Xunit;

namespace OpenTelemetry.Instrumentation.AdoNet.Tests
{
using System.Linq;
using System.Threading.Tasks;

namespace OpenTelemetry.Instrumentation.AdoNet.Tests
{
    public class AdoNetInstrumentationTests : IDisposable
    {
        private readonly SqliteConnection inMemorySqliteConnection;
        private TracerProvider? tracerProvider;
        private List<Activity>? exportedActivities;

        public AdoNetInstrumentationTests()
        {
            this.inMemorySqliteConnection = new SqliteConnection("Data Source=:memory:");
            this.inMemorySqliteConnection.Open(); // Open the connection for the test duration
        }

        public void Dispose()
        {
            this.inMemorySqliteConnection.Close();
            this.inMemorySqliteConnection.Dispose();
            this.tracerProvider?.Dispose();
            GC.SuppressFinalize(this);
        }

        private void SetupTracerProvider(Action<AdoNetInstrumentationOptions>? configureOptions = null)
        {
            this.exportedActivities = new List<Activity>();
            this.tracerProvider = Sdk.CreateTracerProviderBuilder()
                .AddAdoNetInstrumentation(configureOptions)
                .AddInMemoryExporter(this.exportedActivities)
                .Build();
        }

        [Fact]
        public void ExecuteNonQuery_CreatesActivity()
        {
            // Arrange
            SetupTracerProvider();
            using var instrumentedConnection = AdoNetInstrumentation.InstrumentConnection(this.inMemorySqliteConnection);
            using var command = instrumentedConnection.CreateCommand();
            command.CommandText = "CREATE TABLE IF NOT EXISTS TestTable (Id INTEGER PRIMARY KEY);";

            // Act
            command.ExecuteNonQuery();

            // Assert
            Assert.NotNull(this.exportedActivities);
            Assert.Single(this.exportedActivities);
            var activity = this.exportedActivities[0];

            Assert.Equal($"{this.inMemorySqliteConnection.Database}.{nameof(DbCommand.ExecuteNonQuery)}", activity.DisplayName);
            Assert.Equal(ActivityKind.Client, activity.Kind);
            Assert.Equal("sqlite", activity.GetTagItem(SemanticConventions.AttributeDbSystem));
            Assert.Equal(this.inMemorySqliteConnection.Database, activity.GetTagItem(SemanticConventions.AttributeDbName));
            Assert.Equal(this.inMemorySqliteConnection.DataSource, activity.GetTagItem(SemanticConventions.AttributeNetPeerName));
            Assert.Equal(command.CommandText, activity.GetTagItem(SemanticConventions.AttributeDbStatement));
            Assert.Equal(nameof(DbCommand.ExecuteNonQuery), activity.GetTagItem(SemanticConventions.AttributeDbOperation));
            Assert.Equal(ActivityStatusCode.Ok, activity.Status);
        }

        [Fact]
        public void ExecuteScalar_CreatesActivity()
        {
            // Arrange
            SetupTracerProvider();
            using var instrumentedConnection = AdoNetInstrumentation.InstrumentConnection(this.inMemorySqliteConnection);
            // Ensure table exists
            using (var setupCmd = instrumentedConnection.CreateCommand())
            {
                setupCmd.CommandText = "CREATE TABLE IF NOT EXISTS ScalarTest (Value INT); INSERT INTO ScalarTest (Value) VALUES (123);";
                setupCmd.ExecuteNonQuery();
            }
            this.exportedActivities?.Clear(); // Clear setup activity

            using var command = instrumentedConnection.CreateCommand();
            command.CommandText = "SELECT Value FROM ScalarTest WHERE Value = 123;";

            // Act
            var result = command.ExecuteScalar();

            // Assert
            Assert.Equal(123L, result); // Sqlite returns Int64 for INTEGER
            Assert.NotNull(this.exportedActivities);
            Assert.Single(this.exportedActivities);
            var activity = this.exportedActivities[0];

            Assert.Equal($"{this.inMemorySqliteConnection.Database}.{nameof(DbCommand.ExecuteScalar)}", activity.DisplayName);
            Assert.Equal("sqlite", activity.GetTagItem(SemanticConventions.AttributeDbSystem));
            Assert.Equal(command.CommandText, activity.GetTagItem(SemanticConventions.AttributeDbStatement));
            Assert.Equal(nameof(DbCommand.ExecuteScalar), activity.GetTagItem(SemanticConventions.AttributeDbOperation));
            Assert.Equal(ActivityStatusCode.Ok, activity.Status);
        }

        [Fact]
        public void ExecuteReader_CreatesActivity_AndStopsOnReaderClose()
        {
            // Arrange
            SetupTracerProvider();
            using var instrumentedConnection = AdoNetInstrumentation.InstrumentConnection(this.inMemorySqliteConnection);
            using (var setupCmd = instrumentedConnection.CreateCommand()) // Ensure table and data
            {
                setupCmd.CommandText = "CREATE TABLE IF NOT EXISTS ReaderTest (Id INT); INSERT INTO ReaderTest (Id) VALUES (1);";
                setupCmd.ExecuteNonQuery();
            }
            this.exportedActivities?.Clear();

            using var command = instrumentedConnection.CreateCommand();
            command.CommandText = "SELECT Id FROM ReaderTest;";

            Activity? readerActivity = null;

            // Act
            using (var reader = command.ExecuteReader())
            {
                Assert.True(reader.Read());
                Assert.Equal(1, reader.GetInt32(0));

                // Activity should not be stopped yet

                // Activity is started but not yet exported to InMemoryExporter
                Assert.Empty(this.exportedActivities!);
            } // Reader is closed/disposed here, activity should be stopped and exported

            // Assert
            Assert.NotNull(this.exportedActivities);
            Assert.Single(this.exportedActivities);
            var activity = this.exportedActivities[0];

            Assert.Equal($"{this.inMemorySqliteConnection.Database}.{nameof(DbCommand.ExecuteReader)}", activity.DisplayName);
            Assert.Equal("sqlite", activity.GetTagItem(SemanticConventions.AttributeDbSystem));
            Assert.Equal(command.CommandText, activity.GetTagItem(SemanticConventions.AttributeDbStatement));
            Assert.Equal(nameof(DbCommand.ExecuteReader), activity.GetTagItem(SemanticConventions.AttributeDbOperation));
            Assert.Equal(ActivityStatusCode.Ok, activity.Status);
            Assert.True(activity.Duration > TimeSpan.Zero);
        }

            [Fact]
            public void ExecuteNonQuery_WhenExceptionOccurs_ActivityRecordsException()
            {
                // Arrange
                SetupTracerProvider(options => options.RecordException = true); // Enable exception recording
                using var instrumentedConnection = AdoNetInstrumentation.InstrumentConnection(this.inMemorySqliteConnection);
                using var command = instrumentedConnection.CreateCommand();
                command.CommandText = "INSERT INTO NonExistentTable (Id) VALUES (1);"; // This will fail

                // Act & Assert
                Assert.Throws<SqliteException>(() => command.ExecuteNonQuery());

                Assert.NotNull(this.exportedActivities);
                Assert.Single(this.exportedActivities);
                var activity = this.exportedActivities[0];

                Assert.Equal(ActivityStatusCode.Error, activity.Status);
                Assert.Contains("SQLite Error 1: 'no such table: NonExistentTable'", activity.StatusDescription);

                // Check for exception event
                var exceptionEvent = activity.Events.FirstOrDefault(e => e.Name == "exception");
                Assert.NotNull(exceptionEvent);
                Assert.Contains(exceptionEvent.Tags, kvp => kvp.Key == SemanticConventions.AttributeExceptionType && kvp.Value.Contains("Microsoft.Data.Sqlite.SqliteException"));
                Assert.Contains(exceptionEvent.Tags, kvp => kvp.Key == SemanticConventions.AttributeExceptionMessage && kvp.Value.Contains("no such table: NonExistentTable"));
            }

            [Fact]
            public void CommandTypeStoredProcedure_CreatesActivityWithCommandTextAsStatement()
            {
                // Arrange
                SetupTracerProvider();
                using var instrumentedConnection = AdoNetInstrumentation.InstrumentConnection(this.inMemorySqliteConnection);
                using var command = instrumentedConnection.CreateCommand();
                // Sqlite doesn't really have stored procedures in the same way as SQL Server.
                // We're testing that CommandText is used as db.statement when CommandType is StoredProcedure.
                command.CommandText = "MyTestProcedure";
                command.CommandType = CommandType.StoredProcedure;

                // Act
                // ExecuteNonQuery will likely fail if the "procedure" doesn't map to a valid SQL function or similar in Sqlite,
                // but the activity should still be created with the correct tags before the exception.
                // For this test, we only care about the tags on the activity before execution error.
                try
                {
                    // This will fail with "SQLite Error 1: 'MyTestProcedure': unknown function'" or similar
                    // if MyTestProcedure is not a registered function.
                    // Depending on how Sqlite handles CommandType.StoredProcedure, it might also fail earlier.
                    // We'll catch the expected exception.
                    command.ExecuteNonQuery();
                }
                catch (SqliteException)
                {
                    // Expected if "MyTestProcedure" is not a known function.
                }


                // Assert
                Assert.NotNull(this.exportedActivities);
                Assert.Single(this.exportedActivities);
                var activity = this.exportedActivities[0];

                Assert.Equal("MyTestProcedure", activity.GetTagItem(SemanticConventions.AttributeDbStatement));
                Assert.Equal(nameof(DbCommand.ExecuteNonQuery), activity.GetTagItem(SemanticConventions.AttributeDbOperation)); // or the operation that was called
                 // Status could be Error or Ok depending on how Sqlite handles unknown SPs.
                 // For this test, we primarily care that DbStatement was set from CommandText for SP.
            }

            [Fact]
            public void FilterOption_PreventsActivityCreation()
            {
                // Arrange
                SetupTracerProvider(options =>
                {
                    options.Filter = (cmd) => !cmd.CommandText.Contains("SKIP_THIS");
                });
                using var instrumentedConnection = AdoNetInstrumentation.InstrumentConnection(this.inMemorySqliteConnection);

                // Command that should be filtered out
                using (var commandToSkip = instrumentedConnection.CreateCommand())
                {
                    commandToSkip.CommandText = "SELECT 'SKIP_THIS';";
                    commandToSkip.ExecuteScalar();
                }

                // Command that should be instrumented
                using (var commandToInstrument = instrumentedConnection.CreateCommand())
                {
                    commandToInstrument.CommandText = "SELECT 'INSTRUMENT_THIS';";
                    commandToInstrument.ExecuteScalar();
                }

                // Assert
                Assert.NotNull(this.exportedActivities);
                Assert.Single(this.exportedActivities); // Only one activity should be present
                var activity = this.exportedActivities[0];
                Assert.Equal("SELECT 'INSTRUMENT_THIS';", activity.GetTagItem(SemanticConventions.AttributeDbStatement));
            }

            [Fact]
            public void EnrichOption_AddsCustomTags()
            {
                // Arrange
                var customTagName = "my.custom.tag";
                var customTagValue = "custom.value";
                SetupTracerProvider(options =>
                {
                    options.Enrich = (activity, command) =>
                    {
                        activity.SetTag(customTagName, customTagValue);
                        activity.SetTag("db.command.type", command.CommandType.ToString());
                    };
                });

                using var instrumentedConnection = AdoNetInstrumentation.InstrumentConnection(this.inMemorySqliteConnection);
                using var command = instrumentedConnection.CreateCommand();
                command.CommandText = "SELECT 1;";
                command.CommandType = CommandType.Text; // Explicitly set for the test

                // Act
                command.ExecuteScalar();

                // Assert
                Assert.NotNull(this.exportedActivities);
                Assert.Single(this.exportedActivities);
                var activity = this.exportedActivities[0];
                Assert.Equal(customTagValue, activity.GetTagItem(customTagName));
                Assert.Equal(CommandType.Text.ToString(), activity.GetTagItem("db.command.type"));
            }

            [Fact]
            public void DbSystemOption_OverridesDefault()
            {
                // Arrange
                var overrideDbSystem = "testdb";
                SetupTracerProvider(options => options.DbSystem = overrideDbSystem);
                using var instrumentedConnection = AdoNetInstrumentation.InstrumentConnection(this.inMemorySqliteConnection);
                using var command = instrumentedConnection.CreateCommand();
                command.CommandText = "SELECT 1;";

                // Act
                command.ExecuteScalar();

                // Assert
                Assert.NotNull(this.exportedActivities);
                Assert.Single(this.exportedActivities);
                var activity = this.exportedActivities[0];
                Assert.Equal(overrideDbSystem, activity.GetTagItem(SemanticConventions.AttributeDbSystem));
            }

            [Fact]
            public async Task ExecuteNonQueryAsync_CreatesActivity()
            {
                // Arrange
                SetupTracerProvider();
                using var instrumentedConnection = AdoNetInstrumentation.InstrumentConnection(this.inMemorySqliteConnection);
                using var command = instrumentedConnection.CreateCommand();
                command.CommandText = "CREATE TABLE IF NOT EXISTS AsyncTestTable (Id INTEGER PRIMARY KEY);";

                // Act
                await command.ExecuteNonQueryAsync();

                // Assert
                Assert.NotNull(this.exportedActivities);
                Assert.Single(this.exportedActivities);
                var activity = this.exportedActivities[0];

                Assert.Equal($"{this.inMemorySqliteConnection.Database}.{nameof(DbCommand.ExecuteNonQueryAsync)}", activity.DisplayName);
                Assert.Equal(ActivityKind.Client, activity.Kind);
                Assert.Equal("sqlite", activity.GetTagItem(SemanticConventions.AttributeDbSystem));
                Assert.Equal(command.CommandText, activity.GetTagItem(SemanticConventions.AttributeDbStatement));
                Assert.Equal(nameof(DbCommand.ExecuteNonQueryAsync), activity.GetTagItem(SemanticConventions.AttributeDbOperation));
                Assert.Equal(ActivityStatusCode.Ok, activity.Status);
            }

            [Fact]
            public async Task ExecuteReaderAsync_CreatesActivity_AndStopsOnReaderClose()
            {
                // Arrange
                SetupTracerProvider();
                using var instrumentedConnection = AdoNetInstrumentation.InstrumentConnection(this.inMemorySqliteConnection);
                using (var setupCmd = instrumentedConnection.CreateCommand())
                {
                    setupCmd.CommandText = "CREATE TABLE IF NOT EXISTS AsyncReaderTest (Id INT); INSERT INTO AsyncReaderTest (Id) VALUES (1);";
                    await setupCmd.ExecuteNonQueryAsync();
                }
                this.exportedActivities?.Clear();

                using var command = instrumentedConnection.CreateCommand();
                command.CommandText = "SELECT Id FROM AsyncReaderTest;";

                // Act
                using (var reader = await command.ExecuteReaderAsync())
                {
                    Assert.True(await reader.ReadAsync());
                    Assert.Equal(1, reader.GetInt32(0));
                    // Activity is started but not yet exported to InMemoryExporter
                    Assert.Empty(this.exportedActivities!);
                } // Reader is closed/disposed here

                // Assert
                Assert.NotNull(this.exportedActivities);
                Assert.Single(this.exportedActivities);
                var activity = this.exportedActivities[0];

                Assert.Equal($"{this.inMemorySqliteConnection.Database}.{nameof(DbCommand.ExecuteReaderAsync)}", activity.DisplayName);
                // ... rest of assertions are the same as the synchronous version
                Assert.Equal("sqlite", activity.GetTagItem(SemanticConventions.AttributeDbSystem));
                Assert.Equal(command.CommandText, activity.GetTagItem(SemanticConventions.AttributeDbStatement));
                Assert.Equal(nameof(DbCommand.ExecuteReaderAsync), activity.GetTagItem(SemanticConventions.AttributeDbOperation));
                Assert.Equal(ActivityStatusCode.Ok, activity.Status);
                Assert.True(activity.Duration > TimeSpan.Zero);
            }

            [Fact]
            public async Task ExecuteScalarAsync_CreatesActivity()
            {
                // Arrange
                SetupTracerProvider();
                using var instrumentedConnection = AdoNetInstrumentation.InstrumentConnection(this.inMemorySqliteConnection);
                using (var setupCmd = instrumentedConnection.CreateCommand())
                {
                    setupCmd.CommandText = "CREATE TABLE IF NOT EXISTS AsyncScalarTest (Value INT); INSERT INTO AsyncScalarTest (Value) VALUES (456);";
                    await setupCmd.ExecuteNonQueryAsync();
                }
                this.exportedActivities?.Clear();

                using var command = instrumentedConnection.CreateCommand();
                command.CommandText = "SELECT Value FROM AsyncScalarTest WHERE Value = 456;";

                // Act
                var result = await command.ExecuteScalarAsync();

                // Assert
                Assert.Equal(456L, result); // Sqlite returns Int64
                Assert.NotNull(this.exportedActivities);
                Assert.Single(this.exportedActivities);
                var activity = this.exportedActivities[0];

                Assert.Equal($"{this.inMemorySqliteConnection.Database}.{nameof(DbCommand.ExecuteScalarAsync)}", activity.DisplayName);
                Assert.Equal("sqlite", activity.GetTagItem(SemanticConventions.AttributeDbSystem));
                Assert.Equal(command.CommandText, activity.GetTagItem(SemanticConventions.AttributeDbStatement));
                Assert.Equal(nameof(DbCommand.ExecuteScalarAsync), activity.GetTagItem(SemanticConventions.AttributeDbOperation));
                Assert.Equal(ActivityStatusCode.Ok, activity.Status);
            }

            // TODO: Add test for SetDbStatementForText = false
            // TODO: Add test for RecordException = false (verify no exception event on activity)

            [Fact]
            public void SetDbStatementForText_False_DoesNotSetDbStatementTag()
            {
                // Arrange
                SetupTracerProvider(options => options.SetDbStatementForText = false);
                using var instrumentedConnection = AdoNetInstrumentation.InstrumentConnection(this.inMemorySqliteConnection);
                using var command = instrumentedConnection.CreateCommand();
                command.CommandText = "SELECT 1;";
                command.CommandType = CommandType.Text;

                // Act
                command.ExecuteScalar();

                // Assert
                Assert.NotNull(this.exportedActivities);
                Assert.Single(this.exportedActivities);
                var activity = this.exportedActivities[0];
                Assert.Null(activity.GetTagItem(SemanticConventions.AttributeDbStatement));
                Assert.Equal("sqlite", activity.GetTagItem(SemanticConventions.AttributeDbSystem)); // Other tags should still be present
            }

            [Fact]
            public void RecordException_False_DoesNotRecordExceptionEvent()
            {
                // Arrange
                SetupTracerProvider(options => options.RecordException = false); // Default is false, but explicitly set for clarity
                using var instrumentedConnection = AdoNetInstrumentation.InstrumentConnection(this.inMemorySqliteConnection);
                using var command = instrumentedConnection.CreateCommand();
                command.CommandText = "INSERT INTO NonExistentTable (Id) VALUES (1);"; // This will fail

                // Act & Assert
                Assert.Throws<SqliteException>(() => command.ExecuteNonQuery());

                Assert.NotNull(this.exportedActivities);
                Assert.Single(this.exportedActivities);
                var activity = this.exportedActivities[0];

                Assert.Equal(ActivityStatusCode.Error, activity.Status); // Status should still be Error
                Assert.Contains("SQLite Error 1: 'no such table: NonExistentTable'", activity.StatusDescription);

                // Check that no exception event was recorded
                var exceptionEvent = activity.Events.FirstOrDefault(e => e.Name == "exception");
                Assert.Null(exceptionEvent);
            }

            [Fact]
            public void DefaultOptions_RecordException_False_DoesNotRecordExceptionEvent()
            {
                // Arrange
                // Uses default options where RecordException is false.
                SetupTracerProvider();
                using var instrumentedConnection = AdoNetInstrumentation.InstrumentConnection(this.inMemorySqliteConnection);
                using var command = instrumentedConnection.CreateCommand();
                command.CommandText = "INSERT INTO AnotherNonExistentTable (Id) VALUES (1);";

                // Act & Assert
                Assert.Throws<SqliteException>(() => command.ExecuteNonQuery());

                Assert.NotNull(this.exportedActivities);
                Assert.Single(this.exportedActivities);
                var activity = this.exportedActivities[0];

                Assert.Equal(ActivityStatusCode.Error, activity.Status);
                var exceptionEvent = activity.Events.FirstOrDefault(e => e.Name == "exception");
                Assert.Null(exceptionEvent); // Verify no exception event by default
            }

            [Fact]
            public void InstrumentConnection_WithNullOptions_UsesDefaultsFromBuilder()
            {
                // Arrange
                var testDbSystem = "customsqlite";
                // Configure options via builder
                SetupTracerProvider(options => {
                    options.DbSystem = testDbSystem;
                    options.RecordException = true; // Change a default to see it applied
                });

                // Pass null (or no) options to InstrumentConnection
                using var instrumentedConnection = AdoNetInstrumentation.InstrumentConnection(this.inMemorySqliteConnection, null);

                using var command = instrumentedConnection.CreateCommand();
                command.CommandText = "SELECT 1;";

                using var commandWithError = instrumentedConnection.CreateCommand();
                commandWithError.CommandText = "SELECT * FROM NoTableHere;";


                // Act
                command.ExecuteScalar();
                try { commandWithError.ExecuteNonQuery(); } catch (SqliteException) { /* ignore */ }


                // Assert
                Assert.NotNull(this.exportedActivities);
                Assert.Equal(2, this.exportedActivities.Count);

                var activity1 = this.exportedActivities[0];
                Assert.Equal(testDbSystem, activity1.GetTagItem(SemanticConventions.AttributeDbSystem));

                var activity2 = this.exportedActivities[1];
                Assert.Equal(testDbSystem, activity2.GetTagItem(SemanticConventions.AttributeDbSystem));
                Assert.Equal(ActivityStatusCode.Error, activity2.Status);
                Assert.NotNull(activity2.Events.FirstOrDefault(e => e.Name == "exception")); // RecordException was true
            }
    }
}
