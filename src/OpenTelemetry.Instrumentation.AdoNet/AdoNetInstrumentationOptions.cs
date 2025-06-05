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
    /// Options for ADO.NET instrumentation.
    /// </summary>
    public class AdoNetInstrumentationOptions
    {
        /// <summary>
        /// Gets or sets a value indicating whether or not the <see cref="DbException"/> from the instrumented command should be recorded as ActivityEvent
        /// to the Activity. Default value is <see langword="false"/>.
        /// </summary>
        public bool RecordException { get; set; } = false;

        /// <summary>
        /// Gets or sets a value indicating whether or not the <see cref="DbCommand.CommandText"/> should be set as the <c>db.statement</c> tag.
        /// Default value is <see langword="true"/>.
        /// </summary>
        public bool SetDbStatementForText { get; set; } = true;

        /// <summary>
        /// Gets or sets an optional string to override the <c>db.system</c> tag. If not set, the instrumentation will attempt to determine it.
        /// </summary>
        public string? DbSystem { get; set; }

        /// <summary>
        /// Gets or sets a filter function that determines whether or not a command should be collected.
        /// </summary>
        /// <remarks>
        /// The filter function will be called before the activity is started.
        /// If the filter returns <see langword="false"/>, the command will not be collected.
        /// </remarks>
        public Func<DbCommand, bool>? Filter { get; set; }

        /// <summary>
        /// Gets or sets an enrichment action that can be used to add custom tags to the activity.
        /// </summary>
        /// <remarks>
        /// The action will be called after the activity is started and basic tags have been added,
        /// but before the command is executed.
        /// Two arguments are passed to the action: the <see cref="Activity"/> and the <see cref="DbCommand"/>.
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
    }
}
