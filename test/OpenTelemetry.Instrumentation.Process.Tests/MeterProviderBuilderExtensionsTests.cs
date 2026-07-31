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
    public void ProcessMetrics_VirtualMemoryReportsPositiveValues()
    {
        var exportedItems = new List<Metric>();
        using var meterProvider = Sdk.CreateMeterProviderBuilder()
            .AddProcessInstrumentation()
            .AddInMemoryExporter(exportedItems)
            .Build();

        meterProvider.ForceFlush(MaxTimeToAllowForFlush);

        var metric = exportedItems.First(m => m.Name == "process.memory.virtual");
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
    public void ProcessMetrics_CpuTimeReportsNonNegativeValues()
    {
        var exportedItems = new List<Metric>();
        using var meterProvider = Sdk.CreateMeterProviderBuilder()
            .AddProcessInstrumentation()
            .AddInMemoryExporter(exportedItems)
            .Build();

        meterProvider.ForceFlush(MaxTimeToAllowForFlush);

        var metric = exportedItems.First(m => m.Name == "process.cpu.time");
        foreach (ref readonly var metricPoint in metric.GetMetricPoints())
        {
            Assert.True(metricPoint.GetSumDouble() >= 0);
        }
    }

    [Fact]
    public void ProcessMetrics_MemoryUsageIsWithinReasonableRange()
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
            var value = metricPoint.GetSumLong();
            Assert.True(value > 1_000_000, "Working set should be at least 1 MB for a .NET process.");
        }
    }

    [Fact]
    public void ProcessMetrics_VirtualMemoryExceedsPhysicalMemory()
    {
        var exportedItems = new List<Metric>();
        using var meterProvider = Sdk.CreateMeterProviderBuilder()
            .AddProcessInstrumentation()
            .AddInMemoryExporter(exportedItems)
            .Build();

        meterProvider.ForceFlush(MaxTimeToAllowForFlush);

        var physicalMetric = exportedItems.First(m => m.Name == "process.memory.usage");
        var virtualMetric = exportedItems.First(m => m.Name == "process.memory.virtual");

        long physical = 0;
        foreach (ref readonly var metricPoint in physicalMetric.GetMetricPoints())
        {
            physical = metricPoint.GetSumLong();
        }

        long virtual_ = 0;
        foreach (ref readonly var metricPoint in virtualMetric.GetMetricPoints())
        {
            virtual_ = metricPoint.GetSumLong();
        }

        Assert.True(virtual_ >= physical, "Virtual memory should be >= physical memory.");
    }

    [Fact]
    public void ProcessMetrics_ConsecutiveFlushesProduceConsistentMetricNames()
    {
        var exportedItems = new List<Metric>();
        using var meterProvider = Sdk.CreateMeterProviderBuilder()
            .AddProcessInstrumentation()
            .AddInMemoryExporter(exportedItems)
            .Build();

        meterProvider.ForceFlush(MaxTimeToAllowForFlush);
        var firstNames = exportedItems.Select(m => m.Name).OrderBy(n => n).ToList();

        exportedItems.Clear();
        meterProvider.ForceFlush(MaxTimeToAllowForFlush);
        var secondNames = exportedItems.Select(m => m.Name).OrderBy(n => n).ToList();

        Assert.Equal(firstNames, secondNames);
    }
}
