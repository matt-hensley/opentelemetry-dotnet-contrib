// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using OpenTelemetry.Trace; // Required for SemanticConventions and Activity an Status
using OpenTelemetry.Instrumentation.AdoNet.Implementation;

namespace OpenTelemetry.Instrumentation.AdoNet
{
    /// <summary>
    /// A <see cref="DbCommand"/> implementation that wraps an underlying <see cref="DbCommand"/>
    /// and instruments its operations with OpenTelemetry.
    /// </summary>
    internal sealed class InstrumentedDbCommand : DbCommand
    {
        private readonly DbCommand wrappedCommand;
        private readonly InstrumentedDbConnection instrumentedConnection;
        private readonly AdoNetInstrumentationOptions options;

        /// <summary>
        /// Initializes a new instance of the <see cref="InstrumentedDbCommand"/> class.
        /// </summary>
        /// <param name="command">The underlying <see cref="DbCommand"/> to wrap. Must not be null.</param>
        /// <param name="connection">The <see cref="InstrumentedDbConnection"/> that created this command. Must not be null.</param>
        /// <param name="options">The <see cref="AdoNetInstrumentationOptions"/> to use for instrumenting this command. Must not be null.</param>
        public InstrumentedDbCommand(DbCommand command, InstrumentedDbConnection connection, AdoNetInstrumentationOptions options)
        {
            this.wrappedCommand = command ?? throw new ArgumentNullException(nameof(command));
            this.instrumentedConnection = connection ?? throw new ArgumentNullException(nameof(connection));
            this.options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <summary>
        /// Starts an <see cref="Activity"/> for a database command execution.
        /// </summary>
        /// <param name="operationName">The name of the database operation being performed (e.g., "ExecuteNonQuery", "ExecuteReader").</param>
        /// <returns>An <see cref="Activity"/> instance if the command should be instrumented, otherwise null.</returns>
        /// <remarks>
        /// This method applies the <see cref="AdoNetInstrumentationOptions.Filter"/>, if configured.
        /// It sets standard OpenTelemetry semantic convention tags for databases, including db.system, db.name,
        /// net.peer.name, server.address, and db.operation. If <see cref="AdoNetInstrumentationOptions.SetDbStatementForText"/> is true,
        /// it also sets db.statement. The <see cref="AdoNetInstrumentationOptions.Enrich"/> action is called if provided.
        /// </remarks>
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

        /// <summary>
        /// Stops an <see cref="Activity"/>, recording any exception that occurred and setting the activity status.
        /// </summary>
        /// <param name="activity">The <see cref="Activity"/> to stop. Can be null.</param>
        /// <param name="exception">The <see cref="Exception"/> that occurred during the command execution, if any. Can be null.</param>
        /// <remarks>
        /// If an exception is provided, the <see cref="Activity.Status"/> is set to <see cref="ActivityStatusCode.Error"/>.
        /// If <see cref="AdoNetInstrumentationOptions.RecordException"/> is true, the exception is recorded as an <see cref="ActivityEvent"/>.
        /// Otherwise, the <see cref="Activity.Status"/> is set to <see cref="ActivityStatusCode.Ok"/>.
        /// The activity is then disposed.
        /// </remarks>
        private void StopActivity(Activity? activity, Exception? exception)
        {
            if (activity == null) return;

            if (exception != null)
            {
                activity.SetStatus(ActivityStatusCode.Error, exception.Message);
                if (this.options.RecordException)
                {
                    activity.RecordException(exception);
                }
                // TODO: Enrich on exception for activity
            }
            else
            {
                activity.SetStatus(ActivityStatusCode.Ok);
            }
            activity.Dispose();
        }

        private void RecordMetrics(string operationName, long startTimestamp, Exception? exception)
        {
            if (!this.options.EmitMetrics || startTimestamp == 0) return;

            var duration = (Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / Stopwatch.Frequency; // ms

            var tags = new TagList();
            tags.Add(SemanticConventions.AttributeDbSystem, GetDbSystem());
            if (!string.IsNullOrEmpty(this.instrumentedConnection.Database))
                tags.Add(SemanticConventions.AttributeDbName, this.instrumentedConnection.Database);
            if (!string.IsNullOrEmpty(this.instrumentedConnection.DataSource))
                tags.Add(SemanticConventions.AttributeServerAddress, this.instrumentedConnection.DataSource);

            tags.Add(SemanticConventions.AttributeDbOperation, operationName);

            if (exception != null)
            {
                tags.Add(SemanticConventions.AttributeErrorType, exception.GetType().Name);
            }

            InstrumentedDbConnection.DbClientDurationHistogram.Record(duration, tags);
            InstrumentedDbConnection.DbClientCallsCounter.Add(1, tags);
        }

        /// <summary>
        /// Determines the database system string (e.g., "mssql", "postgresql") based on options or connection type.
        /// </summary>
        /// <returns>The database system string.</returns>
        private string GetDbSystem()
        {
            return DbSystemResolver.Resolve(this.instrumentedConnection.WrappedConnection, this.options.DbSystem);
        }

        // DbCommand Overrides
        /// <inheritdoc/>
        public override string CommandText
        {
            get => this.wrappedCommand.CommandText;
            set => this.wrappedCommand.CommandText = value;
        }

        /// <inheritdoc/>
        public override int CommandTimeout
        {
            get => this.wrappedCommand.CommandTimeout;
            set => this.wrappedCommand.CommandTimeout = value;
        }

        /// <inheritdoc/>
        public override CommandType CommandType
        {
            get => this.wrappedCommand.CommandType;
            set => this.wrappedCommand.CommandType = value;
        }

        /// <inheritdoc/>
        protected override DbConnection? DbConnection
        {
            get => this.instrumentedConnection;
            set
            {
                if (value is InstrumentedDbConnection newInstrumentedConnection)
                {
                    this.wrappedCommand.Connection = newInstrumentedConnection.WrappedConnection;
                }
                else
                {
                    this.wrappedCommand.Connection = value;
                }
            }
        }

        /// <inheritdoc/>
        protected override DbParameterCollection DbParameterCollection => this.wrappedCommand.Parameters;

        /// <inheritdoc/>
        protected override DbTransaction? DbTransaction
        {
            get => this.wrappedCommand.Transaction;
            set => this.wrappedCommand.Transaction = value;
        }

        /// <inheritdoc/>
        public override bool DesignTimeVisible
        {
            get => this.wrappedCommand.DesignTimeVisible;
            set => this.wrappedCommand.DesignTimeVisible = value;
        }

        /// <inheritdoc/>
        public override UpdateRowSource UpdatedRowSource
        {
            get => this.wrappedCommand.UpdatedRowSource;
            set => this.wrappedCommand.UpdatedRowSource = value;
        }

        /// <inheritdoc/>
        public override void Cancel() => this.wrappedCommand.Cancel();

        /// <inheritdoc/>
        public override void Prepare() => this.wrappedCommand.Prepare();

        /// <inheritdoc/>
        protected override DbParameter CreateDbParameter() => this.wrappedCommand.CreateParameter();

        /// <inheritdoc/>
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            var operationName = "ExecuteDbDataReader";
            Activity? activity = null;
            long startTimestamp = 0;

            if (this.options.EmitMetrics)
            {
                startTimestamp = Stopwatch.GetTimestamp();
            }
            activity = StartActivity(operationName);

            try
            {
                var reader = this.wrappedCommand.ExecuteReader(behavior);
                RecordMetrics(operationName, startTimestamp, null);
                return new InstrumentedDbDataReader(reader, activity, this.options);
            }
            catch (Exception ex)
            {
                RecordMetrics(operationName, startTimestamp, ex);
                StopActivity(activity, ex);
                throw;
            }
        }

        /// <inheritdoc/>
        public override int ExecuteNonQuery()
        {
            var operationName = "ExecuteNonQuery";
            Activity? activity = null;
            long startTimestamp = 0;

            if (this.options.EmitMetrics)
            {
                startTimestamp = Stopwatch.GetTimestamp();
            }
            activity = StartActivity(operationName);

            try
            {
                var result = this.wrappedCommand.ExecuteNonQuery();
                RecordMetrics(operationName, startTimestamp, null);
                StopActivity(activity, null);
                return result;
            }
            catch (Exception ex)
            {
                RecordMetrics(operationName, startTimestamp, ex);
                StopActivity(activity, ex);
                throw;
            }
        }

        /// <inheritdoc/>
        public override object? ExecuteScalar()
        {
            var operationName = "ExecuteScalar";
            Activity? activity = null;
            long startTimestamp = 0;

            if (this.options.EmitMetrics)
            {
                startTimestamp = Stopwatch.GetTimestamp();
            }
            activity = StartActivity(operationName);

            try
            {
                var result = this.wrappedCommand.ExecuteScalar();
                RecordMetrics(operationName, startTimestamp, null);
                StopActivity(activity, null);
                return result;
            }
            catch (Exception ex)
            {
                RecordMetrics(operationName, startTimestamp, ex);
                StopActivity(activity, ex);
                throw;
            }
        }

#if NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
        /// <inheritdoc/>
        protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)
        {
            var operationName = "ExecuteDbDataReaderAsync";
            Activity? activity = null;
            long startTimestamp = 0;

            if (this.options.EmitMetrics)
            {
                startTimestamp = Stopwatch.GetTimestamp();
            }
            activity = StartActivity(operationName);

            try
            {
                var reader = await this.wrappedCommand.ExecuteReaderAsync(behavior, cancellationToken).ConfigureAwait(false);
                RecordMetrics(operationName, startTimestamp, null);
                return new InstrumentedDbDataReader(reader, activity, this.options);
            }
            catch (Exception ex)
            {
                RecordMetrics(operationName, startTimestamp, ex);
                StopActivity(activity, ex);
                throw;
            }
        }

