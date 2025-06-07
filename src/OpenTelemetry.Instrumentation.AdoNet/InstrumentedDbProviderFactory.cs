// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Data.Common;
using Microsoft.Extensions.Options; // Required for IOptions / IOptionsMonitor

namespace OpenTelemetry.Instrumentation.AdoNet
{
    /// <summary>
    /// A <see cref="DbProviderFactory"/> that wraps another <see cref="DbProviderFactory"/>
    /// to ensure that any <see cref="DbConnection"/> objects it creates are instrumented
    /// with OpenTelemetry.
    /// </summary>
    public sealed class InstrumentedDbProviderFactory : DbProviderFactory, IDisposable
    {
        private readonly DbProviderFactory _underlyingFactory;
        private readonly AdoNetInstrumentationOptions _options;

        /// <summary>
        /// Initializes a new instance of the <see cref="InstrumentedDbProviderFactory"/> class.
        /// </summary>
        /// <param name="underlyingFactory">The original <see cref="DbProviderFactory"/> to be wrapped.</param>
        /// <param name="options">The <see cref="AdoNetInstrumentationOptions"/> to use for instrumenting connections.
        /// This is typically resolved from DI using <see cref="IOptions{AdoNetInstrumentationOptions}"/> or <see cref="IOptionsMonitor{AdoNetInstrumentationOptions}"/>.</param>
        public InstrumentedDbProviderFactory(DbProviderFactory underlyingFactory, AdoNetInstrumentationOptions options)
        {
            this._underlyingFactory = underlyingFactory ?? throw new ArgumentNullException(nameof(underlyingFactory));
            this._options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <summary>
        /// Creates and returns an instrumented <see cref="DbConnection"/> object.
        /// </summary>
        /// <returns>A new instrumented <see cref="DbConnection"/> object, or <see langword="null"/> if the underlying factory returns <see langword="null"/>.</returns>
        public override DbConnection? CreateConnection()
        {
            var connection = this._underlyingFactory.CreateConnection();
            if (connection == null)
            {
                return null;
            }
            return AdoNetInstrumentation.InstrumentConnection(connection, this._options);
        }

        // Delegate all other members to the underlying factory.
        // This is verbose but necessary to fully implement the abstract DbProviderFactory
        // and correctly proxy behavior.

#if NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
        /// <inheritdoc/>
        public override DbBatch? CreateBatch() => this._underlyingFactory.CreateBatch();

        /// <inheritdoc/>
        public override DbBatchCommand? CreateBatchCommand() => this._underlyingFactory.CreateBatchCommand();
#endif

        /// <inheritdoc/>
        public override DbCommand? CreateCommand()
        {
            // Note: Commands created directly from the factory are not automatically associated with an instrumented connection here.
            // If a command is created and then its .Connection property is set to an InstrumentedDbConnection,
            // our InstrumentedDbCommand will take over when that connection is used by the command.
            // However, if the user does factory.CreateCommand() then cmd.Connection = nonInstrumentedConnection,
            // then AdoNetInstrumentation.InstrumentConnection(cmd.Connection).CreateCommand() was never called.
            // This is standard ADO.NET behavior. The primary instrumentation point is the connection.
            return this._underlyingFactory.CreateCommand();
        }

        /// <inheritdoc/>
        public override DbConnectionStringBuilder? CreateConnectionStringBuilder() => this._underlyingFactory.CreateConnectionStringBuilder();

        /// <inheritdoc/>
        public override DbParameter? CreateParameter() => this._underlyingFactory.CreateParameter();

        /// <inheritdoc/>
        public override bool CanCreateBatch => this._underlyingFactory.CanCreateBatch;

        /// <inheritdoc/>
        public override DbCommandBuilder? CreateCommandBuilder() => this._underlyingFactory.CreateCommandBuilder();

        /// <inheritdoc/>
        public override DbDataAdapter? CreateDataAdapter() => this._underlyingFactory.CreateDataAdapter();

#if NETFRAMEWORK || NETSTANDARD2_0 // CreateDataSourceEnumerator is obsolete on newer TFMs
        /// <inheritdoc/>
        public override System.Security.CodeAccessPermission? CreatePermission(System.Security.Permissions.PermissionState state) => this._underlyingFactory.CreatePermission(state);

        /// <inheritdoc/>
        [Obsolete("CreateDataSourceEnumerator is obsolete and not supported. Use the DbProviderFactories.GetFactoryClasses method to get a list of available providers.", error: true)]
        public override DbDataSourceEnumerator? CreateDataSourceEnumerator() => this._underlyingFactory.CreateDataSourceEnumerator();
#endif


        // IDisposable implementation if _underlyingFactory is IDisposable
        // or if this factory itself holds disposable resources.
        // DbProviderFactory itself does not implement IDisposable.
        // However, if _underlyingFactory could be something that needs disposal,
        // we might consider it. For now, let's assume it doesn't.
        // Adding IDisposable for symmetry or future use.
        private bool _disposed = false;
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (this._disposed)
            {
                return;
            }

            if (disposing)
            {
                // If _underlyingFactory implemented IDisposable, we might call it here.
                // Example: (_underlyingFactory as IDisposable)?.Dispose();
            }

            this._disposed = true;
        }
    }
}
