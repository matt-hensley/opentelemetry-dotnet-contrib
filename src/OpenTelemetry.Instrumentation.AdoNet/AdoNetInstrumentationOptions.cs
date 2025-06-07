// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Data.Common;
using System.Diagnostics;
#if NETFRAMEWORK
using System.Diagnostics.CodeAnalysis;
#endif

namespace OpenTelemetry.Instrumentation.AdoNet
{
    /// <summary>
    /// Options for configuring ADO.NET instrumentation.
    /// </summary>
    public class AdoNetInstrumentationOptions
    {
        /// <summary>
        /// Gets or sets a value indicating whether an <see cref="Activity"/> should be created for commands that fail.
        /// Default value is <see langword="false"/>.
        /// When set to <see langword="true"/>, if a <see cref="DbException"/> occurs during command execution,
        /// it will be recorded as an <see cref="ActivityEvent"/> on the <see cref="Activity"/> and the
        /// <see cref="Activity.Status"/> will be set to <see cref="ActivityStatusCode.Error"/>.
        /// Regardless of this setting, the status of the <see cref="Activity"/> will be set to <see cref="ActivityStatusCode.Error"/>
        /// if an exception occurs.
        /// </summary>
        public bool RecordException { get; set; } = false;

        /// <summary>
        /// Gets or sets a value indicating whether the <see cref="DbCommand.CommandText"/> should be collected as the <c>db.statement</c> semantic attribute.
        /// Default value is <see langword="true"/>.
        /// </summary>
        /// <remarks>
        /// The command text can contain sensitive information and increase telemetry cardinality. It is recommended to only enable this if necessary.
        /// This option applies to commands with <see cref="CommandType.Text"/> and <see cref="CommandType.StoredProcedure"/>.
        /// </remarks>
        public bool SetDbStatementForText { get; set; } = true;

        /// <summary>
        /// Gets or sets a string that overrides the <c>db.system</c> semantic attribute.
        /// </summary>
        /// <remarks>
        /// If not set, the instrumentation will attempt to determine the database system (e.g., "mssql", "postgresql", "sqlite")
        /// based on the type name of the <see cref="DbConnection"/>.
        /// Setting this property provides an explicit value for the <see href="https://opentelemetry.io/docs/specs/semconv/database/database-spans/#connection-level-attributes">db.system</see> tag.
        /// </remarks>
        public string? DbSystem { get; set; }

        /// <summary>
        /// Gets or sets a filter function that determines whether a <see cref="DbCommand"/> should be instrumented.
        /// </summary>
        /// <remarks>
        /// The filter function is called before an <see cref="Activity"/> is started for a command.
        /// If the filter returns <see langword="false"/>, the command will not be instrumented, and no <see cref="Activity"/> will be created.
        /// If the filter returns <see langword="true"/>, the command will be instrumented.
        /// The <see cref="DbCommand"/> instance is passed as an argument to the filter function.
        /// </remarks>
        public Func<DbCommand, bool>? Filter { get; set; }

        /// <summary>
        /// Gets or sets an enrichment action that allows adding custom tags to an <see cref="Activity"/> created for a <see cref="DbCommand"/>.
        /// </summary>
        /// <remarks>
        /// The enrichment action is called after the <see cref="Activity"/> has been created and basic attributes have been added,
        /// but before the command is executed. This allows for adding custom information to the telemetry.
        /// The <see cref="Activity"/> and the <see cref="DbCommand"/> are passed as arguments to the action.
        /// </remarks>
#if NETFRAMEWORK
        [UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "The AdoNet library is not trim-safe yet.")]
#else
        // For .NET Core and .NET 5+, UnconditionalSuppressMessage is not needed for this specific scenario as often,
        // but if specific DbCommand types are trimmed, it could still be an issue.
        // If users are AOT trimming heavily and using providers with non-obvious DbCommand types, they might hit issues.
        // For now, we'll assume standard provider patterns.
#endif
        public Action<Activity, DbCommand>? Enrich { get; set; }

        // Consider adding EnrichWithObject for more complex scenarios or different event points like "OnException"
        // public Action<Activity, string, object>? EnrichWithObject { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether ADO.NET client metrics should be collected.
        /// Default value is <see langword="true"/>.
        /// </summary>
        public bool EmitMetrics { get; set; } = true;
    }
}
