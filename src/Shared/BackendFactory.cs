// Klemme15: open-source BMW diagnostics
// Copyright (C) 2026 Kindric Castro
//
// This program is free software: you can redistribute it and/or modify it under the terms of the GNU General
// Public License as published by the Free Software Foundation, either version 3 of the License, or (at your
// option) any later version. This program is distributed in the hope that it will be useful, but WITHOUT ANY
// WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
// General Public License for more details. You should have received a copy of the GNU General Public License
// along with this program. If not, see <https://www.gnu.org/licenses/>.
//
// SPDX-License-Identifier: GPL-3.0-or-later

using Klemme15.Core;
using Klemme15.Data;
using Klemme15.Protocols;
using Klemme15.Protocols.Recording;
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

    /// <summary>Record this session to this file (.k15log), for playing back later. Not available in EDIABAS mode.</summary>
    public string? RecordTo { get; init; }

    /// <summary>Play this recording back instead of talking to a car. Nothing is sent anywhere.</summary>
    public K15Log? Replay { get; init; }

    /// <summary>Playback speed for <see cref="Replay"/>: 1 is the speed of the original session.</summary>
    public double ReplaySpeed { get; init; } = 1;
}

public static class BackendFactory
{
#if EDIABAS
    public const bool EdiabasAvailable = true;
#else
    public const bool EdiabasAvailable = false;
#endif

    private static string AppVersion => typeof(BackendFactory).Assembly.GetName().Version?.ToString(3) ?? "";

    public static IDiagnosticBackend Create(ConnectionSettings settings, VehicleFamily vehicle, TrafficLog log, VehicleModel? model = null)
    {
        var mode = vehicle.Cable.Equals("KLine", StringComparison.OrdinalIgnoreCase) ? CableMode.KLine : CableMode.DCan;

        if (settings.Replay is { } recording)
            return new NativeBackend(new LoggingTransport(new ReplayTransport(recording, settings.ReplaySpeed), log), new NativeBackendOptions
            {
                Mode = mode,
                ProbeTimeout = TimeSpan.FromMilliseconds(300),
            });

        if (!string.IsNullOrWhiteSpace(settings.EdiabasPath) && !settings.Demo)
        {
            if (settings.RecordTo is not null)
                throw new DiagnosticException("Recording a session isn't available in EDIABAS mode yet. Untick 'Use my own BMW files (EDIABAS mode)' to record.");
            if (NetworkTransport.IsNetworkAddress(settings.Port))
                throw new DiagnosticException("EDIABAS mode needs a USB cable for now. Untick 'Use my own BMW files (EDIABAS mode)' to use a Wi-Fi cable.");
#if EDIABAS
            var engine = vehicle.Modules.FirstOrDefault(m => m.IsEngine);
            return new EdiabasBackend(new EdiabasOptions
            {
                EcuPath = settings.EdiabasPath,
                ComPort = settings.Port ?? throw new DiagnosticException("Choose the cable's port"),
                VinSgbds = engine?.Sgbd ?? [],
                // One native broadcast first, so EDIABAS doesn't wait several seconds on each module the car doesn't have.
                PresenceProbe = mode == CableMode.DCan ? async ct =>
                {
                    await using var native = new NativeBackend(new LoggingTransport(new SerialPortTransport(settings.Port!), log),
                        new NativeBackendOptions { Mode = CableMode.DCan, ProbeTimeout = TimeSpan.FromMilliseconds(300) });
                    await native.ConnectAsync(ct);
                    return await native.ProbePresentAsync(ct);
                } : null,
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
            : NetworkTransport.IsNetworkAddress(settings.Port)
                ? new NetworkTransport(settings.Port!)
                : new SerialPortTransport(settings.Port ?? throw new DiagnosticException("Choose the cable's port"))
                {
                    TrustDsrForIgnition = settings.TrustDsr,
                };

        if (settings.RecordTo is { } file)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
                var info = new K15LogInfo(AppVersion, vehicle.Id, model?.Id, mode == CableMode.KLine ? "KLine" : "DCan", DateTimeOffset.Now);
                transport = new RecordingTransport(transport, new K15LogRecorder(File.Create(file), info));
                log.Note($"Recording this session to {Path.GetFileName(file)}");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                transport.Dispose();
                throw new DiagnosticException($"Klemme15 couldn't start the recording ({e.Message}). Choose another place for it, or connect without recording.");
            }
        }

        return new NativeBackend(new LoggingTransport(transport, log), new NativeBackendOptions
        {
            Mode = mode,
            RequireIgnition = settings.TrustDsr && !settings.Demo,
            ProbeTimeout = TimeSpan.FromMilliseconds(settings.Demo ? 120 : 300),
        });
    }
}
