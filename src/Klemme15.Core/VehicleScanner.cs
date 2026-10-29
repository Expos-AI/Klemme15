using System.Diagnostics;

namespace Klemme15.Core;

/// <summary>Scan progress. <paramref name="Current"/> is the module being asked right now, if any.</summary>
public sealed record ScanProgress(int Done, int Total, EcuScanResult? Latest, string Message, EcuDefinition? Current = null);

/// <summary>
/// The "quick test": identify every ECU, then read its fault memory. Absent modules are skipped
/// as soon as they fail to answer the identify request, which is what keeps a full scan fast.
/// </summary>
public sealed class VehicleScanner(IDiagnosticBackend backend, IFaultCodeDatabase? codes = null)
{
    /// <summary>How many times to retry an ECU that answered identify but failed later.</summary>
    public int Retries { get; init; } = 1;

    public async Task<VehicleScanResult> ScanAsync(
        IReadOnlyList<EcuDefinition> ecus,
        string? chassis,
        IProgress<ScanProgress>? progress = null,
        CancellationToken ct = default)
    {
        var started = DateTimeOffset.Now;
        var total = Stopwatch.StartNew();
        var results = new List<EcuScanResult>(ecus.Count);
        var cancelled = false;

        // One broadcast tells us which modules are fitted, so absent ones can be skipped quickly.
        if (backend is IPresenceProbe probe)
        {
            progress?.Report(new ScanProgress(0, ecus.Count, null, "Finding fitted modules"));
            try { await probe.ProbePresentAsync(ct); }
            catch (OperationCanceledException) { cancelled = true; }
            catch (Exception) { /* fall back to asking every module */ }
        }

        string? vin = null;
        if (!cancelled && backend.Capabilities.HasFlag(BackendCapabilities.ReadVin))
        {
            progress?.Report(new ScanProgress(0, ecus.Count, null, "Reading VIN"));
            try { vin = await backend.ReadVinAsync(ct); }
            catch (OperationCanceledException) { cancelled = true; }
            catch (Exception) { /* VIN is optional */ }
        }

        foreach (var ecu in cancelled ? Array.Empty<EcuDefinition>() : ecus)
        {
            if (ct.IsCancellationRequested) { cancelled = true; break; }
            progress?.Report(new ScanProgress(results.Count, ecus.Count, null, $"Asking {ecu.Name}", ecu));
            EcuScanResult result;
            try
            {
                result = await ScanEcuAsync(ecu, ct);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
                break;
            }
            results.Add(result);
            progress?.Report(new ScanProgress(results.Count, ecus.Count, result, $"{ecu.Name}: {result.Status}"));
        }

        return new VehicleScanResult
        {
            Started = started,
            Backend = backend.Name,
            Vin = vin,
            Chassis = chassis,
            IgnitionOn = backend.IgnitionOn,
            Ecus = results,
            Elapsed = total.Elapsed,
            Cancelled = cancelled,
        };
    }

    public async Task<EcuScanResult> ScanEcuAsync(EcuDefinition ecu, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        Exception? last = null;
        for (var attempt = 0; attempt <= Retries; attempt++)
        {
            try
            {
                var identity = await backend.IdentifyAsync(ecu, ct);
                if (identity is null)
                    return new EcuScanResult { Ecu = ecu, Status = EcuScanStatus.NoResponse, Elapsed = sw.Elapsed };

                IReadOnlyList<Fault> faults = [];
                string? note = null;
                if (backend.Capabilities.HasFlag(BackendCapabilities.ReadFaults))
                {
                    try { faults = Describe(await backend.ReadFaultsAsync(ecu, ct), ecu, identity); }
                    catch (NotSupportedException e) { note = e.Message; }
                }

                return new EcuScanResult
                {
                    Ecu = ecu,
                    Status = faults.Any(f => !f.IsInfo) ? EcuScanStatus.Faults : EcuScanStatus.Ok,
                    Identity = identity,
                    Faults = faults,
                    Note = note,
                    Elapsed = sw.Elapsed,
                };
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e)
            {
                last = e;
            }
        }
        return new EcuScanResult { Ecu = ecu, Status = EcuScanStatus.Error, Error = last?.Message, Elapsed = sw.Elapsed };
    }

    /// <summary>Fills in descriptions, tips and sources, using the module's identity to pick the right dictionary.</summary>
    public IReadOnlyList<Fault> Describe(IReadOnlyList<Fault> faults, EcuDefinition ecu, EcuIdentity? identity)
    {
        if (codes is IFaultDescriber describer) return faults.Select(f => describer.Describe(f, ecu, identity)).ToList();
        return Describe(faults);
    }

    /// <summary>Fills in descriptions, tips and sources the backend did not supply.</summary>
    public IReadOnlyList<Fault> Describe(IReadOnlyList<Fault> faults)
    {
        if (codes is null) return faults;
        return faults.Select(f =>
        {
            if (f.Description is not null) return f;
            if (f.Source == FaultSource.Bmw && codes is IFaultCodeDetails details && details.LookupBmw(f.EcuId, f.Code) is { } info)
                return f with { Description = info.Text, Hint = info.Hint, Confidence = info.Confidence, Sources = info.Sources ?? [], UsuallyHarmless = info.UsuallyHarmless };
            return f with { Description = f.Source == FaultSource.Obd2 ? codes.DescribeObd(f.Code) : codes.DescribeBmw(f.EcuId, f.Code) };
        }).ToList();
    }
}
