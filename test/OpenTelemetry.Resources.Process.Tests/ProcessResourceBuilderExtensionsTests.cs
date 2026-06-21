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
    public void ProcessDetector_PidIsPositive()
    {
        var resource = ResourceBuilder.CreateEmpty().AddProcessDetector().Build();
        var resourceAttributes = resource.Attributes.ToDictionary(x => x.Key, x => x.Value);

        var pid = Assert.IsType<long>(resourceAttributes[ProcessSemanticConventions.AttributeProcessPid]);
        Assert.True(pid > 0, "Process ID should be a positive integer.");
    }

    [Fact]
    public void ProcessDetector_OwnerIsNotEmpty()
    {
        var resource = ResourceBuilder.CreateEmpty().AddProcessDetector().Build();
        var resourceAttributes = resource.Attributes.ToDictionary(x => x.Key, x => x.Value);

        var owner = Assert.IsType<string>(resourceAttributes[ProcessSemanticConventions.AttributeProcessOwner]);
        Assert.NotEmpty(owner);
    }
}
