// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System.Data;
using System.Data.Common;
using System.Diagnostics; // Required for ActivitySource
using OpenTelemetry.Trace; // Required for SemanticConventions
using System.Reflection;

namespace OpenTelemetry.Instrumentation.AdoNet
{
    /// <summary>
    /// A <see cref="DbConnection"/> implementation that wraps an underlying <see cref="DbConnection"/>
    /// and instruments its operations with OpenTelemetry.
    /// </summary>
    internal sealed class InstrumentedDbConnection : DbConnection
    {
        internal static readonly AssemblyName AssemblyName = typeof(InstrumentedDbConnection).Assembly.GetName();
        internal static readonly ActivitySource ActivitySource = new ActivitySource(
            AssemblyName.Name ?? "OpenTelemetry.Instrumentation.AdoNet", // Default to a fixed name if assembly name is null
            AssemblyName.Version?.ToString() ?? "1.0.0"); // Default to a fixed version if assembly version is null

        private readonly DbConnection wrappedConnection;
        private readonly AdoNetInstrumentationOptions options;

        /// <summary>
        /// Initializes a new instance of the <see cref="InstrumentedDbConnection"/> class.
        /// </summary>
        /// <param name="connection">The underlying <see cref="DbConnection"/> to wrap. Must not be null.</param>
        /// <param name="options">The <see cref="AdoNetInstrumentationOptions"/> to use for instrumenting this connection. Must not be null.</param>
        public InstrumentedDbConnection(DbConnection connection, AdoNetInstrumentationOptions options)
        {
            this.wrappedConnection = connection ?? throw new ArgumentNullException(nameof(connection));
            this.options = options ?? throw new ArgumentNullException(nameof(options));
        }

        // Abstract members - Implementation providing instrumentation
        /// <inheritdoc/>
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => this.wrappedConnection.BeginTransaction(isolationLevel);

        /// <inheritdoc/>
        public override void ChangeDatabase(string databaseName) => this.wrappedConnection.ChangeDatabase(databaseName);

        /// <inheritdoc/>
        public override void Close() => this.wrappedConnection.Close();

        /// <inheritdoc/>
        protected override DbCommand CreateDbCommand()
        {
            var command = this.wrappedConnection.CreateCommand();
            // Pass this (InstrumentedDbConnection) and options to InstrumentedDbCommand for further instrumentation.
            return new InstrumentedDbCommand(command, this, this.options);
        }

        /// <inheritdoc/>
        public override void Open() => this.wrappedConnection.Open();

#if NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
        /// <inheritdoc/>
        public override async System.Threading.Tasks.Task OpenAsync(System.Threading.CancellationToken cancellationToken)
        {
            await this.wrappedConnection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        protected override async System.Threading.Tasks.ValueTask<DbTransaction> BeginDbTransactionAsync(IsolationLevel isolationLevel, System.Threading.CancellationToken cancellationToken)
        {
            return await this.wrappedConnection.BeginTransactionAsync(isolationLevel, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public override async System.Threading.Tasks.Task ChangeDatabaseAsync(string databaseName, System.Threading.CancellationToken cancellationToken = default)
        {
            await this.wrappedConnection.ChangeDatabaseAsync(databaseName, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public override async System.Threading.Tasks.Task CloseAsync()
        {
            await this.wrappedConnection.CloseAsync().ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public override async System.Threading.Tasks.ValueTask DisposeAsync()
        {
            await this.wrappedConnection.DisposeAsync().ConfigureAwait(false);
        }
#endif

        // Properties - Delegating to wrappedConnection, no specific instrumentation needed for these properties.
        /// <inheritdoc/>
        public override string ConnectionString
        {
            get => this.wrappedConnection.ConnectionString;
            set => this.wrappedConnection.ConnectionString = value;
        }

        /// <inheritdoc/>
        public override string Database => this.wrappedConnection.Database;

        /// <inheritdoc/>
        public override string DataSource => this.wrappedConnection.DataSource;

        /// <inheritdoc/>
        public override string ServerVersion => this.wrappedConnection.ServerVersion;

        /// <inheritdoc/>
        public override ConnectionState State => this.wrappedConnection.State;

        /// <inheritdoc/>
        public override int ConnectionTimeout => this.wrappedConnection.ConnectionTimeout;

        // Other Overridable Members - Delegating
        /// <inheritdoc/>
        protected override DbProviderFactory? DbProviderFactory => this.wrappedConnection.GetDbProviderFactory();

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.wrappedConnection.Dispose();
            }
            base.Dispose(disposing);
        }

        /// <inheritdoc/>
        public override DataTable GetSchema() => this.wrappedConnection.GetSchema();

        /// <inheritdoc/>
        public override DataTable GetSchema(string collectionName) => this.wrappedConnection.GetSchema(collectionName);

        /// <inheritdoc/>
        public override DataTable GetSchema(string collectionName, string?[] restrictionValues) => this.wrappedConnection.GetSchema(collectionName, restrictionValues);

        /// <summary>
        /// Gets the underlying <see cref="DbConnection"/> that this instance is wrapping.
        /// </summary>
        internal DbConnection WrappedConnection => this.wrappedConnection;
    }
}
