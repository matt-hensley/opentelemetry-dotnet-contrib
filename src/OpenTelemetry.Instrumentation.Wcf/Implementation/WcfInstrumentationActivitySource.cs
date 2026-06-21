// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.ServiceModel.Channels;
using OpenTelemetry.Instrumentation.Wcf.Implementation;
using OpenTelemetry.Trace;

namespace OpenTelemetry.Instrumentation.Wcf;

/// <summary>
/// WCF instrumentation.
/// </summary>
internal static class WcfInstrumentationActivitySource
{
    internal static readonly ActivitySource ActivitySource = ActivitySourceFactory.Create(typeof(WcfInstrumentationActivitySource), null);
    internal static readonly string ActivitySourceName = ActivitySource.Name;
    internal static readonly string IncomingRequestActivityName = ActivitySourceName + ".IncomingRequest";
    internal static readonly string OutgoingRequestActivityName = ActivitySourceName + ".OutgoingRequest";
    internal static readonly string UnassociatedExceptionActivityName = ActivitySourceName + ".Exception";

    public static WcfInstrumentationOptions? Options { get; set; }

    public static IEnumerable<string>? MessageHeaderValuesGetter(Message request, string name)
        => TelemetryPropagationReader.Default(request, name);

    public static void MessageHeaderValueSetter(Message request, string name, string value)
        => TelemetryPropagationWriter.Default(request, name, value);
}
