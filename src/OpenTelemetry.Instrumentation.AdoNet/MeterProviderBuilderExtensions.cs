// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using OpenTelemetry.Instrumentation.AdoNet; // For InstrumentedDbConnection.AssemblyName
using OpenTelemetry.Internal; // For Guard

namespace OpenTelemetry.Metrics
{
    /// <summary>
    /// Extension methods to simplify registering ADO.NET instrumentation for metrics.
    /// </summary>
    public static class MeterProviderBuilderExtensions
    {
        /// <summary>
        /// Enables ADO.NET instrumentation for metrics.
        /// </summary>
        /// <remarks>
        /// Note: The behavior of ADO.NET metrics, such as enabling/disabling metrics (`EmitMetrics`)
        /// and other related settings, is controlled by <see cref="AdoNetInstrumentationOptions"/>.
        /// These options are typically configured using the
        /// <c>OpenTelemetry.Trace.TracerProviderBuilderExtensions.AddAdoNetInstrumentation(Action&lt;AdoNetInstrumentationOptions&gt;)</c>
        /// method when setting up tracing. The options configured there will apply to metrics as well.
        /// This method ensures that the Meter used by ADO.NET instrumentation is enabled in the <see cref="MeterProvider"/>.
        /// </remarks>
        /// <param name="builder"><see cref="MeterProviderBuilder"/> being configured.</param>
        /// <returns>The instance of <see cref="MeterProviderBuilder"/> to chain the calls.</returns>
        public static MeterProviderBuilder AddAdoNetInstrumentationMetrics(this MeterProviderBuilder builder)
        {
            Guard.ThrowIfNull(builder);

            // The AdoNetInstrumentationOptions (including EmitMetrics) are configured via
            // the AddAdoNetInstrumentation extension for TracerProviderBuilder, which sets
            // AdoNetInstrumentation.DefaultOptions. This metrics extension method primarily
            // ensures the meter is added to the provider.

            return builder.AddMeter(InstrumentedDbConnection.AssemblyName.Name);
            // Or use: return builder.AddMeter("OpenTelemetry.Instrumentation.AdoNet");
            // Using AssemblyName.Name is consistent with the tracing setup.
        }
    }
}
