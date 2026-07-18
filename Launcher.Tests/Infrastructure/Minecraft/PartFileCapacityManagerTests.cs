/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.Infrastructure.Minecraft;

namespace Launcher.Tests.Infrastructure.Minecraft;

[CollectionDefinition(PartFileCapacityManagerTestCollection.CollectionName, DisableParallelization = true)]
public sealed class PartFileCapacityManagerTestCollection
{
    public const string CollectionName = "Part file capacity manager";
}

[Collection(PartFileCapacityManagerTestCollection.CollectionName)]
public sealed class PartFileCapacityManagerTests
{
    [Fact]
    public void UnknownSizeLeasesDoNotReserveCapacityBeforeBytesAreWritten()
    {
        var leases = new List<PartFileCapacityManager.CapacityLease>();
        try
        {
            for (var index = 0; index < 33; index++)
                leases.Add(PartFileCapacityManager.Reserve(expectedSize: null));

            // The 2 GiB budget is still enforced from actual bytes, not from
            // speculative reservations for 33 unknown-size responses.
            foreach (var lease in leases.Take(32))
                lease.BeforeWrite(64 * 1024 * 1024);

            Assert.Throws<DownloadLocalFileException>(() => leases[^1].BeforeWrite(1));
        }
        finally
        {
            foreach (var lease in leases)
                lease.Dispose();
        }
    }
}
