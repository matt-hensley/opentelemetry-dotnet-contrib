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
        /// Enables ADO.NET data provider instrumentation for tracing.
        /// </summary>
        /// <remarks>
        /// This method registers the ADO.NET instrumentation's <see cref="System.Diagnostics.ActivitySource"/> with the <see cref="TracerProviderBuilder"/>.
        /// It ensures that activities created by instrumented ADO.NET connections are processed by the OpenTelemetry SDK.
        /// This overload does not configure any <see cref="AdoNetInstrumentationOptions"/>. If options need to be configured (e.g., for trace enrichment,
        /// filtering, or controlling metric emission), use the overload <see cref="AddAdoNetInstrumentation(TracerProviderBuilder, Action{AdoNetInstrumentationOptions})"/>
        /// or configure options via Dependency Injection using <c>IServiceCollection.ConfigureAdoNetInstrumentation</c>.
        /// Connections still need to be manually wrapped using
        /// <see cref="AdoNetInstrumentation.InstrumentConnection(System.Data.Common.DbConnection, AdoNetInstrumentationOptions)"/> for instrumentation to occur.
        /// </remarks>
        /// <param name="builder"><see cref="TracerProviderBuilder"/> being configured.</param>
        /// <returns>The instance of <see cref="TracerProviderBuilder"/> to chain calls.</returns>
        public static TracerProviderBuilder AddAdoNetInstrumentation(this TracerProviderBuilder builder)
        {
            return AddAdoNetInstrumentation(builder, configure: null);
        }

        /// <summary>
        /// Enables ADO.NET data provider instrumentation for tracing with custom configuration.
        /// </summary>
        /// <remarks>
        /// This method registers the ADO.NET instrumentation's <see cref="System.Diagnostics.ActivitySource"/> with the <see cref="TracerProviderBuilder"/>
        /// and configures the static <see cref="AdoNetInstrumentation.DefaultOptions"/>. These <see cref="AdoNetInstrumentation.DefaultOptions"/>
        /// are used as a fallback when <see cref="AdoNetInstrumentation.InstrumentConnection(System.Data.Common.DbConnection, AdoNetInstrumentationOptions)"/>
        /// is called without explicit options, or when options are not resolved from Dependency Injection.
        /// If options are configured via Dependency Injection (e.g., using <c>IServiceCollection.ConfigureAdoNetInstrumentation</c>),
        /// those DI-configured options will typically take precedence when <see cref="AdoNetInstrumentation.InstrumentConnection(System.Data.Common.DbConnection, AdoNetInstrumentationOptions)"/>
        /// is used within a DI scope or if <see cref="Otel.Instrumentation.AdoNet.InstrumentedDbProviderFactory"/> is resolved from DI.
        /// Connections still need to be manually wrapped using
        /// <see cref="AdoNetInstrumentation.InstrumentConnection(System.Data.Common.DbConnection, AdoNetInstrumentationOptions)"/> for instrumentation to occur.
        /// </remarks>
        /// <param name="builder"><see cref="TracerProviderBuilder"/> being configured.</param>
        /// <param name="configure">A callback action to configure the <see cref="AdoNetInstrumentationOptions"/>. These options are set as the
        /// static <see cref="AdoNetInstrumentation.DefaultOptions"/>.</param>
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
