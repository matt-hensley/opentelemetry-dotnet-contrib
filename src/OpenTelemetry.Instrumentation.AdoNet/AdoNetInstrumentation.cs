// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Data.Common;

namespace OpenTelemetry.Instrumentation.AdoNet
{
    /// <summary>
    /// Provides static methods to instrument ADO.NET connections and configure default options.
    /// </summary>
    public static class AdoNetInstrumentation
    {
        internal static AdoNetInstrumentationOptions? DefaultOptions { get; private set; }

        /// <summary>
        /// Configures the default <see cref="AdoNetInstrumentationOptions"/> for ADO.NET instrumentation.
        /// These options will be used when <see cref="InstrumentConnection(DbConnection, AdoNetInstrumentationOptions)"/>
        /// is called without explicit options.
        /// </summary>
        /// <param name="options">The default options to set.</param>
        internal static void SetDefaultOptions(AdoNetInstrumentationOptions options)
        {
            DefaultOptions = options;
        }

        /// <summary>
        /// Wraps an existing <see cref="DbConnection"/> with OpenTelemetry instrumentation.
        /// If <paramref name="options"/> are not provided, default options configured via
        /// <c>TracerProviderBuilder.AddAdoNetInstrumentation()</c> will be used.
        /// </summary>
        /// <param name="connection">The <see cref="DbConnection"/> to instrument.</param>
        /// <param name="options">Optional <see cref="AdoNetInstrumentationOptions"/> to configure the instrumentation.</param>
        /// <returns>An instrumented <see cref="DbConnection"/>.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="connection"/> is null.</exception>
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
