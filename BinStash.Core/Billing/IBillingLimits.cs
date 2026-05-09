// Copyright (C) Lukas Eßmann — AGPLv3

namespace BinStash.Core.Billing;

public interface IBillingLimits
{
    bool IsStorageAllowed { get; }

    bool IsIngestAllowed { get; }

    bool IsEgressAllowed { get; }

    long MaxStorageBytes { get; }
}
