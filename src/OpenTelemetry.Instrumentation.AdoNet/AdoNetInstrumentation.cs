// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Data.Common;

namespace OpenTelemetry.Instrumentation.AdoNet
{
    /// <summary>
    /// Provides static methods to instrument ADO.NET <see cref="DbConnection"/> instances.
    /// </summary>
    public static class AdoNetInstrumentation
    {
        /// <summary>
        /// Gets the globally configured default <see cref="AdoNetInstrumentationOptions"/>.
        /// These options are typically set via <see cref="Otel.TracerProviderBuilderExtensions.AddAdoNetInstrumentation(Otel.TracerProviderBuilder, Action{AdoNetInstrumentationOptions})"/>.
        /// </summary>
        internal static AdoNetInstrumentationOptions? DefaultOptions { get; private set; }

        /// <summary>
        /// Sets the default <see cref="AdoNetInstrumentationOptions"/> to be used when calling
        /// <see cref="InstrumentConnection(DbConnection, AdoNetInstrumentationOptions)"/>
        /// without explicitly providing options. This method is typically called by the
        /// <see cref="Otel.TracerProviderBuilderExtensions.AddAdoNetInstrumentation(Otel.TracerProviderBuilder, Action{AdoNetInstrumentationOptions})"/> extension.
        /// </summary>
        /// <param name="options">The <see cref="AdoNetInstrumentationOptions"/> to use as default.</param>
        internal static void SetDefaultOptions(AdoNetInstrumentationOptions options)
        {
            DefaultOptions = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <summary>
        /// Wraps the given <see cref="DbConnection"/> with OpenTelemetry instrumentation.
        /// </summary>
        /// <remarks>
        /// The returned <see cref="DbConnection"/> will be an instrumented wrapper around the original <paramref name="connection"/>.
        /// All operations performed on the instrumented connection that result in database calls (e.g., <see cref="DbCommand.ExecuteNonQuery()"/>,
        /// <see cref="DbCommand.ExecuteReader()"/>) will create <see cref="System.Diagnostics.Activity"/> instances and optionally record metrics.
        ///
        /// Options precedence for instrumentation is as follows:
        /// 1. If <paramref name="options"/> are provided to this method, they are used.
        /// 2. If <paramref name="options"/> are null, the static <see cref="DefaultOptions"/> are used. These are typically configured by
        ///    <see cref="Otel.TracerProviderBuilderExtensions.AddAdoNetInstrumentation(Otel.TracerProviderBuilder, Action{AdoNetInstrumentationOptions})"/>.
        /// 3. If <see cref="DefaultOptions"/> are also null (e.g., if `AddAdoNetInstrumentation` was not called or was called without an options delegate),
        ///    a new instance of <see cref="AdoNetInstrumentationOptions"/> with default values is used.
        ///
        /// It is the caller's responsibility to manage the lifetime of the returned <see cref="DbConnection"/>, including opening, closing, and disposing it.
        /// </remarks>
        /// <param name="connection">The <see cref="DbConnection"/> to instrument. Must not be null.</param>
        /// <param name="options">
        /// Optional <see cref="AdoNetInstrumentationOptions"/> to configure the behavior of the instrumentation for this specific connection.
        /// If null, <see cref="DefaultOptions"/> will be used if previously configured, otherwise default options will be applied.
        /// </param>
        /// <returns>An instrumented <see cref="DbConnection"/> that wraps the original connection.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="connection"/> is <see langword="null"/>.</exception>
        public static DbConnection InstrumentConnection(DbConnection connection, AdoNetInstrumentationOptions? options = null)
        {
            if (connection == null)
            {
                throw new ArgumentNullException(nameof(connection));
            }

            var instrumentationOptions = options ?? DefaultOptions ?? new AdoNetInstrumentationOptions();

            return new InstrumentedDbConnection(connection, instrumentationOptions);
        }
    }
}
