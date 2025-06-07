// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using Microsoft.Data.Sqlite;
using OpenTelemetry.Trace;
using Xunit;
using OpenTelemetry.Metrics;
using OpenTelemetry.Exporter;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OpenTelemetry.Instrumentation.AdoNet.Tests
{
    public class AdoNetInstrumentationTests : IDisposable
    {
        private readonly SqliteConnection _connection;

        public AdoNetInstrumentationTests()
        {
            _connection = new SqliteConnection("Data Source=:memory:");
            _connection.Open();
            InitializeSchema(_connection);
        }

        private void InitializeSchema(DbConnection connection)
        {
            using var command = connection.CreateCommand();

            command.CommandText = "CREATE TABLE IF NOT EXISTS TestTable (Id INTEGER PRIMARY KEY, Name TEXT);";
            command.ExecuteNonQuery();
            command.CommandText = "INSERT INTO TestTable (Id, Name) VALUES (1, 'InitialName');";
            command.ExecuteNonQuery();

            command.CommandText = "CREATE TABLE IF NOT EXISTS ScalarTest1 (Value INTEGER);";
            command.ExecuteNonQuery();
            command.CommandText = "INSERT INTO ScalarTest1 (Value) VALUES (123);";
            command.ExecuteNonQuery();

            command.CommandText = "CREATE TABLE IF NOT EXISTS ReaderTest1 (Id INTEGER);";
            command.ExecuteNonQuery();
            command.CommandText = "INSERT INTO ReaderTest1 (Id) VALUES (1);";
            command.ExecuteNonQuery();

            command.CommandText = "CREATE TABLE IF NOT EXISTS AsyncScalarTest (Value INTEGER);";
            command.ExecuteNonQuery();
            command.CommandText = "INSERT INTO AsyncScalarTest (Value) VALUES (456);";
            command.ExecuteNonQuery();

            command.CommandText = "CREATE TABLE IF NOT EXISTS AsyncReaderTest (Id INTEGER);";
            command.ExecuteNonQuery();
            command.CommandText = "INSERT INTO AsyncReaderTest (Id) VALUES (1);";
            command.ExecuteNonQuery();

            command.CommandText = "CREATE TABLE IF NOT EXISTS DefaultOptionsTable (Id INTEGER PRIMARY KEY, Name TEXT);";
            command.ExecuteNonQuery();
        }

        public void Dispose()
        {
            _connection.Close();
            _connection.Dispose();
            GC.SuppressFinalize(this);
        }

        private static (List<Activity> ExportedActivities, TracerProvider TracerProvider) SetupTracer(
            Action<AdoNetInstrumentationOptions>? configureAdoNetOptions = null)
        {
            var exportedActivities = new List<Activity>();
            var tracerProvider = Sdk.CreateTracerProviderBuilder()
                .AddAdoNetInstrumentation(configureAdoNetOptions)
                .AddInMemoryExporter(exportedActivities)
                .Build();

            return (exportedActivities, tracerProvider);
        }

        private static (List<Activity> ExportedActivities, TracerProvider TracerProvider) SetupTracer()
        {
            return SetupTracer(configureAdoNetOptions: null);
        }

        private static (List<Metric> ExportedMetrics, MeterProvider MeterProvider) SetupMetricsProvider()
        {
            var exportedMetrics = new List<Metric>();
            var meterProvider = Sdk.CreateMeterProviderBuilder()
                .AddAdoNetInstrumentationMetrics()
                .AddInMemoryExporter(exportedMetrics)
                .Build();

            return (exportedMetrics, meterProvider);
        }

        [Fact]
        public void ExecuteNonQuery_CreatesActivity()
        {
            (var exportedActivities, var tracerProvider) = SetupTracer();
            using (tracerProvider)
            {
                using var instrumentedConnection = AdoNetInstrumentation.InstrumentConnection(this._connection);
                using var command = instrumentedConnection.CreateCommand();
                command.CommandText = "CREATE TABLE IF NOT EXISTS SpecificTableForNonQueryTest (Id INTEGER PRIMARY KEY);";
                command.ExecuteNonQuery();
                Assert.Single(exportedActivities);
                var activity = exportedActivities[0];
                Assert.Equal($"{this._connection.Database}.{nameof(DbCommand.ExecuteNonQuery)}", activity.DisplayName);
                Assert.Equal(ActivityKind.Client, activity.Kind);
                Assert.Equal("sqlite", activity.GetTagItem(SemanticConventions.AttributeDbSystem));
                Assert.Equal(this._connection.Database, activity.GetTagItem(SemanticConventions.AttributeDbName));
                Assert.Equal(this._connection.DataSource, activity.GetTagItem(SemanticConventions.AttributeNetPeerName));
                Assert.Equal(command.CommandText, activity.GetTagItem(SemanticConventions.AttributeDbStatement));
                Assert.Equal(nameof(DbCommand.ExecuteNonQuery), activity.GetTagItem(SemanticConventions.AttributeDbOperation));
                Assert.Equal(ActivityStatusCode.Ok, activity.Status);
            }
        }

        [Fact]
        public void ExecuteScalar_CreatesActivity()
        {
            (var exportedActivities, var tracerProvider) = SetupTracer();
            using (tracerProvider)
            {
                using var instrumentedConnection = AdoNetInstrumentation.InstrumentConnection(this._connection);
                using var command = instrumentedConnection.CreateCommand();
                command.CommandText = "SELECT Value FROM ScalarTest1 WHERE Value = 123;";
                var result = command.ExecuteScalar();
                Assert.Equal(123L, result);
                Assert.NotNull(exportedActivities);
                Assert.Single(exportedActivities);
                var activity = exportedActivities[0];
                Assert.Equal($"{this._connection.Database}.{nameof(DbCommand.ExecuteScalar)}", activity.DisplayName);
                Assert.Equal("sqlite", activity.GetTagItem(SemanticConventions.AttributeDbSystem));
                Assert.Equal(this._connection.Database, activity.GetTagItem(SemanticConventions.AttributeDbName));
                Assert.Equal(this._connection.DataSource, activity.GetTagItem(SemanticConventions.AttributeNetPeerName));
                Assert.Equal(command.CommandText, activity.GetTagItem(SemanticConventions.AttributeDbStatement));
                Assert.Equal(nameof(DbCommand.ExecuteScalar), activity.GetTagItem(SemanticConventions.AttributeDbOperation));
                Assert.Equal(ActivityStatusCode.Ok, activity.Status);
            }
        }

        [Fact]
        public void ExecuteReader_CreatesActivity_AndStopsOnReaderClose()
        {
            (var exportedActivities, var tracerProvider) = SetupTracer();
            using (tracerProvider)
            {
                using var instrumentedConnection = AdoNetInstrumentation.InstrumentConnection(this._connection);
                using var command = instrumentedConnection.CreateCommand();
                command.CommandText = "SELECT Id FROM ReaderTest1;";
                using (var reader = command.ExecuteReader())
                {
                    Assert.True(reader.Read());
                    Assert.Equal(1, reader.GetInt32(0));
                    Assert.Empty(exportedActivities!);
                }
                Assert.NotNull(exportedActivities);
                Assert.Single(exportedActivities);
                var activity = exportedActivities[0];
                Assert.Equal($"{this._connection.Database}.{nameof(DbCommand.ExecuteReader)}", activity.DisplayName);
                Assert.Equal("sqlite", activity.GetTagItem(SemanticConventions.AttributeDbSystem));
                Assert.Equal(this._connection.Database, activity.GetTagItem(SemanticConventions.AttributeDbName));
                Assert.Equal(this._connection.DataSource, activity.GetTagItem(SemanticConventions.AttributeNetPeerName));
                Assert.Equal(command.CommandText, activity.GetTagItem(SemanticConventions.AttributeDbStatement));
                Assert.Equal(nameof(DbCommand.ExecuteReader), activity.GetTagItem(SemanticConventions.AttributeDbOperation));
                Assert.Equal(ActivityStatusCode.Ok, activity.Status);
                Assert.True(activity.Duration > TimeSpan.Zero);
            }
        }

        [Fact]
        public void ExecuteNonQuery_WhenExceptionOccurs_ActivityRecordsException()
        {
            (var exportedActivities, var tracerProvider) = SetupTracer(options => options.RecordException = true);
            using (tracerProvider)
            {
                using var instrumentedConnection = AdoNetInstrumentation.InstrumentConnection(this._connection);
                using var command = instrumentedConnection.CreateCommand();
                command.CommandText = "INSERT INTO NonExistentTable (Id) VALUES (1);";
                Assert.Throws<SqliteException>(() => command.ExecuteNonQuery());
                Assert.NotNull(exportedActivities);
                Assert.Single(exportedActivities);
                var activity = exportedActivities[0];
                Assert.Equal(ActivityStatusCode.Error, activity.Status);
                Assert.Contains("SQLite Error 1: 'no such table: NonExistentTable'", activity.StatusDescription);
                var exceptionEvent = activity.Events.FirstOrDefault(e => e.Name == "exception");
                Assert.NotNull(exceptionEvent);
                Assert.Contains(exceptionEvent.Tags, kvp => kvp.Key == SemanticConventions.AttributeExceptionType && kvp.Value.Contains("Microsoft.Data.Sqlite.SqliteException"));
                Assert.Contains(exceptionEvent.Tags, kvp => kvp.Key == SemanticConventions.AttributeExceptionMessage && kvp.Value.Contains("no such table: NonExistentTable"));
            }
        }

        [Fact]
        public void CommandTypeStoredProcedure_CreatesActivityWithCommandTextAsStatement()
        {
            (var exportedActivities, var tracerProvider) = SetupTracer();
            using (tracerProvider)
            {
                using var instrumentedConnection = AdoNetInstrumentation.InstrumentConnection(this._connection);
                using var command = instrumentedConnection.CreateCommand();
                command.CommandText = "MyTestProcedure";
                command.CommandType = CommandType.StoredProcedure;
                try { command.ExecuteNonQuery(); } catch (SqliteException) { /* Expected */ }
                Assert.NotNull(exportedActivities);
                Assert.Single(exportedActivities);
                var activity = exportedActivities[0];
                Assert.Equal("MyTestProcedure", activity.GetTagItem(SemanticConventions.AttributeDbStatement));
                Assert.Equal(this._connection.Database + ".MyTestProcedure", activity.DisplayName); // Check DisplayName for SP
                Assert.Equal("MyTestProcedure", activity.GetTagItem(SemanticConventions.AttributeDbOperation)); // Operation is SP name
            }
        }

        [Fact]
        public void FilterOption_PreventsActivityCreation()
        {
            (var exportedActivities, var tracerProvider) = SetupTracer(options => { options.Filter = cmd => !cmd.CommandText.Contains("SKIP_THIS"); });
            using (tracerProvider)
            {
                using var instrumentedConnection = AdoNetInstrumentation.InstrumentConnection(this._connection);
                using (var commandToSkip = instrumentedConnection.CreateCommand()) { commandToSkip.CommandText = "SELECT 'SKIP_THIS';"; commandToSkip.ExecuteScalar(); }
                using (var commandToInstrument = instrumentedConnection.CreateCommand()) { commandToInstrument.CommandText = "SELECT 'INSTRUMENT_THIS';"; commandToInstrument.ExecuteScalar(); }
                Assert.NotNull(exportedActivities);
                Assert.Single(exportedActivities);
                var activity = exportedActivities[0];
                Assert.Equal("SELECT 'INSTRUMENT_THIS';", activity.GetTagItem(SemanticConventions.AttributeDbStatement));
            }
        }

        [Fact]
        public void EnrichOption_AddsCustomTags()
        {
            var customTagName = "my.custom.tag";
            var customTagValue = "custom.value";
            (var exportedActivities, var tracerProvider) = SetupTracer(options => { options.Enrich = (activity, command) => { activity.SetTag(customTagName, customTagValue); activity.SetTag("db.command.type", command.CommandType.ToString()); }; });
            using (tracerProvider)
            {
                using var instrumentedConnection = AdoNetInstrumentation.InstrumentConnection(this._connection);
                using var command = instrumentedConnection.CreateCommand();
                command.CommandText = "SELECT 1;"; command.CommandType = CommandType.Text;
                command.ExecuteScalar();
                Assert.NotNull(exportedActivities); Assert.Single(exportedActivities);
                var activity = exportedActivities[0];
                Assert.Equal(customTagValue, activity.GetTagItem(customTagName));
                Assert.Equal(CommandType.Text.ToString(), activity.GetTagItem("db.command.type"));
            }
        }

        [Fact]
        public void DbSystemOption_OverridesDefault()
        {
            var overrideDbSystem = "testdb";
            (var exportedActivities, var tracerProvider) = SetupTracer(options => options.DbSystem = overrideDbSystem);
            using (tracerProvider)
            {
                using var instrumentedConnection = AdoNetInstrumentation.InstrumentConnection(this._connection);
                using var command = instrumentedConnection.CreateCommand();
                command.CommandText = "SELECT 1;";
                command.ExecuteScalar();
                Assert.NotNull(exportedActivities); Assert.Single(exportedActivities);
                var activity = exportedActivities[0];
                Assert.Equal(overrideDbSystem, activity.GetTagItem(SemanticConventions.AttributeDbSystem));
            }
        }

        [Fact]
        public async Task ExecuteNonQueryAsync_CreatesActivity()
        {
            (var exportedActivities, var tracerProvider) = SetupTracer();
            using (tracerProvider)
            {
                using var instrumentedConnection = AdoNetInstrumentation.InstrumentConnection(this._connection);
                using var command = instrumentedConnection.CreateCommand();
                command.CommandText = "CREATE TABLE IF NOT EXISTS SpecificTableForAsyncNonQueryTest (Id INTEGER PRIMARY KEY);";
                await command.ExecuteNonQueryAsync();
                Assert.NotNull(exportedActivities); Assert.Single(exportedActivities);
                var activity = exportedActivities[0];
                Assert.Equal($"{this._connection.Database}.{nameof(DbCommand.ExecuteNonQueryAsync)}", activity.DisplayName);
                Assert.Equal(ActivityKind.Client, activity.Kind);
                Assert.Equal("sqlite", activity.GetTagItem(SemanticConventions.AttributeDbSystem));
                Assert.Equal(this._connection.Database, activity.GetTagItem(SemanticConventions.AttributeDbName));
                Assert.Equal(this._connection.DataSource, activity.GetTagItem(SemanticConventions.AttributeNetPeerName));
                Assert.Equal(command.CommandText, activity.GetTagItem(SemanticConventions.AttributeDbStatement));
                Assert.Equal(nameof(DbCommand.ExecuteNonQueryAsync), activity.GetTagItem(SemanticConventions.AttributeDbOperation));
                Assert.Equal(ActivityStatusCode.Ok, activity.Status);
            }
        }

        [Fact]
        public async Task ExecuteReaderAsync_CreatesActivity_AndStopsOnReaderClose()
        {
            (var exportedActivities, var tracerProvider) = SetupTracer();
            using (tracerProvider)
            {
                using var instrumentedConnection = AdoNetInstrumentation.InstrumentConnection(this._connection);
                using var command = instrumentedConnection.CreateCommand();
                command.CommandText = "SELECT Id FROM AsyncReaderTest;";
                using (var reader = await command.ExecuteReaderAsync())
                {
                    Assert.True(await reader.ReadAsync()); Assert.Equal(1, reader.GetInt32(0)); Assert.Empty(exportedActivities!);
                }
                Assert.NotNull(exportedActivities); Assert.Single(exportedActivities);
                var activity = exportedActivities[0];
                Assert.Equal($"{this._connection.Database}.{nameof(DbCommand.ExecuteReaderAsync)}", activity.DisplayName);
                Assert.Equal("sqlite", activity.GetTagItem(SemanticConventions.AttributeDbSystem));
                Assert.Equal(this._connection.Database, activity.GetTagItem(SemanticConventions.AttributeDbName));
                Assert.Equal(this._connection.DataSource, activity.GetTagItem(SemanticConventions.AttributeNetPeerName));
                Assert.Equal(command.CommandText, activity.GetTagItem(SemanticConventions.AttributeDbStatement));
                Assert.Equal(nameof(DbCommand.ExecuteReaderAsync), activity.GetTagItem(SemanticConventions.AttributeDbOperation));
                Assert.Equal(ActivityStatusCode.Ok, activity.Status); Assert.True(activity.Duration > TimeSpan.Zero);
            }
        }

        [Fact]
        public async Task ExecuteScalarAsync_CreatesActivity()
        {
            (var exportedActivities, var tracerProvider) = SetupTracer();
            using (tracerProvider)
            {
                using var instrumentedConnection = AdoNetInstrumentation.InstrumentConnection(this._connection);
                using var command = instrumentedConnection.CreateCommand();
                command.CommandText = "SELECT Value FROM AsyncScalarTest WHERE Value = 456;";
                var result = await command.ExecuteScalarAsync();
                Assert.Equal(456L, result);
                Assert.NotNull(exportedActivities); Assert.Single(exportedActivities);
                var activity = exportedActivities[0];
                Assert.Equal($"{this._connection.Database}.{nameof(DbCommand.ExecuteScalarAsync)}", activity.DisplayName);
                Assert.Equal("sqlite", activity.GetTagItem(SemanticConventions.AttributeDbSystem));
                Assert.Equal(this._connection.Database, activity.GetTagItem(SemanticConventions.AttributeDbName));
                Assert.Equal(this._connection.DataSource, activity.GetTagItem(SemanticConventions.AttributeNetPeerName));
                Assert.Equal(command.CommandText, activity.GetTagItem(SemanticConventions.AttributeDbStatement));
                Assert.Equal(nameof(DbCommand.ExecuteScalarAsync), activity.GetTagItem(SemanticConventions.AttributeDbOperation));
                Assert.Equal(ActivityStatusCode.Ok, activity.Status);
            }
        }

        [Fact]
        public void SetDbStatementForText_False_DoesNotSetDbStatementTag()
        {
            (var exportedActivities, var tracerProvider) = SetupTracer(options => options.SetDbStatementForText = false);
            using (tracerProvider)
            {
                using var instrumentedConnection = AdoNetInstrumentation.InstrumentConnection(this._connection);
                using var command = instrumentedConnection.CreateCommand();
                command.CommandText = "SELECT 1;"; command.CommandType = CommandType.Text;
                command.ExecuteScalar();
                Assert.NotNull(exportedActivities); Assert.Single(exportedActivities);
                var activity = exportedActivities[0];
                Assert.Null(activity.GetTagItem(SemanticConventions.AttributeDbStatement));
                Assert.Equal("sqlite", activity.GetTagItem(SemanticConventions.AttributeDbSystem));
            }
        }

        [Fact]
        public void RecordException_False_DoesNotRecordExceptionEvent()
        {
            (var exportedActivities, var tracerProvider) = SetupTracer(options => options.RecordException = false);
            using (tracerProvider)
            {
                using var instrumentedConnection = AdoNetInstrumentation.InstrumentConnection(this._connection);
                using var command = instrumentedConnection.CreateCommand();
                command.CommandText = "INSERT INTO NonExistentTable (Id) VALUES (1);";
                Assert.Throws<SqliteException>(() => command.ExecuteNonQuery());
                Assert.NotNull(exportedActivities); Assert.Single(exportedActivities);
                var activity = exportedActivities[0];
                Assert.Equal(ActivityStatusCode.Error, activity.Status);
                Assert.Contains("SQLite Error 1: 'no such table: NonExistentTable'", activity.StatusDescription);
                var exceptionEvent = activity.Events.FirstOrDefault(e => e.Name == "exception");
                Assert.Null(exceptionEvent);
            }
        }

        [Fact]
        public void DefaultOptions_RecordException_False_DoesNotRecordExceptionEvent()
        {
            (var exportedActivities, var tracerProvider) = SetupTracer();
            using (tracerProvider)
            {
                using var instrumentedConnection = AdoNetInstrumentation.InstrumentConnection(this._connection);
                using var command = instrumentedConnection.CreateCommand();
                command.CommandText = "INSERT INTO AnotherNonExistentTable (Id) VALUES (1);";
                Assert.Throws<SqliteException>(() => command.ExecuteNonQuery());
                Assert.NotNull(exportedActivities); Assert.Single(exportedActivities);
                var activity = exportedActivities[0];
                Assert.Equal(ActivityStatusCode.Error, activity.Status);
                var exceptionEvent = activity.Events.FirstOrDefault(e => e.Name == "exception");
                Assert.Null(exceptionEvent);
            }
        }

        [Fact]
        public void InstrumentConnection_WithNullOptions_UsesDefaultsFromBuilder()
        {
            var testDbSystem = "customsqlite";
            (var exportedActivities, var tracerProvider) = SetupTracer(options => { options.DbSystem = testDbSystem; options.RecordException = true; });
            using (tracerProvider)
            {
                using var instrumentedConnection = AdoNetInstrumentation.InstrumentConnection(this._connection, null);
                using var command = instrumentedConnection.CreateCommand();
                command.CommandText = "SELECT 1;";
                using var commandWithError = instrumentedConnection.CreateCommand();
                commandWithError.CommandText = "SELECT * FROM NoTableHere;";
                command.ExecuteScalar();
                try { commandWithError.ExecuteNonQuery(); } catch (SqliteException) { /* ignore */ }
                Assert.NotNull(exportedActivities); Assert.Equal(2, exportedActivities.Count);
                var activity1 = exportedActivities.First(a => a.GetTagItem("db.statement")?.ToString() == "SELECT 1;");
                Assert.Equal(testDbSystem, activity1.GetTagItem(SemanticConventions.AttributeDbSystem));
                var activity2 = exportedActivities.First(a => a.GetTagItem("db.statement")?.ToString() == "SELECT * FROM NoTableHere;");
                Assert.Equal(testDbSystem, activity2.GetTagItem(SemanticConventions.AttributeDbSystem));
                Assert.Equal(ActivityStatusCode.Error, activity2.Status);
                Assert.NotNull(activity2.Events.FirstOrDefault(e => e.Name == "exception"));
            }
        }

        [Fact]
        public void ExecuteNonQuery_WithMetricsEnabled_RecordsDurationAndCallsMetrics()
        {
            (var exportedActivities, var tracerProvider) = SetupTracer(options => options.EmitMetrics = true);
            (var exportedMetrics, var meterProvider) = SetupMetricsProvider();
            using (tracerProvider)
            using (meterProvider)
            {
                using var instrumentedConnection = AdoNetInstrumentation.InstrumentConnection(this._connection);
                using var command = instrumentedConnection.CreateCommand();
                command.CommandText = "CREATE TABLE IF NOT EXISTS MetricsTestTable (Id INTEGER PRIMARY KEY);";
                command.ExecuteNonQuery();
                meterProvider.ForceFlush();
                Assert.Single(exportedMetrics.Where(m => m.Name == "db.client.duration"));
                Assert.Single(exportedMetrics.Where(m => m.Name == "db.client.calls"));
                var durationMetric = exportedMetrics.First(m => m.Name == "db.client.duration");
                var callMetric = exportedMetrics.First(m => m.Name == "db.client.calls");
                bool foundDurationPoint = false;
                foreach (var p in durationMetric.GetMetricPoints())
                {
                    Assert.True(p.GetHistogramSum() > 0); Assert.Equal(1L, p.GetHistogramCount());
                    var tags = new List<KeyValuePair<string, object?>>();
                    foreach(var tag in p.Tags) { tags.Add(tag); }
                    Assert.Contains(new KeyValuePair<string, object?>("db.system", "sqlite"), tags);
                    Assert.Contains(new KeyValuePair<string, object?>("db.operation", "ExecuteNonQuery"), tags);
                    Assert.Contains(new KeyValuePair<string, object?>("server.address", this._connection.DataSource), tags);
                    foundDurationPoint = true;
                }
                Assert.True(foundDurationPoint, "No histogram point found for duration metric.");
                bool foundCallsPoint = false;
                foreach (var p in callMetric.GetMetricPoints())
                {
                    Assert.Equal(1L, p.GetLongSum());
                    var tags = new List<KeyValuePair<string, object?>>();
                    foreach(var tag in p.Tags) { tags.Add(tag); }
                    Assert.Contains(new KeyValuePair<string, object?>("db.system", "sqlite"), tags);
                    Assert.Contains(new KeyValuePair<string, object?>("db.operation", "ExecuteNonQuery"), tags);
                    foundCallsPoint = true;
                }
                Assert.True(foundCallsPoint, "No sum point found for calls metric.");
            }
        }

        [Fact]
        public void ExecuteNonQuery_WithMetricsDisabled_DoesNotRecordMetrics()
        {
            (var exportedActivities, var tracerProvider) = SetupTracer(options => options.EmitMetrics = false);
            (var exportedMetrics, var meterProvider) = SetupMetricsProvider();
            using (tracerProvider)
            using (meterProvider)
            {
                using var instrumentedConnection = AdoNetInstrumentation.InstrumentConnection(this._connection);
                using var command = instrumentedConnection.CreateCommand();
                command.CommandText = "CREATE TABLE IF NOT EXISTS MetricsTestTableDisabled (Id INTEGER PRIMARY KEY);";
                command.ExecuteNonQuery();
                meterProvider.ForceFlush();
                Assert.DoesNotContain(exportedMetrics, m => m.Name == "db.client.duration");
                Assert.DoesNotContain(exportedMetrics, m => m.Name == "db.client.calls");
            }
        }

        [Fact]
        public void ExecuteNonQuery_WhenExceptionOccurs_MetricsIncludeErrorTag()
        {
            (var exportedActivities, var tracerProvider) = SetupTracer(options => { options.EmitMetrics = true; options.RecordException = true; });
            (var exportedMetrics, var meterProvider) = SetupMetricsProvider();
            using (tracerProvider)
            using (meterProvider)
            {
                using var instrumentedConnection = AdoNetInstrumentation.InstrumentConnection(this._connection);
                using var command = instrumentedConnection.CreateCommand();
                command.CommandText = "INSERT INTO NonExistentTableForMetrics (Id) VALUES (1);";
                Assert.Throws<SqliteException>(() => command.ExecuteNonQuery());
                meterProvider.ForceFlush();
                Assert.Single(exportedMetrics.Where(m => m.Name == "db.client.duration"));
                Assert.Single(exportedMetrics.Where(m => m.Name == "db.client.calls"));
                var durationMetric = exportedMetrics.First(m => m.Name == "db.client.duration");
                bool foundErrorDurationPoint = false;
                foreach (var p in durationMetric.GetMetricPoints())
                {
                    var tags = new List<KeyValuePair<string, object?>>();
                    foreach(var tag in p.Tags) { tags.Add(tag); }
                    Assert.Contains(new KeyValuePair<string, object?>("error.type", typeof(SqliteException).Name), tags);
                    foundErrorDurationPoint = true;
                }
                Assert.True(foundErrorDurationPoint, "No histogram point with error.type tag found for duration metric.");
                var callMetric = exportedMetrics.First(m => m.Name == "db.client.calls");
                bool foundErrorCallPoint = false;
                foreach (var p in callMetric.GetMetricPoints())
                {
                    var tags = new List<KeyValuePair<string, object?>>();
                    foreach(var tag in p.Tags) { tags.Add(tag); }
                    Assert.Contains(new KeyValuePair<string, object?>("error.type", typeof(SqliteException).Name), tags);
                    foundErrorCallPoint = true;
                }
                Assert.True(foundErrorCallPoint, "No sum point with error.type tag found for calls metric.");
            }
        }

        [Theory]
        [InlineData("SELECT * FROM Users WHERE UserId = 123; -- This is a comment", "SELECT * FROM Users WHERE UserId = ?;")]
        [InlineData("SELECT * FROM Products WHERE Name = 'Test Product' AND Category = 'Electronics'", "SELECT * FROM Products WHERE Name = ? AND Category = ?")]
        [InlineData("SELECT * FROM Logs WHERE Timestamp < 0x1A2B3C;", "SELECT * FROM Logs WHERE Timestamp < ?;")]
        [InlineData("/* Multi-line\n   Comment */\nSELECT Value FROM Settings WHERE Id = -10.5;", "SELECT Value FROM Settings WHERE Id = ?;")]
        [InlineData("SELECT Name FROM Employees -- Inline Comment\nWHERE DepartmentID = 1;", "SELECT Name FROM Employees \nWHERE DepartmentID = ?;")]
        public void SanitizeDbStatement_True_SanitizesStatement(string rawSql, string expectedSanitizedSql)
        {
            (var exportedActivities, var tracerProvider) = SetupTracer(options => {
                options.SetDbStatementForText = true;
                options.SanitizeDbStatement = true;
            });

            using (tracerProvider)
            {
                using var instrumentedConnection = AdoNetInstrumentation.InstrumentConnection(this._connection);
                using var command = instrumentedConnection.CreateCommand();
                command.CommandText = rawSql;
                command.CommandType = CommandType.Text;
                try { command.ExecuteScalar(); } catch { /* Ignore execution errors */ }
                Assert.NotEmpty(exportedActivities);
                var activity = exportedActivities[0];
                Assert.Equal(expectedSanitizedSql, activity.GetTagItem(SemanticConventions.AttributeDbStatement));
            }
        }

        [Fact]
        public void SanitizeDbStatement_False_UsesRawStatement()
        {
            var rawSql = "SELECT * FROM Users WHERE UserId = 123; -- This is a comment";
            (var exportedActivities, var tracerProvider) = SetupTracer(options => {
                options.SetDbStatementForText = true;
                options.SanitizeDbStatement = false;
            });

            using (tracerProvider)
            {
                using var instrumentedConnection = AdoNetInstrumentation.InstrumentConnection(this._connection);
                using var command = instrumentedConnection.CreateCommand();
                command.CommandText = rawSql;
                command.CommandType = CommandType.Text;
                try { command.ExecuteScalar(); } catch { /* Ignore execution errors */ }
                Assert.NotEmpty(exportedActivities);
                var activity = exportedActivities[0];
                Assert.Equal(rawSql, activity.GetTagItem(SemanticConventions.AttributeDbStatement));
            }
        }

        [Fact]
        public void SetDbStatementForText_False_NoStatementRegardlessOfSanitizeOption()
        {
            var rawSql = "SELECT * FROM Users WHERE UserId = 123; -- This is a comment";
            (var exportedActivities, var tracerProvider) = SetupTracer(options => {
                options.SetDbStatementForText = false;
                options.SanitizeDbStatement = true;
            });

            using (tracerProvider)
            {
                using var instrumentedConnection = AdoNetInstrumentation.InstrumentConnection(this._connection);
                using var command = instrumentedConnection.CreateCommand();
                command.CommandText = rawSql;
                command.CommandType = CommandType.Text;
                try { command.ExecuteScalar(); } catch { /* Ignore execution errors */ }
                Assert.NotEmpty(exportedActivities);
                var activity = exportedActivities[0];
                Assert.Null(activity.GetTagItem(SemanticConventions.AttributeDbStatement));
            }
        }

        [Fact]
        public void StoredProcedure_SanitizeOptionIgnored_DbStatementIsSPName()
        {
            var spName = "MyTestProcedure";
            (var exportedActivities, var tracerProvider) = SetupTracer(options => {
                options.SetDbStatementForText = true;
                options.SanitizeDbStatement = true;
            });

            using (tracerProvider)
            {
                using var instrumentedConnection = AdoNetInstrumentation.InstrumentConnection(this._connection);
                using var command = instrumentedConnection.CreateCommand();
                command.CommandText = spName;
                command.CommandType = CommandType.StoredProcedure;
                try { command.ExecuteNonQuery(); } catch (SqliteException) { /* Expected */ }
                Assert.NotEmpty(exportedActivities);
                var activity = exportedActivities[0];
                Assert.Equal(spName, activity.GetTagItem(SemanticConventions.AttributeDbStatement));
                Assert.Equal(spName, activity.GetTagItem(SemanticConventions.AttributeDbStoredProcedureName));
            }
        }
    }

    // Mock Connection Classes for DbSystemResolver Testing (No longer needed here, moved to AdoNetInstrumentationDITests.cs conceptually or separate file)
    // For the current task, these are not being moved from AdoNetInstrumentationTests.cs if they were already there.
    // Assuming they are present from a previous step if DbSystemResolver_IdentifiesCorrectDbSystem tests are in this file.
    // If DbSystemResolver_IdentifiesCorrectDbSystem was intended for AdoNetInstrumentationDITests.cs, then these mocks would go there.
    // The prompt implies adding new tests to *this* file, AdoNetInstrumentationTests.cs.
    // So, if the DbSystemResolver tests were also added here, their mocks would be here too.
    // For this specific subtask, only adding the sanitization tests.
}
