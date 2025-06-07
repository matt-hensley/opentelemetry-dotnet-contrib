// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using OpenTelemetry.Trace;
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
        /// <param name="displayName">The display name for the activity.</param>
        /// <param name="operationAttributeValue">The value for the db.operation tag.</param>
        /// <returns>An <see cref="Activity"/> instance if the command should be instrumented, otherwise null.</returns>
        private Activity? StartActivity(string displayName, string? operationAttributeValue = null)
        {
            if (this.options.Filter != null && !this.options.Filter(this.wrappedCommand))
            {
                return null;
            }

            var activity = InstrumentedDbConnection.ActivitySource.StartActivity(
                displayName,
                ActivityKind.Client);

            if (activity == null)
            {
                return null;
            }

            if (activity.IsAllDataRequested)
            {
                activity.SetTag(SemanticConventions.AttributeDbSystem, GetDbSystem());
                if(!string.IsNullOrEmpty(this.instrumentedConnection.Database))
                    activity.SetTag(SemanticConventions.AttributeDbName, this.instrumentedConnection.Database);
                if(!string.IsNullOrEmpty(this.instrumentedConnection.DataSource))
                {
                    activity.SetTag(SemanticConventions.AttributeNetPeerName, this.instrumentedConnection.DataSource);
                    activity.SetTag(SemanticConventions.AttributeServerAddress, this.instrumentedConnection.DataSource);
                }

                if (!string.IsNullOrEmpty(operationAttributeValue))
                {
                    activity.SetTag(SemanticConventions.AttributeDbOperation, operationAttributeValue);
                }

                if (this.options.SetDbStatementForText)
                {
                    if (this.CommandType == CommandType.Text && !string.IsNullOrEmpty(this.CommandText))
                    {
                        if (this.options.SanitizeDbStatement) // Check the new option
                        {
                            var sqlStatementInfo = SqlProcessor.GetSanitizedSql(this.CommandText);
                            activity.SetTag(SemanticConventions.AttributeDbStatement, sqlStatementInfo.SanitizedSql);
                        }
                        else
                        {
                            activity.SetTag(SemanticConventions.AttributeDbStatement, this.CommandText); // Use raw text
                        }
                    }
                    else if (this.CommandType == CommandType.StoredProcedure && !string.IsNullOrEmpty(this.CommandText))
                    {
                        // Stored procedure names are not affected by SanitizeDbStatement option.
                        activity.SetTag(SemanticConventions.AttributeDbStatement, this.CommandText);
                        activity.SetTag(SemanticConventions.AttributeDbStoredProcedureName, this.CommandText);
                    }
                }

                try
                {
                    this.options.Enrich?.Invoke(activity, this.wrappedCommand);
                }
                catch (Exception)
                {
                    // AdoNetInstrumentationEventSource.Log.EnrichmentException(ex);
                }
            }
            return activity;
        }

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

            var duration = (Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / Stopwatch.Frequency;

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

        private string GetDbSystem()
        {
            return DbSystemResolver.Resolve(this.instrumentedConnection.WrappedConnection, this.options.DbSystem);
        }

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
            string operationNameForTag;
            string activityDisplayName;

            if (this.CommandType == CommandType.StoredProcedure && !string.IsNullOrEmpty(this.CommandText))
            {
                operationNameForTag = this.CommandText;
                activityDisplayName = !string.IsNullOrEmpty(this.instrumentedConnection.Database)
                    ? $"{this.instrumentedConnection.Database}.{this.CommandText}"
                    : this.CommandText;
            }
            else
            {
                operationNameForTag = "ExecuteDbDataReader";
                activityDisplayName = !string.IsNullOrEmpty(this.instrumentedConnection.Database)
                    ? $"{this.instrumentedConnection.Database}.{operationNameForTag}"
                    : operationNameForTag;
            }

            Activity? activity = null;
            long startTimestamp = 0;

            if (this.options.EmitMetrics) { startTimestamp = Stopwatch.GetTimestamp(); }
            activity = StartActivity(activityDisplayName, operationNameForTag);

            try
            {
                var reader = this.wrappedCommand.ExecuteReader(behavior);
                RecordMetrics(operationNameForTag, startTimestamp, null);
                return new InstrumentedDbDataReader(reader, activity, this.options);
            }
            catch (Exception ex)
            {
                RecordMetrics(operationNameForTag, startTimestamp, ex);
                StopActivity(activity, ex);
                throw;
            }
        }

        public override int ExecuteNonQuery()
        {
            string operationNameForTag;
            string activityDisplayName;

            if (this.CommandType == CommandType.StoredProcedure && !string.IsNullOrEmpty(this.CommandText))
            {
                operationNameForTag = this.CommandText;
                activityDisplayName = !string.IsNullOrEmpty(this.instrumentedConnection.Database)
                    ? $"{this.instrumentedConnection.Database}.{this.CommandText}"
                    : this.CommandText;
            }
            else
            {
                operationNameForTag = "ExecuteNonQuery";
                activityDisplayName = !string.IsNullOrEmpty(this.instrumentedConnection.Database)
                    ? $"{this.instrumentedConnection.Database}.{operationNameForTag}"
                    : operationNameForTag;
            }

            Activity? activity = null;
            long startTimestamp = 0;

            if (this.options.EmitMetrics) { startTimestamp = Stopwatch.GetTimestamp(); }
            activity = StartActivity(activityDisplayName, operationNameForTag);

            try
            {
                var result = this.wrappedCommand.ExecuteNonQuery();
                RecordMetrics(operationNameForTag, startTimestamp, null);
                StopActivity(activity, null);
                return result;
            }
            catch (Exception ex)
            {
                RecordMetrics(operationNameForTag, startTimestamp, ex);
                StopActivity(activity, ex);
                throw;
            }
        }

        public override object? ExecuteScalar()
        {
            string operationNameForTag;
            string activityDisplayName;

            if (this.CommandType == CommandType.StoredProcedure && !string.IsNullOrEmpty(this.CommandText))
            {
                operationNameForTag = this.CommandText;
                activityDisplayName = !string.IsNullOrEmpty(this.instrumentedConnection.Database)
                    ? $"{this.instrumentedConnection.Database}.{this.CommandText}"
                    : this.CommandText;
            }
            else
            {
                operationNameForTag = "ExecuteScalar";
                activityDisplayName = !string.IsNullOrEmpty(this.instrumentedConnection.Database)
                    ? $"{this.instrumentedConnection.Database}.{operationNameForTag}"
                    : operationNameForTag;
            }

            Activity? activity = null;
            long startTimestamp = 0;

            if (this.options.EmitMetrics) { startTimestamp = Stopwatch.GetTimestamp(); }
            activity = StartActivity(activityDisplayName, operationNameForTag);

            try
            {
                var result = this.wrappedCommand.ExecuteScalar();
                RecordMetrics(operationNameForTag, startTimestamp, null);
                StopActivity(activity, null);
                return result;
            }
            catch (Exception ex)
            {
                RecordMetrics(operationNameForTag, startTimestamp, ex);
                StopActivity(activity, ex);
                throw;
            }
        }

