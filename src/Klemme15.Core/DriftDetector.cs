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

using System.Globalization;

namespace Klemme15.Core;

/// <summary>
/// Notices a value that stays outside its healthy range, as opposed to one that only pokes out of it now and then.
/// A running total (a one-sided CUSUM for each direction) grows while a reading is outside the healthy band, by how
/// far outside it is, and drains while readings are inside. It is a plain struct with no allocation, so it can run
/// on every live sample. It says how far a value has drifted from the baseline it is given: it is not a prediction
/// of when a part will fail, and the baseline has to be right for the result to mean anything.
/// </summary>
public struct DriftDetector
{
    /// <summary>The edge of a 10th-90th percentile band, in standard deviations of a normal spread.</summary>
    public const double BandEdge = 1.2816;

    /// <summary>The total at which drift counts as sustained. With readings one band-width outside, that takes four samples.</summary>
    public const double AlarmLine = 5;

    private double _up, _down;

    public int Samples { get; private set; }

    /// <summary>
    /// Adds one reading with the healthy range at its operating point. Returns the drift score: 0 on the baseline,
    /// 1 at the line for "sustained", more than 1 for longer or larger drift.
    /// </summary>
    public double Add(double value, double p10, double median, double p90)
    {
        var sigma = Math.Max((p90 - p10) / (2 * BandEdge), 1e-9);
        // Capped, so one wild sample (a sensor glitch, a gear change) can't trip it by itself.
        var z = Math.Clamp((value - median) / sigma, -4, 4);
        _up = Math.Max(0, _up + z - BandEdge);
        _down = Math.Max(0, _down - z - BandEdge);
        Samples++;
        return Score;
    }

    public readonly double Score => Math.Max(_up, _down) / AlarmLine;
    public readonly bool High => _up >= AlarmLine;
    public readonly bool Low => _down >= AlarmLine;

    public void Reset() => this = default;
}

/// <summary>What a log's walk through the drift detector found.</summary>
public sealed record DriftResult(double PeakScore, bool Above, TimeSpan? SustainedFrom, int Samples, string Summary);

public static class BaselineDrift
{
    /// <summary>
    /// Runs a log through the drift detector in the order it was recorded, each sample against the healthy range at
    /// its own operating point. Null when the log lacks what the curve needs.
    /// </summary>
    public static DriftResult? Run(DataLog log, BaselineCurve curve)
    {
        if (!BaselineAligner.Applies(log, curve)) return null;
        var detector = new DriftDetector();
        double peak = 0;
        var above = false;
        TimeSpan? from = null;
        foreach (var (i, y, reference) in BaselineAligner.Walk(log, curve))
        {
            var score = detector.Add(y, reference.P10, reference.Median, reference.P90);
            if (score > peak) { peak = score; above = detector.High || (!detector.Low && y > reference.Median); }
            if (from is null && (detector.High || detector.Low)) from = log.Times[i] - log.Times[0];
        }
        if (detector.Samples == 0) return null;
        var name = BaselineSignals.Label(curve.Y).ToLowerInvariant();
        var summary = from is { } t
            ? $"Sustained drift: your {name} stayed {(above ? "above" : "below")} the healthy range from {t:m\\:ss} into the log (drift score {peak.ToString("0.0", CultureInfo.CurrentCulture)}; 1.0 is where drift counts as sustained)."
            : peak >= 0.5 ? $"Your {name} left the healthy range for short stretches, not long enough to count as sustained drift."
            : "No sustained drift from the healthy range.";
        return new DriftResult(peak, above, from, detector.Samples, summary);
    }
}
