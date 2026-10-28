using Klemme15.Core;
using Klemme15.Data;
using Klemme15.Protocols;
using Klemme15.Protocols.Simulation;
using Klemme15.Protocols.Transport;
using Klemme15.Serial;
#if EDIABAS
using Klemme15.Ediabas;
#endif

namespace Klemme15;

/// <summary>Everything needed to open a diagnostic session. Shared by the app and the CLI.</summary>
public sealed record ConnectionSettings
{
    public required string VehicleId { get; init; }

    /// <summary>The specific model (e.g. "E9x-335i"). Picks the demo car; optional for a real car.</summary>
    public string? ModelId { get; init; }
    public string? Port { get; init; }
    public bool Demo { get; init; }

    /// <summary>Folder with the user's SGBD files. Set to use EDIABAS mode.</summary>
    public string? EdiabasPath { get; init; }

    /// <summary>The cable's DSR line really reports ignition (genuine K+DCAN designs do; some clones don't).</summary>
    public bool TrustDsr { get; init; }

    public IReadOnlyList<LiveParameter> LiveFavorites { get; init; } = [];
}

public static class BackendFactory
{
#if EDIABAS
    public const bool EdiabasAvailable = true;
#else
    public const bool EdiabasAvailable = false;
#endif

    public static IDiagnosticBackend Create(ConnectionSettings settings, VehicleFamily vehicle, TrafficLog log, VehicleModel? model = null)
    {
        var mode = vehicle.Cable.Equals("KLine", StringComparison.OrdinalIgnoreCase) ? CableMode.KLine : CableMode.DCan;

        if (!string.IsNullOrWhiteSpace(settings.EdiabasPath) && !settings.Demo)
        {
#if EDIABAS
            var engine = vehicle.Modules.FirstOrDefault(m => m.IsEngine);
            return new EdiabasBackend(new EdiabasOptions
            {
                EcuPath = settings.EdiabasPath,
                ComPort = settings.Port ?? throw new DiagnosticException("Choose the cable's port"),
                VinSgbds = engine?.Sgbd ?? [],
                LiveFavorites = settings.LiveFavorites,
                Log = log,
            });
#else
            throw new DiagnosticException("This build of Klemme15 was made without EDIABAS mode (EdiabasLib was not fetched). Download a release build to use your BMW files.");
#endif
        }

        model ??= vehicle.Models.FirstOrDefault(m => m.Id == settings.ModelId) ?? vehicle.Models.FirstOrDefault();
        ITransport transport = settings.Demo
            ? (model is not null ? VirtualCar.ForModel(model, vehicle.Modules)
               : mode == CableMode.KLine ? VirtualCar.E46_325i() : VirtualCar.E90_325i())
            : new SerialPortTransport(settings.Port ?? throw new DiagnosticException("Choose the cable's port"))
            {
                TrustDsrForIgnition = settings.TrustDsr,
            };

        return new NativeBackend(new LoggingTransport(transport, log), new NativeBackendOptions
        {
            Mode = mode,
            RequireIgnition = settings.TrustDsr && !settings.Demo,
            ProbeTimeout = TimeSpan.FromMilliseconds(settings.Demo ? 120 : 400),
        });
    }
}
