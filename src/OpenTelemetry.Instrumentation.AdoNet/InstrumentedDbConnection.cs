// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System.Data;
using System.Data.Common;
using System.Diagnostics; // Required for ActivitySource
using OpenTelemetry.Trace; // Required for SemanticConventions
using System.Reflection;

namespace OpenTelemetry.Instrumentation.AdoNet
{
    internal sealed class InstrumentedDbConnection : DbConnection
    {
        internal static readonly AssemblyName AssemblyName = typeof(InstrumentedDbConnection).Assembly.GetName();
        internal static readonly ActivitySource ActivitySource = new ActivitySource(
            AssemblyName.Name ?? "OpenTelemetry.Instrumentation.AdoNet",
            AssemblyName.Version?.ToString() ?? "unknown");

        private readonly DbConnection wrappedConnection;
        private readonly AdoNetInstrumentationOptions options; // Will be added in a later step

        public InstrumentedDbConnection(DbConnection connection, AdoNetInstrumentationOptions? options = null)
        {
            this.wrappedConnection = connection ?? throw new ArgumentNullException(nameof(connection));
            this.options = options ?? new AdoNetInstrumentationOptions(); // Options will be properly defined and used later
        }

        // Abstract members - Implementation
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => this.wrappedConnection.BeginTransaction(isolationLevel);
        public override void ChangeDatabase(string databaseName) => this.wrappedConnection.ChangeDatabase(databaseName);
        public override void Close() => this.wrappedConnection.Close();
        protected override DbCommand CreateDbCommand()
        {
            var command = this.wrappedConnection.CreateCommand();
            // Pass this (InstrumentedDbConnection) and options to InstrumentedDbCommand
            return new InstrumentedDbCommand(command, this, this.options);
        }
        public override void Open() => this.wrappedConnection.Open();
#if NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
        public override async System.Threading.Tasks.Task OpenAsync(System.Threading.CancellationToken cancellationToken)
        {
            await this.wrappedConnection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        protected override async System.Threading.Tasks.ValueTask<DbTransaction> BeginDbTransactionAsync(IsolationLevel isolationLevel, System.Threading.CancellationToken cancellationToken)
        {
            return await this.wrappedConnection.BeginTransactionAsync(isolationLevel, cancellationToken).ConfigureAwait(false);
        }

        public override async System.Threading.Tasks.Task ChangeDatabaseAsync(string databaseName, System.Threading.CancellationToken cancellationToken = default)
        {
            await this.wrappedConnection.ChangeDatabaseAsync(databaseName, cancellationToken).ConfigureAwait(false);
        }

        public override async System.Threading.Tasks.Task CloseAsync()
        {
            await this.wrappedConnection.CloseAsync().ConfigureAwait(false);
        }

        public override async System.Threading.Tasks.ValueTask DisposeAsync()
        {
            await this.wrappedConnection.DisposeAsync().ConfigureAwait(false);
        }
#endif

        // Properties - Delegating to wrappedConnection
        public override string ConnectionString
        {
            get => this.wrappedConnection.ConnectionString;
            set => this.wrappedConnection.ConnectionString = value;
        }

        public override string Database => this.wrappedConnection.Database;
        public override string DataSource => this.wrappedConnection.DataSource;
        public override string ServerVersion => this.wrappedConnection.ServerVersion;
        public override ConnectionState State => this.wrappedConnection.State;
        public override int ConnectionTimeout => this.wrappedConnection.ConnectionTimeout;

        // Other Overridable Members - Delegating
        protected override DbProviderFactory? DbProviderFactory => this.wrappedConnection.GetDbProviderFactory();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.wrappedConnection.Dispose();
            }
            base.Dispose(disposing);
        }

        public override DataTable GetSchema() => this.wrappedConnection.GetSchema();
        public override DataTable GetSchema(string collectionName) => this.wrappedConnection.GetSchema(collectionName);
        public override DataTable GetSchema(string collectionName, string?[] restrictionValues) => this.wrappedConnection.GetSchema(collectionName, restrictionValues);

        // Internal accessor for the original connection, might be needed by InstrumentedDbCommand
        internal DbConnection WrappedConnection => this.wrappedConnection;
    }
}