#if NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
        protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)
        {
            string operationNameForTag;
            string activityDisplayName;

            if (this.CommandType == CommandType.StoredProcedure && !string.IsNullOrEmpty(this.CommandText))
            {
                operationNameForTag = this.CommandText;
                activityDisplayName = !string.IsNullOrEmpty(this.instrumentedConnection.Database)
                    ? $"{this.instrumentedConnection.Database}.{this.CommandText}"
                    : this.CommandText;
            }
            else
            {
                operationNameForTag = "ExecuteDbDataReaderAsync";
                activityDisplayName = !string.IsNullOrEmpty(this.instrumentedConnection.Database)
                    ? $"{this.instrumentedConnection.Database}.{operationNameForTag}"
                    : operationNameForTag;
            }

            Activity? activity = null;
            long startTimestamp = 0;

            if (this.options.EmitMetrics) { startTimestamp = Stopwatch.GetTimestamp(); }
            activity = StartActivity(activityDisplayName, operationNameForTag);

            try
            {
                var reader = await this.wrappedCommand.ExecuteReaderAsync(behavior, cancellationToken).ConfigureAwait(false);
                RecordMetrics(operationNameForTag, startTimestamp, null);
                return new InstrumentedDbDataReader(reader, activity, this.options);
            }
            catch (Exception ex)
            {
                RecordMetrics(operationNameForTag, startTimestamp, ex);
                StopActivity(activity, ex);
                throw;
            }
        }

        public override async Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
        {
            string operationNameForTag;
            string activityDisplayName;

            if (this.CommandType == CommandType.StoredProcedure && !string.IsNullOrEmpty(this.CommandText))
            {
                operationNameForTag = this.CommandText;
                activityDisplayName = !string.IsNullOrEmpty(this.instrumentedConnection.Database)
                    ? $"{this.instrumentedConnection.Database}.{this.CommandText}"
                    : this.CommandText;
            }
            else
            {
                operationNameForTag = "ExecuteNonQueryAsync";
                activityDisplayName = !string.IsNullOrEmpty(this.instrumentedConnection.Database)
                    ? $"{this.instrumentedConnection.Database}.{operationNameForTag}"
                    : operationNameForTag;
            }

            Activity? activity = null;
            long startTimestamp = 0;

            if (this.options.EmitMetrics) { startTimestamp = Stopwatch.GetTimestamp(); }
            activity = StartActivity(activityDisplayName, operationNameForTag);

            try
            {
                var result = await this.wrappedCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                RecordMetrics(operationNameForTag, startTimestamp, null);
                StopActivity(activity, null);
                return result;
            }
            catch (Exception ex)
            {
                RecordMetrics(operationNameForTag, startTimestamp, ex);
                StopActivity(activity, ex);
                throw;
            }
        }

        public override async Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken)
        {
            string operationNameForTag;
            string activityDisplayName;

            if (this.CommandType == CommandType.StoredProcedure && !string.IsNullOrEmpty(this.CommandText))
            {
                operationNameForTag = this.CommandText;
                activityDisplayName = !string.IsNullOrEmpty(this.instrumentedConnection.Database)
                    ? $"{this.instrumentedConnection.Database}.{this.CommandText}"
                    : this.CommandText;
            }
            else
            {
                operationNameForTag = "ExecuteScalarAsync";
                activityDisplayName = !string.IsNullOrEmpty(this.instrumentedConnection.Database)
                    ? $"{this.instrumentedConnection.Database}.{operationNameForTag}"
                    : operationNameForTag;
            }

            Activity? activity = null;
            long startTimestamp = 0;

            if (this.options.EmitMetrics) { startTimestamp = Stopwatch.GetTimestamp(); }
            activity = StartActivity(activityDisplayName, operationNameForTag);

            try
            {
                var result = await this.wrappedCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                RecordMetrics(operationNameForTag, startTimestamp, null);
                StopActivity(activity, null);
                return result;
            }
            catch (Exception ex)
            {
                RecordMetrics(operationNameForTag, startTimestamp, ex);
                StopActivity(activity, ex);
                throw;
            }
        }

        public override Task PrepareAsync(CancellationToken cancellationToken = default)
        {
            return this.wrappedCommand.PrepareAsync(cancellationToken);
        }
#endif
    }
}
