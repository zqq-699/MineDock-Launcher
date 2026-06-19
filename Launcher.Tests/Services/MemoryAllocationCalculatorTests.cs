using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.Tests.Services;

public sealed class MemoryAllocationCalculatorTests
{
    [Theory]
    [InlineData(LoaderKind.Vanilla, 0, 4096)]
    [InlineData(LoaderKind.Fabric, 0, 4096)]
    [InlineData(LoaderKind.Quilt, 30, 4608)]
    [InlineData(LoaderKind.Fabric, 31, 5120)]
    [InlineData(LoaderKind.Fabric, 81, 5632)]
    [InlineData(LoaderKind.Fabric, 151, 6144)]
    [InlineData(LoaderKind.Forge, 0, 4096)]
    [InlineData(LoaderKind.NeoForge, 30, 4608)]
    [InlineData(LoaderKind.Forge, 31, 5120)]
    [InlineData(LoaderKind.Forge, 81, 5632)]
    [InlineData(LoaderKind.Forge, 151, 6144)]
    public void AutomaticRequestedMemoryUsesLoaderAndModTiers(
        LoaderKind loader,
        int enabledModCount,
        int expectedMemoryMb)
    {
        Assert.Equal(
            expectedMemoryMb,
            MemoryAllocationCalculator.CalculateAutomaticRequestedMemoryMb(loader, enabledModCount));
    }

    [Fact]
    public void AutomaticMemoryIsCappedByCurrentAvailableMemory()
    {
        var snapshot = CreateSnapshot(totalMemoryGb: 16, availableMemoryGb: 4);

        var memoryMb = MemoryAllocationCalculator.CalculateAutomaticMemoryMb(
            snapshot,
            LoaderKind.Forge,
            enabledModCount: 151);

        Assert.Equal(3072, memoryMb);
    }

    [Fact]
    public void AutomaticMemoryIsCappedByTotalReservedMemory()
    {
        var snapshot = CreateSnapshot(totalMemoryGb: 6, availableMemoryGb: 6);

        var memoryMb = MemoryAllocationCalculator.CalculateAutomaticMemoryMb(
            snapshot,
            LoaderKind.Forge,
            enabledModCount: 151);

        Assert.Equal(4096, memoryMb);
    }

    [Fact]
    public void AutomaticSafetyMaximumUsesTotalMemoryRatio()
    {
        Assert.Equal(
            7680,
            MemoryAllocationCalculator.CalculateAutomaticSafetyMaximumMb(
                totalMemoryMb: 10240,
                availableMemoryMb: 12288));
    }

    private static SystemMemorySnapshot CreateSnapshot(int totalMemoryGb, int availableMemoryGb)
    {
        return new SystemMemorySnapshot(
            TotalMemoryBytes: totalMemoryGb * 1024L * 1024L * 1024L,
            AvailableMemoryBytes: availableMemoryGb * 1024L * 1024L * 1024L);
    }
}
