// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

#if NET
using System.Runtime.InteropServices;
#endif

namespace OpenTelemetry.Resources.Host.Tests;

public class HostResourceBuilderExtensionsTests
{
    [Fact]
    public void AddHostDetector_NullBuilder_Throws()
    {
        ResourceBuilder builder = null!;
        Assert.Throws<ArgumentNullException>(() => builder.AddHostDetector());
    }

    [Fact]
    public void AddHostDetector_ReturnsResourceBuilderInstance()
    {
        var builder = ResourceBuilder.CreateEmpty();
        var result = builder.AddHostDetector();
        Assert.Same(builder, result);
    }

    [Fact]
    public void HostDetector_HostNameIsNotEmpty()
    {
        var resource = ResourceBuilder.CreateEmpty().AddHostDetector().Build();
        var resourceAttributes = resource.Attributes.ToDictionary(x => x.Key, x => (string)x.Value);

        Assert.Equal(Environment.MachineName, resourceAttributes[HostSemanticConventions.AttributeHostName]);
    }

#if NET
    [Fact]
    public void HostDetector_NullMachineId_DoesNotIncludeHostId()
    {
        var detector = new HostDetector(
            _ => false,
            () => [],
            () => null,
            () => null);
        var resource = ResourceBuilder.CreateEmpty().AddDetector(detector).Build();
        var resourceAttributes = resource.Attributes.ToDictionary(x => x.Key, x => (string)x.Value);

        Assert.False(resourceAttributes.ContainsKey(HostSemanticConventions.AttributeHostId));
    }

    [Fact]
    public void HostDetector_EmptyMachineId_DoesNotIncludeHostId()
    {
        var detector = new HostDetector(
            _ => false,
            () => [],
            () => string.Empty,
            () => string.Empty);
        var resource = ResourceBuilder.CreateEmpty().AddDetector(detector).Build();
        var resourceAttributes = resource.Attributes.ToDictionary(x => x.Key, x => (string)x.Value);

        Assert.False(resourceAttributes.ContainsKey(HostSemanticConventions.AttributeHostId));
    }

    [Theory]
    [InlineData(Architecture.X86, "x86")]
    [InlineData(Architecture.X64, "amd64")]
    [InlineData(Architecture.Arm, "arm32")]
    [InlineData(Architecture.Arm64, "arm64")]
    [InlineData(Architecture.S390x, "s390x")]
    [InlineData(Architecture.Armv6, "arm32")]
    [InlineData(Architecture.Ppc64le, "ppc64")]
    [InlineData(Architecture.Wasm, null)]
    [InlineData(Architecture.LoongArch64, null)]
    public void MapArchitectureToOtel_ReturnsExpectedValue(Architecture arch, string? expected)
    {
        var result = HostDetector.MapArchitectureToOtel(arch);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ParseMacOsOutput_NullInput_ReturnsNull()
    {
        var result = HostDetector.ParseMacOsOutput(null);
        Assert.Null(result);
    }

    [Fact]
    public void ParseMacOsOutput_EmptyInput_ReturnsNull()
    {
        var result = HostDetector.ParseMacOsOutput(string.Empty);
        Assert.Null(result);
    }

    [Fact]
    public void ParseMacOsOutput_NoUuidLine_ReturnsNull()
    {
        var result = HostDetector.ParseMacOsOutput("some random output without UUID");
        Assert.Null(result);
    }

    [Fact]
    public void ParseMacOsOutput_MalformedUuidLine_ReturnsNull()
    {
        var result = HostDetector.ParseMacOsOutput("\"IOPlatformUUID\" = malformed");
        Assert.Null(result);
    }

    [Fact]
    public void HostDetector_LinuxWithNonexistentPaths_DoesNotIncludeHostId()
    {
        var detector = new HostDetector(
            osPlatform => osPlatform == OSPlatform.Linux,
            () => ["/nonexistent/path/machine-id"],
            () => throw new Exception("should not be called"),
            () => throw new Exception("should not be called"));
        var resource = ResourceBuilder.CreateEmpty().AddDetector(detector).Build();
        var resourceAttributes = resource.Attributes.ToDictionary(x => x.Key, x => (string)x.Value);

        Assert.False(resourceAttributes.ContainsKey(HostSemanticConventions.AttributeHostId));
    }
#endif
}
