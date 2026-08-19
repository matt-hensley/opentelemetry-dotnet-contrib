// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using OpenTelemetry.Instrumentation.Http.Implementation;

namespace OpenTelemetry.Instrumentation.Http.Tests;

public sealed class TelemetryHelperTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(999)]
    public void GetBoxedStatusCodeReturnsBoxedIntegerForOutOfRangeStatusCodes(int statusCode)
    {
        var result = TelemetryHelper.GetBoxedStatusCode((HttpStatusCode)statusCode);

        Assert.IsType<int>(result);
        Assert.Equal(statusCode, result);
    }
}
