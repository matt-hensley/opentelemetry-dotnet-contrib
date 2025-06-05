// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System;
using OpenTelemetry.Instrumentation.AdoNet;
using OpenTelemetry.Internal; // For Guard

namespace OpenTelemetry.Trace
{
    /// <summary>
    /// Extension methods to simplify registration of ADO.NET instrumentation.
    /// </summary>
    public static class TracerProviderBuilderExtensions
    {
        /// <summary>
        /// Enables ADO.NET data provider instrumentation.
        /// </summary>
        /// <remarks>
        /// This method establishes default <see cref="AdoNetInstrumentationOptions"/> for all <see cref="System.Data.Common.DbConnection"/>
        /// instances instrumented via <see cref="AdoNetInstrumentation.InstrumentConnection(System.Data.Common.DbConnection, AdoNetInstrumentationOptions)"/>.
        /// These default options are used when no specific options are provided to the <c>InstrumentConnection</c> method.
        /// Note that ADO.NET instrumentation still requires connections to be manually wrapped using
        /// <see cref="AdoNetInstrumentation.InstrumentConnection(System.Data.Common.DbConnection, AdoNetInstrumentationOptions)"/>.
        /// This setup method primarily configures the shared <see cref="ActivitySource"/> and default behaviors.
        /// </remarks>
        /// <param name="builder"><see cref="TracerProviderBuilder"/> being configured.</param>
        /// <returns>The instance of <see cref="TracerProviderBuilder"/> to chain calls.</returns>
        public static TracerProviderBuilder AddAdoNetInstrumentation(this TracerProviderBuilder builder)
        {
            return AddAdoNetInstrumentation(builder, configure: null);
        }

        /// <summary>
        /// Enables ADO.NET data provider instrumentation with custom configuration.
        /// </summary>
        /// <remarks>
        /// This method establishes specified <see cref="AdoNetInstrumentationOptions"/> as defaults for all <see cref="System.Data.Common.DbConnection"/>
        /// instances instrumented via <see cref="AdoNetInstrumentation.InstrumentConnection(System.Data.Common.DbConnection, AdoNetInstrumentationOptions)"/>.
        /// These options are used when no specific options are provided to the <c>InstrumentConnection</c> method.
        /// Note that ADO.NET instrumentation still requires connections to be manually wrapped using
        /// <see cref="AdoNetInstrumentation.InstrumentConnection(System.Data.Common.DbConnection, AdoNetInstrumentationOptions)"/>.
        /// This setup method primarily configures the shared <see cref="ActivitySource"/> and default behaviors.
        /// </remarks>
        /// <param name="builder"><see cref="TracerProviderBuilder"/> being configured.</param>
        /// <param name="configure">A callback action to configure the <see cref="AdoNetInstrumentationOptions"/>.</param>
        /// <returns>The instance of <see cref="TracerProviderBuilder"/> to chain calls.</returns>
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
