// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Xunit;

namespace OpenTelemetry.Instrumentation.AdoNet.Tests
{
    public class AdoNetInstrumentationDITests // No longer IDisposable
    {
        // private SqliteConnection? _sharedSqliteConnection; // Not used in current tests, keep for future reference

        // Helper to build ServiceProvider for a test
        private static (ServiceProvider ServiceProvider, List<Activity> ExportedActivities, List<Metric> ExportedMetrics) BuildServiceProvider(
            Action<IServiceCollection> configureTestServices, // Renamed for clarity
            Action<AdoNetInstrumentationOptions>? configureAdoNetOptions = null)
        {
            var services = new ServiceCollection();

            if (configureAdoNetOptions != null)
            {
                services.ConfigureAdoNetInstrumentation(configureAdoNetOptions);
            }
            else
            {
                services.ConfigureAdoNetInstrumentation(o => { }); // Default empty config
            }

            // Allow test to add its specific registrations
            configureTestServices(services);

            var exportedActivities = new List<Activity>(); // Local list
            var exportedMetrics = new List<Metric>();    // Local list

            services.AddOpenTelemetry()
                .WithTracing(builder => builder
                    .AddAdoNetInstrumentation()
                    .AddInMemoryExporter(exportedActivities))
                .WithMetrics(builder => builder
                    .AddAdoNetInstrumentationMetrics()
                    .AddInMemoryExporter(exportedMetrics));

            var serviceProvider = services.BuildServiceProvider(); // Local SP
            return (serviceProvider, exportedActivities, exportedMetrics);
        }

        [Fact]
        public void AddInstrumentedDbProviderFactory_ResolvesInstrumentedFactory()
        {
            // Arrange
            // Ensure Sqlite is registered
            try { DbProviderFactories.RegisterFactory("Microsoft.Data.Sqlite", SqliteFactory.Instance); }
            catch (ArgumentException) { /* Already registered, common in test runners */ }

            (var serviceProvider, var exportedActivities, var exportedMetrics) = BuildServiceProvider(services =>
            {
                services.AddInstrumentedDbProviderFactory("Microsoft.Data.Sqlite");
            }, options => {
                options.EmitMetrics = true;
                options.DbSystem = "sqlite_di_factory_test";
            });

            using (serviceProvider)
            {
                var meterProvider = serviceProvider.GetRequiredService<MeterProvider>();

                // Act
                var factory = serviceProvider.GetRequiredService<DbProviderFactory>();
            Assert.IsType<InstrumentedDbProviderFactory>(factory);

            using var connection = factory.CreateConnection();
            Assert.NotNull(connection);
            connection.ConnectionString = "Data Source=:memory:";
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1;";
            command.ExecuteScalar();
                connection.Close();

                meterProvider.ForceFlush();

                // Assert
                Assert.NotEmpty(exportedActivities);
                Assert.Contains(exportedActivities, act => act.GetTagItem(SemanticConventions.AttributeDbSystem)?.ToString() == "sqlite_di_factory_test");

                Assert.NotEmpty(exportedMetrics);
                var callMetric = exportedMetrics.FirstOrDefault(m => m.Name == "db.client.calls");
                Assert.NotNull(callMetric);
                bool foundCorrectTag = false;
                foreach(var mp in callMetric.GetMetricPoints()){
                    foreach(var tag in mp.Tags) {
                        if(tag.Key == "db.system" && tag.Value?.ToString() == "sqlite_di_factory_test") {
                            foundCorrectTag = true; break;
                        }
                    }
                    if(foundCorrectTag) break;
                }
                Assert.True(foundCorrectTag, "Metric did not contain correct db.system tag from DI options.");
            }
        }

