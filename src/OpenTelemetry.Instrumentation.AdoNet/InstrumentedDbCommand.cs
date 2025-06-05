// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using OpenTelemetry.Trace; // Required for SemanticConventions and Activity an Status

namespace OpenTelemetry.Instrumentation.AdoNet
{
    internal sealed class InstrumentedDbCommand : DbCommand
    {
        private readonly DbCommand wrappedCommand;
        private readonly InstrumentedDbConnection instrumentedConnection;
        private readonly AdoNetInstrumentationOptions options;

        public InstrumentedDbCommand(DbCommand command, InstrumentedDbConnection connection, AdoNetInstrumentationOptions options)
        {
            this.wrappedCommand = command ?? throw new ArgumentNullException(nameof(command));
            this.instrumentedConnection = connection ?? throw new ArgumentNullException(nameof(connection));
            this.options = options ?? throw new ArgumentNullException(nameof(options));
        }

        private Activity? StartActivity(string operationName)
        {
            if (this.options.Filter != null && !this.options.Filter(this.wrappedCommand))
            {
                // If the filter returns false, don't start an activity.
                return null;
            }

            var activity = InstrumentedDbConnection.ActivitySource.StartActivity(
                $"{this.instrumentedConnection.Database}.{operationName}", // Using Database for name, can be refined
                ActivityKind.Client);

            if (activity == null)
            {
                return null; // Early exit if not sampled (StartActivity returns null)
            }

            if (activity.IsAllDataRequested)
            {
                // Default tags
                activity.SetTag(SemanticConventions.AttributeDbSystem, GetDbSystem());
                activity.SetTag(SemanticConventions.AttributeDbName, this.instrumentedConnection.Database);
                activity.SetTag(SemanticConventions.AttributeNetPeerName, this.instrumentedConnection.DataSource); // Or server address
                activity.SetTag(SemanticConventions.AttributeServerAddress, this.instrumentedConnection.DataSource);

                // Specific operation tag
                if (!string.IsNullOrEmpty(operationName))
                {
                    activity.SetTag(SemanticConventions.AttributeDbOperation, operationName);
                }

                if (this.options.SetDbStatementForText && this.CommandType == CommandType.Text && !string.IsNullOrEmpty(this.CommandText))
                {
                    // TODO: Use SqlProcessor from OpenTelemetry.Instrumentation.SqlClient for sanitization if possible and applicable
                    activity.SetTag(SemanticConventions.AttributeDbStatement, this.CommandText);
                }
                else if (this.CommandType == CommandType.StoredProcedure && !string.IsNullOrEmpty(this.CommandText))
                {
                    activity.SetTag(SemanticConventions.AttributeDbStatement, this.CommandText); // For SP, CommandText is SP name
                }

                try
                {
                    this.options.Enrich?.Invoke(activity, this.wrappedCommand);
                }
                catch (Exception)
                {
                    // Log enrichment exception? For now, let's ignore to not break tracing.
                    // AdoNetInstrumentationEventSource.Log.EnrichmentException(ex); // Example, if we add an EventSource
                }
            }
            return activity;
        }

        private void StopActivity(Activity? activity, Exception? exception = null)
        {
            if (activity == null)
            {
                return;
            }

            if (exception != null)
            {
                activity.SetStatus(ActivityStatusCode.Error, exception.Message);
                if (this.options.RecordException)
                {
                    activity.RecordException(exception);
                }
                // TODO: Add Enrich option call: this.options.Enrich?.Invoke(activity, "OnException", exception);
            }
            else
            {
                activity.SetStatus(ActivityStatusCode.Ok);
            }

            activity.Dispose(); // or activity.Stop();
        }

        private string GetDbSystem()
        {
            if (!string.IsNullOrEmpty(this.options.DbSystem))
            {
                return this.options.DbSystem;
            }
            // Basic heuristic, can be expanded
            var connectionType = this.instrumentedConnection.WrappedConnection.GetType().Name;
            if (connectionType.Contains("SqlConnection")) return SemanticConventions.DbSystemMsSql; // "mssql"
            if (connectionType.Contains("NpgsqlConnection")) return SemanticConventions.DbSystemPostgreSql; // "postgresql"
            if (connectionType.Contains("MySqlConnection")) return SemanticConventions.DbSystemMySql; // "mysql"
            if (connectionType.Contains("SqliteConnection")) return SemanticConventions.DbSystemSqlite; // "sqlite"
            return "other"; // Fallback
        }

