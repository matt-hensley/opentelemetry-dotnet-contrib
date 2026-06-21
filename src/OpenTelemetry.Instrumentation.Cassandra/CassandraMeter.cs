// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.Metrics;
using OpenTelemetry.Metrics;

namespace OpenTelemetry.Instrumentation.Cassandra;

internal static class CassandraMeter
{
    static CassandraMeter()
    {
        Instance = MeterFactory.Create(typeof(CassandraMeter), null);
    }

    public static Meter Instance { get; }
}