        /// <inheritdoc/>
        public override async Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
        {
            var operationName = "ExecuteNonQueryAsync";
            Activity? activity = null;
            long startTimestamp = 0;

            if (this.options.EmitMetrics)
            {
                startTimestamp = Stopwatch.GetTimestamp();
            }
            activity = StartActivity(operationName);

            try
            {
                var result = await this.wrappedCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                RecordMetrics(operationName, startTimestamp, null);
                StopActivity(activity, null);
                return result;
            }
            catch (Exception ex)
            {
                RecordMetrics(operationName, startTimestamp, ex);
                StopActivity(activity, ex);
                throw;
            }
        }

        /// <inheritdoc/>
        public override async Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken)
        {
            var operationName = "ExecuteScalarAsync";
            Activity? activity = null;
            long startTimestamp = 0;

            if (this.options.EmitMetrics)
            {
                startTimestamp = Stopwatch.GetTimestamp();
            }
            activity = StartActivity(operationName);

            try
            {
                var result = await this.wrappedCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                RecordMetrics(operationName, startTimestamp, null);
                StopActivity(activity, null);
                return result;
            }
            catch (Exception ex)
            {
                RecordMetrics(operationName, startTimestamp, ex);
                StopActivity(activity, ex);
                throw;
            }
        }

        /// <inheritdoc/>
        public override Task PrepareAsync(CancellationToken cancellationToken = default)
        {
            return this.wrappedCommand.PrepareAsync(cancellationToken);
        }
#endif
    }
}