        // DbCommand Overrides
        public override string CommandText
        {
            get => this.wrappedCommand.CommandText;
            set => this.wrappedCommand.CommandText = value;
        }
        public override int CommandTimeout
        {
            get => this.wrappedCommand.CommandTimeout;
            set => this.wrappedCommand.CommandTimeout = value;
        }
        public override CommandType CommandType
        {
            get => this.wrappedCommand.CommandType;
            set => this.wrappedCommand.CommandType = value;
        }
        protected override DbConnection? DbConnection
        {
            get => this.instrumentedConnection;
            set
            {
                // This setter should ideally not be called with a different connection.
                // If it is, we can't guarantee instrumentation.
                // For now, let's update the wrapped command's connection.
                // A more robust solution might involve disallowing this or re-wrapping.
                if (value is InstrumentedDbConnection newInstrumentedConnection)
                {
                    this.wrappedCommand.Connection = newInstrumentedConnection.WrappedConnection;
                    // this.instrumentedConnection = newInstrumentedConnection; // This would change the parent connection instance
                }
                else
                {
                    this.wrappedCommand.Connection = value;
                }
            }
        }
        protected override DbParameterCollection DbParameterCollection => this.wrappedCommand.Parameters;
        protected override DbTransaction? DbTransaction
        {
            get => this.wrappedCommand.Transaction;
            set => this.wrappedCommand.Transaction = value;
        }
        public override bool DesignTimeVisible
        {
            get => this.wrappedCommand.DesignTimeVisible;
            set => this.wrappedCommand.DesignTimeVisible = value;
        }
        public override UpdateRowSource UpdatedRowSource
        {
            get => this.wrappedCommand.UpdatedRowSource;
            set => this.wrappedCommand.UpdatedRowSource = value;
        }

        public override void Cancel() => this.wrappedCommand.Cancel();
        public override void Prepare() => this.wrappedCommand.Prepare();
        protected override DbParameter CreateDbParameter() => this.wrappedCommand.CreateParameter();

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            var activity = StartActivity(nameof(ExecuteDbDataReader)); // Activity can be null
            try
            {
                var reader = this.wrappedCommand.ExecuteReader(behavior);
                // Pass the activity and options to the reader wrapper
                return new InstrumentedDbDataReader(reader, activity, this.options);
            }
            catch (Exception ex)
            {
                // If an exception occurs before reader is created/returned, stop activity here
                if (activity != null)
                {
                    activity.SetStatus(ActivityStatusCode.Error, ex.Message);
                    if (this.options.RecordException)
                    {
                        activity.RecordException(ex);
                    }
                    // TODO: Enrich on exception
                    activity.Dispose();
                }
                throw;
            }
            // The activity is now managed by InstrumentedDbDataReader and will be stopped upon its Close/Dispose
        }

        public override int ExecuteNonQuery()
        {
            var activity = StartActivity(nameof(ExecuteNonQuery));
            try
            {
                return this.wrappedCommand.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                StopActivity(activity, ex);
                throw;
            }
            finally
            {
                StopActivity(activity);
            }
        }

        public override object? ExecuteScalar()
        {
            var activity = StartActivity(nameof(ExecuteScalar));
            try
            {
                return this.wrappedCommand.ExecuteScalar();
            }
            catch (Exception ex)
            {
                StopActivity(activity, ex);
                throw;
            }
            finally
            {
                StopActivity(activity);
            }
        }

#if NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
        protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)
        {
            var activity = StartActivity(nameof(ExecuteDbDataReaderAsync)); // Activity can be null
            try
            {
                var reader = await this.wrappedCommand.ExecuteReaderAsync(behavior, cancellationToken).ConfigureAwait(false);
                // Pass the activity and options to the reader wrapper
                return new InstrumentedDbDataReader(reader, activity, this.options);
            }
            catch (Exception ex)
            {
                // If an exception occurs before reader is created/returned, stop activity here
                if (activity != null)
                {
                    activity.SetStatus(ActivityStatusCode.Error, ex.Message);
                    if (this.options.RecordException)
                    {
                        activity.RecordException(ex);
                    }
                    // TODO: Enrich on exception
                    activity.Dispose();
                }
                throw;
            }
            // The activity is now managed by InstrumentedDbDataReader and will be stopped upon its Close/Dispose
        }

        public override async Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
        {
            var activity = StartActivity(nameof(ExecuteNonQueryAsync));
            try
            {
                return await this.wrappedCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                StopActivity(activity, ex);
                throw;
            }
            finally
            {
                StopActivity(activity);
            }
        }

        public override async Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken)
        {
            var activity = StartActivity(nameof(ExecuteScalarAsync));
            try
            {
                return await this.wrappedCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                StopActivity(activity, ex);
                throw;
            }
            finally
            {
                StopActivity(activity);
            }
        }

        public override Task PrepareAsync(CancellationToken cancellationToken = default)
        {
            return this.wrappedCommand.PrepareAsync(cancellationToken);
        }
#endif
    }
}
