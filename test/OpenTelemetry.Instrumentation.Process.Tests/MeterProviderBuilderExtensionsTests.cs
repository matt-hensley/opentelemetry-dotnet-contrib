// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using OpenTelemetry.Metrics;

namespace OpenTelemetry.Instrumentation.Process.Tests;

public class MeterProviderBuilderExtensionsTests
{
    private const int MaxTimeToAllowForFlush = 10000;

    [Fact]
    public void AddProcessInstrumentation_NullBuilder_Throws()
    {
        MeterProviderBuilder builder = null!;
        Assert.Throws<ArgumentNullException>(() => builder.AddProcessInstrumentation());
    }

    [Fact]
    public void ProcessMetrics_MemoryUsageMetricHasCorrectMetadata()
    {
        var exportedItems = new List<Metric>();
        using var meterProvider = Sdk.CreateMeterProviderBuilder()
            .AddProcessInstrumentation()
            .AddInMemoryExporter(exportedItems)
            .Build();

        meterProvider.ForceFlush(MaxTimeToAllowForFlush);

        var metric = exportedItems.First(m => m.Name == "process.memory.usage");
        Assert.Equal("By", metric.Unit);
        Assert.Equal("The amount of physical memory in use.", metric.Description);
    }

    [Fact]
    public void ProcessMetrics_VirtualMemoryMetricHasCorrectMetadata()
    {
        var exportedItems = new List<Metric>();
        using var meterProvider = Sdk.CreateMeterProviderBuilder()
            .AddProcessInstrumentation()
            .AddInMemoryExporter(exportedItems)
            .Build();

        meterProvider.ForceFlush(MaxTimeToAllowForFlush);

        var metric = exportedItems.First(m => m.Name == "process.memory.virtual");
        Assert.Equal("By", metric.Unit);
        Assert.Equal("The amount of committed virtual memory.", metric.Description);
    }

    [Fact]
    public void ProcessMetrics_CpuTimeMetricHasCorrectMetadata()
    {
        var exportedItems = new List<Metric>();
        using var meterProvider = Sdk.CreateMeterProviderBuilder()
            .AddProcessInstrumentation()
            .AddInMemoryExporter(exportedItems)
            .Build();

        meterProvider.ForceFlush(MaxTimeToAllowForFlush);

        var metric = exportedItems.First(m => m.Name == "process.cpu.time");
        Assert.Equal("s", metric.Unit);
        Assert.Equal("Total CPU seconds broken down by different states.", metric.Description);
    }

    [Fact]
    public void ProcessMetrics_ThreadCountMetricHasCorrectMetadata()
    {
        var exportedItems = new List<Metric>();
        using var meterProvider = Sdk.CreateMeterProviderBuilder()
            .AddProcessInstrumentation()
            .AddInMemoryExporter(exportedItems)
            .Build();

        meterProvider.ForceFlush(MaxTimeToAllowForFlush);

        var metric = exportedItems.First(m => m.Name == "process.thread.count");
        Assert.Equal("{thread}", metric.Unit);
        Assert.Equal("Process threads count.", metric.Description);
    }

    [Fact]
    public void ProcessMetrics_MemoryUsageReportsPositiveValues()
    {
        var exportedItems = new List<Metric>();
        using var meterProvider = Sdk.CreateMeterProviderBuilder()
            .AddProcessInstrumentation()
            .AddInMemoryExporter(exportedItems)
            .Build();

        meterProvider.ForceFlush(MaxTimeToAllowForFlush);

        var metric = exportedItems.First(m => m.Name == "process.memory.usage");
        foreach (ref readonly var metricPoint in metric.GetMetricPoints())
        {
            Assert.True(metricPoint.GetSumLong() > 0);
        }
    }

    [Fact]
    public void ProcessMetrics_ThreadCountReportsPositiveValues()
    {
        var exportedItems = new List<Metric>();
        using var meterProvider = Sdk.CreateMeterProviderBuilder()
            .AddProcessInstrumentation()
            .AddInMemoryExporter(exportedItems)
            .Build();

        meterProvider.ForceFlush(MaxTimeToAllowForFlush);

        var metric = exportedItems.First(m => m.Name == "process.thread.count");
        foreach (ref readonly var metricPoint in metric.GetMetricPoints())
        {
            Assert.True(metricPoint.GetSumLong() > 0);
        }
    }

    [Fact]
    public void ProcessMetrics_SemanticConventionsVersionIsSet()
    {
        Assert.NotNull(ProcessMetrics.SemanticConventionsVersion);
        Assert.Equal(new Version(1, 25, 0), ProcessMetrics.SemanticConventionsVersion);
    }
}