        [Fact]
        public void AddInstrumentedDbConnection_ResolvesInstrumentedConnection()
        {
            const string testConnectionString = "Data Source=sharedInMemoryDITest;Mode=Memory;Cache=Shared";

            using (var setupConn = new SqliteConnection(testConnectionString))
            {
                setupConn.Open();
                using var cmd = setupConn.CreateCommand();
                cmd.CommandText = "CREATE TABLE IF NOT EXISTS DiConnTest (Id INT);";
                cmd.ExecuteNonQuery();
            }

            (var serviceProvider, var exportedActivities, var exportedMetrics) = BuildServiceProvider(services =>
            {
                services.AddInstrumentedDbConnection(
                    provider => new SqliteConnection(testConnectionString),
                    lifetime: ServiceLifetime.Scoped
                );
            }, options => {
                options.EmitMetrics = true;
                options.DbSystem = "sqlite_di_conn_test";
            });

            using (serviceProvider)
            {
                var meterProvider = serviceProvider.GetRequiredService<MeterProvider>();

                using (var scope = serviceProvider.CreateScope())
                {
                    var connection = scope.ServiceProvider.GetRequiredService<DbConnection>();
                    Assert.IsAssignableFrom<InstrumentedDbConnection>(connection);

                    if(connection.State != ConnectionState.Open) connection.Open();
                    using var command = connection.CreateCommand();
                    command.CommandText = "SELECT COUNT(*) FROM DiConnTest;";
                    var count = command.ExecuteScalar();
                    Assert.NotNull(count);
                    connection.Close();
                }
                meterProvider.ForceFlush();

                Assert.NotEmpty(exportedActivities);
                Assert.Contains(exportedActivities, act => act.GetTagItem(SemanticConventions.AttributeDbSystem)?.ToString() == "sqlite_di_conn_test");

                Assert.NotEmpty(exportedMetrics);
                var callMetric = exportedMetrics.FirstOrDefault(m => m.Name == "db.client.calls");
                Assert.NotNull(callMetric);
                bool foundCorrectTag = false;
                foreach(var mp in callMetric.GetMetricPoints()){
                    foreach(var tag in mp.Tags) {
                        if(tag.Key == "db.system" && tag.Value?.ToString() == "sqlite_di_conn_test") {
                            foundCorrectTag = true; break;
                        }
                    }
                    if(foundCorrectTag) break;
                }
                Assert.True(foundCorrectTag, "Metric did not contain correct db.system tag from DI options for connection.");
            }
        }

        [Fact]
        public void NamedOptions_AreApplied_ViaDI()
        {
            const string namedOption = "MyNamedDb";
            const string testConnectionString = "Data Source=:memory:";

            var localServices = new ServiceCollection();
            var exportedActivities = new List<Activity>();
            var exportedMetrics = new List<Metric>();

            // Configure named options
            localServices.ConfigureAdoNetInstrumentation(namedOption, opt => {
                opt.EmitMetrics = true;
                opt.DbSystem = "sqlite_named_options";
            });

            localServices.AddOpenTelemetry()
                .WithTracing(builder => builder.AddAdoNetInstrumentation().AddInMemoryExporter(exportedActivities))
                .WithMetrics(builder => builder.AddAdoNetInstrumentationMetrics().AddInMemoryExporter(exportedMetrics));

            localServices.AddInstrumentedDbConnection(
                    provider => new SqliteConnection(testConnectionString),
                    optionsName: namedOption, // Use the named options
                    lifetime: ServiceLifetime.Scoped);

            var serviceProvider = localServices.BuildServiceProvider();
            var meterProvider = serviceProvider.GetRequiredService<MeterProvider>();

            using (var scope = serviceProvider.CreateScope())
            {
                var connection = scope.ServiceProvider.GetRequiredService<DbConnection>();
                if(connection.State != ConnectionState.Open) connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT 1;";
                command.ExecuteScalar();
                connection.Close();
            }
            meterProvider.ForceFlush();

            Assert.NotEmpty(exportedActivities);
            Assert.Contains(exportedActivities, act => act.GetTagItem(SemanticConventions.AttributeDbSystem)?.ToString() == "sqlite_named_options");
            Assert.NotEmpty(exportedMetrics);
            var callMetric = exportedMetrics.FirstOrDefault(m => m.Name == "db.client.calls");
            Assert.NotNull(callMetric);
            bool foundCorrectTag = false;
            foreach(var mp in callMetric.GetMetricPoints()){
                foreach(var tag in mp.Tags) {
                    if(tag.Key == "db.system" && tag.Value?.ToString() == "sqlite_named_options") {
                        foundCorrectTag = true; break;
                    }
                }
                if(foundCorrectTag) break;
            }
            Assert.True(foundCorrectTag, "Metric did not contain correct db.system tag from NAMED DI options.");
            serviceProvider.Dispose();
        }
    }
}
