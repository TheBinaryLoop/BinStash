// Copyright (C) Lukas Eßmann — AGPLv3

namespace BinStash.Core.Billing;

public interface IUsageMeteringService
{
    void RecordIngest(Guid tenantId, long bytes);

    void RecordEgress(Guid tenantId, long bytes);

    Task RecordStorageSnapshotAsync(Guid tenantId, long bytes, CancellationToken ct = default);
}
