// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using OpenTelemetry.Trace;

namespace OpenTelemetry.Instrumentation.ServiceFabricRemoting;

internal class ServiceFabricRemotingActivitySource
{
    internal static readonly ActivitySource ActivitySource = ActivitySourceFactory.Create<ServiceFabricRemotingActivitySource>(null);
    internal static readonly string ActivitySourceName = ActivitySource.Name;
    internal static readonly string IncomingRequestActivityName = ActivitySourceName + ".IncomingRequest";
    internal static readonly string OutgoingRequestActivityName = ActivitySourceName + ".OutgoingRequest";

    public static ServiceFabricRemotingInstrumentationOptions? Options { get; set; }
}
