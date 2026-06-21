// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.InteropServices;

namespace OpenTelemetry.Resources.ProcessRuntime.Tests;

public class ProcessRuntimeResourceBuilderExtensionsTests
{
    [Fact]
    public void AddProcessRuntimeDetector_NullBuilder_Throws()
    {
        ResourceBuilder builder = null!;
        Assert.Throws<ArgumentNullException>(() => builder.AddProcessRuntimeDetector());
    }

    [Fact]
    public void AddProcessRuntimeDetector_ReturnsResourceBuilderInstance()
    {
        var builder = ResourceBuilder.CreateEmpty();
        var result = builder.AddProcessRuntimeDetector();
        Assert.Same(builder, result);
    }

    [Fact]
    public void ProcessRuntimeDetector_AllAttributesAreNonEmptyStrings()
    {
        var resource = ResourceBuilder.CreateEmpty().AddProcessRuntimeDetector().Build();
        var resourceAttributes = resource.Attributes.ToDictionary(x => x.Key, x => x.Value);

        var description = Assert.IsType<string>(resourceAttributes[ProcessRuntimeSemanticConventions.AttributeProcessRuntimeDescription]);
        Assert.NotEmpty(description);

        var name = Assert.IsType<string>(resourceAttributes[ProcessRuntimeSemanticConventions.AttributeProcessRuntimeName]);
        Assert.NotEmpty(name);

        var version = Assert.IsType<string>(resourceAttributes[ProcessRuntimeSemanticConventions.AttributeProcessRuntimeVersion]);
        Assert.NotEmpty(version);
    }

    [Fact]
    public void ProcessRuntimeDetector_DescriptionMatchesFrameworkDescription()
    {
        var resource = ResourceBuilder.CreateEmpty().AddProcessRuntimeDetector().Build();
        var resourceAttributes = resource.Attributes.ToDictionary(x => x.Key, x => x.Value);

        Assert.Equal(
            RuntimeInformation.FrameworkDescription,
            resourceAttributes[ProcessRuntimeSemanticConventions.AttributeProcessRuntimeDescription]);
    }

    [Fact]
    public void ProcessRuntimeDetector_VersionMatchesEnvironmentVersion()
    {
        var resource = ResourceBuilder.CreateEmpty().AddProcessRuntimeDetector().Build();
        var resourceAttributes = resource.Attributes.ToDictionary(x => x.Key, x => x.Value);

        var version = Assert.IsType<string>(resourceAttributes[ProcessRuntimeSemanticConventions.AttributeProcessRuntimeVersion]);
#if !NETFRAMEWORK
        Assert.Equal(Environment.Version.ToString(), version);
#endif
    }

    [Fact]
    public void ProcessRuntimeDetector_MultipleInvocationsReturnConsistentResults()
    {
        var resource1 = ResourceBuilder.CreateEmpty().AddProcessRuntimeDetector().Build();
        var resource2 = ResourceBuilder.CreateEmpty().AddProcessRuntimeDetector().Build();

        var attrs1 = resource1.Attributes.ToDictionary(x => x.Key, x => x.Value);
        var attrs2 = resource2.Attributes.ToDictionary(x => x.Key, x => x.Value);

        Assert.Equal(
            attrs1[ProcessRuntimeSemanticConventions.AttributeProcessRuntimeDescription],
            attrs2[ProcessRuntimeSemanticConventions.AttributeProcessRuntimeDescription]);
        Assert.Equal(
            attrs1[ProcessRuntimeSemanticConventions.AttributeProcessRuntimeName],
            attrs2[ProcessRuntimeSemanticConventions.AttributeProcessRuntimeName]);
        Assert.Equal(
            attrs1[ProcessRuntimeSemanticConventions.AttributeProcessRuntimeVersion],
            attrs2[ProcessRuntimeSemanticConventions.AttributeProcessRuntimeVersion]);
    }
}
