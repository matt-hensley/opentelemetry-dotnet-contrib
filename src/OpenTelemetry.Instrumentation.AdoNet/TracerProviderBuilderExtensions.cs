// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System;
using OpenTelemetry.Instrumentation.AdoNet;
using OpenTelemetry.Internal; // For Guard

namespace OpenTelemetry.Trace
{
    /// <summary>
    /// Extension methods to simplify registering ADO.NET instrumentation.
    /// </summary>
    public static class TracerProviderBuilderExtensions
    {
        /// <summary>
        /// Enables ADO.NET instrumentation.
        /// </summary>
        /// <remarks>
        /// This method configures default options for ADO.NET instrumentation.
        /// Connections must still be manually wrapped using
        /// <see cref="AdoNetInstrumentation.InstrumentConnection(System.Data.Common.DbConnection, AdoNetInstrumentationOptions)"/>
        /// for instrumentation to occur. The configured options will be applied to such manually instrumented connections
        /// if no explicit options are provided to the <c>InstrumentConnection</c> method.
        /// </remarks>
        /// <param name="builder"><see cref="TracerProviderBuilder"/> being configured.</param>
        /// <returns>The instance of <see cref="TracerProviderBuilder"/> to chain the calls.</returns>
        public static TracerProviderBuilder AddAdoNetInstrumentation(this TracerProviderBuilder builder)
        {
            return AddAdoNetInstrumentation(builder, configure: null);
        }

        /// <summary>
        /// Enables ADO.NET instrumentation.
        /// </summary>
        /// <remarks>
        /// This method configures specified options for ADO.NET instrumentation.
        /// Connections must still be manually wrapped using
        /// <see cref="AdoNetInstrumentation.InstrumentConnection(System.Data.Common.DbConnection, AdoNetInstrumentationOptions)"/>
        /// for instrumentation to occur. The configured options will be applied to such manually instrumented connections
        /// if no explicit options are provided to the <c>InstrumentConnection</c> method.
        /// </remarks>
        /// <param name="builder"><see cref="TracerProviderBuilder"/> being configured.</param>
        /// <param name="configure">Callback action to configure the <see cref="AdoNetInstrumentationOptions"/>.</param>
        /// <returns>The instance of <see cref="TracerProviderBuilder"/> to chain the calls.</returns>
        public static TracerProviderBuilder AddAdoNetInstrumentation(
            this TracerProviderBuilder builder,
            Action<AdoNetInstrumentationOptions>? configure)
        {
            Guard.ThrowIfNull(builder);

            var options = new AdoNetInstrumentationOptions();
            configure?.Invoke(options);

            AdoNetInstrumentation.SetDefaultOptions(options);

            // Adds the ActivitySource name to the list of sources listened to by the TracerProvider
            return builder.AddSource(InstrumentedDbConnection.AssemblyName.Name);
            // Could also use: return builder.AddSource("OpenTelemetry.Instrumentation.AdoNet");
            // but using AssemblyName.Name is more robust if it ever changes.
        }
    }
}
