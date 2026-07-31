// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.Tracing;
using OpenTelemetry.Internal;

namespace OpenTelemetry.Instrumentation.AWSLambda.Implementation;

[EventSource(Name = "OpenTelemetry-Instrumentation-AWSLambda")]
internal sealed class AWSLambdaInstrumentationEventSource : EventSource
{
    public static readonly AWSLambdaInstrumentationEventSource Log = new();

    [NonEvent]
    public void FailedToDeserializeSqsBody(Exception ex)
    {
        if (this.IsEnabled(EventLevel.Warning, EventKeywords.All))
        {
            this.FailedToDeserializeSqsBody(ex.ToInvariantString());
        }
    }

    [Event(1, Message = "Failed to deserialize SNS message from SQS body. Exception {0}.", Level = EventLevel.Warning)]
    public void FailedToDeserializeSqsBody(string exception)
    {
        this.WriteEvent(1, exception);
    }
}
