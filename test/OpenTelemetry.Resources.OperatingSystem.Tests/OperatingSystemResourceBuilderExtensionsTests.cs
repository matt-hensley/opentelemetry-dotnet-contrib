// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

namespace OpenTelemetry.Resources.OperatingSystem.Tests;

public class OperatingSystemResourceBuilderExtensionsTests
{
    [Fact]
    public void AddOperatingSystemDetector_NullBuilder_Throws()
    {
        ResourceBuilder builder = null!;
        Assert.Throws<ArgumentNullException>(() => builder.AddOperatingSystemDetector());
    }

    [Fact]
    public void AddOperatingSystemDetector_ReturnsResourceBuilderInstance()
    {
        var builder = ResourceBuilder.CreateEmpty();
        var result = builder.AddOperatingSystemDetector();
        Assert.Same(builder, result);
    }

    [Fact]
    public void OperatingSystemDetector_NullOsType_ReturnsEmptyResource()
    {
#if NET
        var detector = new OperatingSystemDetector(null, null, null, null, null);
#else
        var detector = new OperatingSystemDetector(null, null);
#endif
        var resource = detector.Detect();
        Assert.Equal(Resource.Empty, resource);
    }

    [Fact]
    public void OperatingSystemDetector_AlwaysIncludesOsType()
    {
        var resource = ResourceBuilder.CreateEmpty().AddOperatingSystemDetector().Build();
        var resourceAttributes = resource.Attributes.ToDictionary(x => x.Key, x => (string)x.Value);

        Assert.Contains(OperatingSystemSemanticConventions.AttributeOperatingSystemType, resourceAttributes.Keys);
        var osType = resourceAttributes[OperatingSystemSemanticConventions.AttributeOperatingSystemType];
        Assert.Contains(osType, new[]
        {
            OperatingSystemSemanticConventions.OperatingSystemsValues.Windows,
            OperatingSystemSemanticConventions.OperatingSystemsValues.Linux,
            OperatingSystemSemanticConventions.OperatingSystemsValues.Darwin,
        });
    }

    [Fact]
    public void OperatingSystemDetector_AlwaysIncludesDescription()
    {
        var resource = ResourceBuilder.CreateEmpty().AddOperatingSystemDetector().Build();
        var resourceAttributes = resource.Attributes.ToDictionary(x => x.Key, x => (string)x.Value);

        Assert.Contains(OperatingSystemSemanticConventions.AttributeOperatingSystemDescription, resourceAttributes.Keys);
        Assert.NotEmpty(resourceAttributes[OperatingSystemSemanticConventions.AttributeOperatingSystemDescription]);
    }

#if NET
    [Fact]
    public void OperatingSystemDetector_LinuxWithoutBuildId_FallsBackToKernelOsRelease()
    {
        var osReleasePath = "Samples/os-release-no-buildid";
        var kernelPath = "Samples/kernelOsrelease";
        var osDetector = new OperatingSystemDetector(
            OperatingSystemSemanticConventions.OperatingSystemsValues.Linux,
            null,
            kernelPath,
            [osReleasePath],
            null);
        var attributes = osDetector.Detect().Attributes.ToDictionary(x => x.Key, x => (string)x.Value);

        Assert.Equal("Debian GNU/Linux", attributes[OperatingSystemSemanticConventions.AttributeOperatingSystemName]);
        Assert.Equal("12", attributes[OperatingSystemSemanticConventions.AttributeOperatingSystemVersion]);
        Assert.Equal("5.15.0-76-generic", attributes[OperatingSystemSemanticConventions.AttributeOperatingSystemBuildId]);
    }

    [Fact]
    public void OperatingSystemDetector_LinuxWithBuildId_UsesBuildIdFromOsRelease()
    {
        var osReleasePath = "Samples/os-release-with-buildid";
        var kernelPath = "Samples/kernelOsrelease";
        var osDetector = new OperatingSystemDetector(
            OperatingSystemSemanticConventions.OperatingSystemsValues.Linux,
            null,
            kernelPath,
            [osReleasePath],
            null);
        var attributes = osDetector.Detect().Attributes.ToDictionary(x => x.Key, x => (string)x.Value);

        Assert.Equal("Fedora Linux", attributes[OperatingSystemSemanticConventions.AttributeOperatingSystemName]);
        Assert.Equal("39", attributes[OperatingSystemSemanticConventions.AttributeOperatingSystemVersion]);
        Assert.Equal("39.20231001.0", attributes[OperatingSystemSemanticConventions.AttributeOperatingSystemBuildId]);
    }

    [Fact]
    public void OperatingSystemDetector_LinuxWithMissingOsReleasePaths_DoesNotThrow()
    {
        var osDetector = new OperatingSystemDetector(
            OperatingSystemSemanticConventions.OperatingSystemsValues.Linux,
            null,
            "/nonexistent/kernelOsrelease",
            ["/nonexistent/os-release"],
            null);
        var resource = osDetector.Detect();
        var attributes = resource.Attributes.ToDictionary(x => x.Key, x => (string)x.Value);

        Assert.Equal(OperatingSystemSemanticConventions.OperatingSystemsValues.Linux, attributes[OperatingSystemSemanticConventions.AttributeOperatingSystemType]);
    }

    [Fact]
    public void OperatingSystemDetector_MacOSWithMissingPlistPaths_DoesNotThrow()
    {
        var osDetector = new OperatingSystemDetector(
            OperatingSystemSemanticConventions.OperatingSystemsValues.Darwin,
            null,
            null,
            null,
            ["/nonexistent/SystemVersion.plist"]);
        var resource = osDetector.Detect();
        var attributes = resource.Attributes.ToDictionary(x => x.Key, x => (string)x.Value);

        Assert.Equal(OperatingSystemSemanticConventions.OperatingSystemsValues.Darwin, attributes[OperatingSystemSemanticConventions.AttributeOperatingSystemType]);
        Assert.False(attributes.ContainsKey(OperatingSystemSemanticConventions.AttributeOperatingSystemName));
    }
#endif
}
