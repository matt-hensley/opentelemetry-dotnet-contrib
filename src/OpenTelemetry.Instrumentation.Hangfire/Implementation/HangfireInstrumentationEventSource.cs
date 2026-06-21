// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.Tracing;
using OpenTelemetry.Internal;

namespace OpenTelemetry.Instrumentation.Hangfire.Implementation;

[EventSource(Name = "OpenTelemetry-Instrumentation-Hangfire")]
internal sealed class HangfireInstrumentationEventSource : EventSource
{
    public static readonly HangfireInstrumentationEventSource Log = new();

    [NonEvent]
    public void FilterException(Exception ex)
    {
        if (this.IsEnabled(EventLevel.Error, EventKeywords.All))
        {
            this.FilterException(ex.ToInvariantString());
        }
    }

    [Event(1, Message = "Filter threw exception. Job will not be collected. Exception {0}.", Level = EventLevel.Error)]
    public void FilterException(string exception)
    {
        this.WriteEvent(1, exception);
    }
}
