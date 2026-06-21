// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

namespace OpenTelemetry.Resources.Process.Tests;

public class ProcessResourceBuilderExtensionsTests
{
    [Fact]
    public void AddProcessDetector_NullBuilder_Throws()
    {
        ResourceBuilder builder = null!;
        Assert.Throws<ArgumentNullException>(() => builder.AddProcessDetector());
    }

    [Fact]
    public void AddProcessDetector_ReturnsResourceBuilderInstance()
    {
        var builder = ResourceBuilder.CreateEmpty();
        var result = builder.AddProcessDetector();
        Assert.Same(builder, result);
    }

    [Fact]
    public void ProcessDetector_PidMatchesCurrentProcess()
    {
        var resource = ResourceBuilder.CreateEmpty().AddProcessDetector().Build();
        var resourceAttributes = resource.Attributes.ToDictionary(x => x.Key, x => x.Value);

        using var process = System.Diagnostics.Process.GetCurrentProcess();
        Assert.Equal((long)process.Id, resourceAttributes[ProcessSemanticConventions.AttributeProcessPid]);
    }

    [Fact]
    public void ProcessDetector_OwnerMatchesCurrentUser()
    {
        var resource = ResourceBuilder.CreateEmpty().AddProcessDetector().Build();
        var resourceAttributes = resource.Attributes.ToDictionary(x => x.Key, x => x.Value);

        Assert.Equal(Environment.UserName, resourceAttributes[ProcessSemanticConventions.AttributeProcessOwner]);
    }

    [Fact]
    public void ProcessDetector_CreationTimeIsIso8601Format()
    {
        var resource = ResourceBuilder.CreateEmpty().AddProcessDetector().Build();
        var resourceAttributes = resource.Attributes.ToDictionary(x => x.Key, x => x.Value);

        var creationTime = Assert.IsType<string>(resourceAttributes[ProcessSemanticConventions.AttributeProcessCreationTime]);
        Assert.True(DateTimeOffset.TryParse(creationTime, out _), "Creation time should be parseable as ISO 8601.");
    }

    [Fact]
    public void ProcessDetector_ContainsExpectedAttributeKeys()
    {
        var resource = ResourceBuilder.CreateEmpty().AddProcessDetector().Build();
        var resourceAttributes = resource.Attributes.ToDictionary(x => x.Key, x => x.Value);

        Assert.Contains(ProcessSemanticConventions.AttributeProcessOwner, resourceAttributes.Keys);
        Assert.Contains(ProcessSemanticConventions.AttributeProcessPid, resourceAttributes.Keys);
        Assert.Contains(ProcessSemanticConventions.AttributeProcessCreationTime, resourceAttributes.Keys);
    }
}
