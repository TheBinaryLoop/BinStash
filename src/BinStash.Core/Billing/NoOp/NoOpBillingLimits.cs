// Copyright (C) Lukas Eßmann — AGPLv3 or later

namespace BinStash.Core.Billing.NoOp;

public sealed class NoOpBillingLimits : IBillingLimits
{
    public static NoOpBillingLimits Instance { get; } = new();

    public bool IsStorageAllowed => true;

    public bool IsIngestAllowed => true;

    public bool IsEgressAllowed => true;

    public long MaxStorageBytes => long.MaxValue;
}
