// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using OpenTelemetry.Internal;
using StackExchange.Redis;

namespace OpenTelemetry.Instrumentation.StackExchangeRedis;

/// <summary>
/// StackExchange.Redis instrumentation.
/// </summary>
public sealed class StackExchangeRedisInstrumentation : IDisposable
{
    /// <summary>
    /// Redis instrumentation instance.
    /// </summary>
    public static readonly StackExchangeRedisInstrumentation Instance = new();

    /// <summary>
    /// Provides access to the manager responsible for handling instrumentation handles.
    /// </summary>
    /// <remarks>This field is read-only and initialized with a new instance of <see
    /// cref="InstrumentationHandleManager"/>. It can be used to manage and track instrumentation handles throughout the
    /// application.</remarks>
    internal readonly InstrumentationHandleManager HandleManager = new();

    internal StackExchangeRedisInstrumentation()
    {
    }

    /// <summary>
    /// Gets or sets the tracing options for configuring StackExchange.Redis instrumentation.
    /// </summary>
    public IOptionsMonitor<StackExchangeRedisInstrumentationOptions>? TracingOptions { get; set; }

    internal ConcurrentDictionary<(string Name, IConnectionMultiplexer Connection), StackExchangeRedisConnectionInstrumentation> InstrumentedConnections { get; set; } = [];

    /// <summary>
    /// Adds an <see cref="IConnectionMultiplexer"/> to the instrumentation.
    /// </summary>
    /// <param name="connection"><see cref="IConnectionMultiplexer"/>.</param>
    /// <returns><see cref="IDisposable"/> to cancel the registration.</returns>
    public IDisposable AddConnection(IConnectionMultiplexer connection)
        => this.AddConnection(Options.DefaultName, connection);

    /// <summary>
    /// Adds an <see cref="IConnectionMultiplexer"/> to the instrumentation.
    /// </summary>
    /// <param name="name">Name to use when retrieving options.</param>
    /// <param name="connection"><see cref="IConnectionMultiplexer"/>.</param>
    /// <returns><see cref="IDisposable"/> to cancel the registration.</returns>
    public IDisposable AddConnection(string name, IConnectionMultiplexer connection)
    {
        Guard.ThrowIfNull(name);
        Guard.ThrowIfNull(connection);

        var tracingOptions = this.TracingOptions?.Get(name) ?? new();
        var key = (name, connection);
        var instrumentation = this.InstrumentedConnections.GetOrAdd(key, _ => new StackExchangeRedisConnectionInstrumentation(connection, name, tracingOptions));

#pragma warning disable CA2000 // Dispose objects before losing scope
        var tracingHandle = this.TracingOptions != null ? this.HandleManager.AddTracingHandle() : null;
        var metricsHandle = this.HandleManager.AddMetricHandle();
#pragma warning restore CA2000 // Dispose objects before losing scope

        return new StackExchangeRedisConnectionInstrumentationRegistration(() =>
        {
            if (this.InstrumentedConnections.TryRemove(key, out _))
            {
                instrumentation.Dispose();
                tracingHandle?.Dispose();
                metricsHandle?.Dispose();
            }
        });
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (var instrumentation in this.InstrumentedConnections.Values)
        {
            instrumentation.Dispose();
        }

        this.InstrumentedConnections.Clear();
    }

    private sealed class StackExchangeRedisConnectionInstrumentationRegistration : IDisposable
    {
        private readonly Action disposalAction;

        public StackExchangeRedisConnectionInstrumentationRegistration(
            Action disposalAction)
        {
            this.disposalAction = disposalAction;
        }

        public void Dispose()
        {
            this.disposalAction();
        }
    }
}
