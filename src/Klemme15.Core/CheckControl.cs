using System.Globalization;

namespace Klemme15.Core;

/// <summary>
/// How serious a Check Control message is, matching the colour of the cluster's warning triangle:
/// <see cref="Critical"/> is red (stop safely), <see cref="Warning"/> and <see cref="Advisory"/> are yellow.
/// </summary>
public enum CcSeverity { Advisory, Warning, Critical }

/// <summary>What the database knows about one Check Control ID.</summary>
public sealed record CheckControlInfo(
    string Title,
    string? Description,
    CcSeverity Severity,
    string? Hint = null,
    string? Confidence = null,
    IReadOnlyList<string>? Sources = null);

/// <summary>
/// One Check Control (CC-ID) message from the instrument cluster: the dash warnings with the small triangle
/// (key battery low, bulb out, washer fluid low...). These are not fault codes, so a car can show one with an
/// empty fault memory.
/// </summary>
public sealed record CheckControlMessage
{
    public required int Id { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public CcSeverity Severity { get; init; } = CcSeverity.Warning;

    /// <summary>True when the cluster shows it now; false for an entry from the cluster's message history.</summary>
    public bool Active { get; init; } = true;

    /// <summary>Mileage when it last appeared (history entries only; 8 km steps).</summary>
    public long? MileageKm { get; init; }

    public string? Hint { get; init; }

    /// <summary>"high" or "medium" for researched entries; null when the ID isn't in the database yet.</summary>
    public string? Confidence { get; init; }

    public IReadOnlyList<string> Sources { get; init; } = [];

    /// <summary>BMW's own wording, read from the user's BMW files when available (never shipped with Klemme15).</summary>
    public string? OfficialText { get; init; }

    public string? OfficialSource { get; init; }

    /// <summary>True when the ID isn't in Klemme15's list yet.</summary>
    public bool Unknown => Confidence is null;

    /// <summary>"CC 67" — how BMW tools and forums usually write it.</summary>
    public string Code => $"CC {Id}";
}

/// <summary>What one read of the cluster returned: the active IDs, the history, and the raw replies for the log.</summary>
public sealed record CheckControlReading(
    IReadOnlyList<int> Active,
    IReadOnlyList<CheckControlHistoryEntry> History,
    string? RawActive = null,
    string? RawHistory = null);

public sealed record CheckControlHistoryEntry(int Id, long? MileageKm);

/// <summary>A backend that can ask the instrument cluster which Check Control messages it shows.</summary>
public interface ICheckControlReader
{
    /// <summary>Reads the active messages (and the history, if the cluster offers it). Null when the cluster can't say.</summary>
    Task<CheckControlReading?> ReadCheckControlAsync(EcuDefinition cluster, CancellationToken ct = default);
}

/// <summary>Turns a CC-ID into words: Klemme15's own list first, BMW's text from the user's files alongside.</summary>
public interface ICheckControlCatalog
{
    CheckControlInfo? LookupCheckControl(int id);

    /// <summary>BMW's text for the ID from the user's own files, and where it came from; null when there are none.</summary>
    (string Text, string Source)? OfficialCheckControlText(int id) => null;
}

/// <summary>
/// Decodes the cluster's Check Control replies (KWP2000 on D-CAN, cluster address 0x60):
/// <list type="bullet">
/// <item><c>$21 09</c> → <c>61 09 [count] id id ...</c>: the IDs showing now, 2 bytes each, high byte first.
/// BMW's description gives the count and the 2-byte IDs; whether the count byte is present can differ between
/// clusters, so the length decides (odd payload = count first).</item>
/// <item><c>$21 0F</c> → <c>61 0F ...</c>: the message history, 4 bytes per entry (ID, then mileage in 8 km steps).
/// This layout is inferred from BMW's result list and not yet confirmed on a car.</item>
/// </list>
/// </summary>
public static class CheckControlParser
{
    public static readonly byte[] ActiveRequest = [0x21, 0x09];
    public static readonly byte[] HistoryRequest = [0x21, 0x0F];

    /// <summary>IDs that mean "nothing" rather than a message.</summary>
    public static bool IsPlaceholder(int id) => id is 0 or 0xFFFF;

    /// <summary>The active IDs from a <c>61 09</c> reply, or null when the reply isn't one (negative response, garbage).</summary>
    public static IReadOnlyList<int>? ParseActive(byte[]? reply)
    {
        if (reply is null || reply.Length < 2 || reply[0] != 0x61 || reply[1] != 0x09) return null;
        var payload = reply.AsSpan(2);
        var ids = new List<int>();
        int start = 0, limit = int.MaxValue;
        if (payload.Length % 2 == 1)
        {
            limit = payload[0];
            start = 1;
        }
        for (var i = start; i + 1 < payload.Length && ids.Count < limit; i += 2)
        {
            var id = payload[i] << 8 | payload[i + 1];
            if (!IsPlaceholder(id) && !ids.Contains(id)) ids.Add(id);
        }
        return ids;
    }

    /// <summary>The history from a <c>61 0F</c> reply; empty when the layout isn't recognised.</summary>
    public static IReadOnlyList<CheckControlHistoryEntry> ParseHistory(byte[]? reply)
    {
        if (reply is null || reply.Length < 2 || reply[0] != 0x61 || reply[1] != 0x0F) return [];
        var payload = reply.AsSpan(2);
        var start = payload.Length % 4 switch { 0 => 0, 1 => 1, _ => -1 };
        if (start < 0) return [];
        var list = new List<CheckControlHistoryEntry>();
        for (var i = start; i + 3 < payload.Length; i += 4)
        {
            var id = payload[i] << 8 | payload[i + 1];
            if (IsPlaceholder(id)) continue;
            var steps = payload[i + 2] << 8 | payload[i + 3];
            list.Add(new CheckControlHistoryEntry(id, steps == 0xFFFF ? null : steps * 8L));
        }
        return list;
    }

    /// <summary>
    /// The IDs from EDIABAS's STAT_CC_NUMMERN (a hex string, 2 bytes per ID). <paramref name="count"/> is
    /// STAT_CC_ID_ANZAHL / STAT_ANZAHL when the job returned it.
    /// </summary>
    public static IReadOnlyList<int> ParseEdiabasList(string? hex, int? count = null)
    {
        if (string.IsNullOrWhiteSpace(hex)) return [];
        var clean = new string(hex.Where(Uri.IsHexDigit).ToArray());
        if (clean.Length % 2 == 1) clean = clean[..^1];
        var bytes = Convert.FromHexString(clean);
        var ids = new List<int>();
        for (var i = 0; i + 1 < bytes.Length && (count is null || ids.Count < count); i += 2)
        {
            var id = bytes[i] << 8 | bytes[i + 1];
            if (!IsPlaceholder(id) && !ids.Contains(id)) ids.Add(id);
        }
        return ids;
    }

    public static string Hex(byte[]? reply) => reply is null ? "" : Convert.ToHexString(reply);

    public static int? ParseInt(string? s) =>
        int.TryParse(s?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;
}

/// <summary>Builds the messages shown to the user from a reading and the catalog.</summary>
public static class CheckControlDescriber
{
    /// <summary>The cluster is the module that keeps the Check Control list (KOMBI on E8x/E9x/E6x/E7x).</summary>
    public static bool IsCluster(EcuDefinition ecu) =>
        ecu.Protocol == BusProtocol.BmwFast && ecu.Id.EndsWith(".KOMBI", StringComparison.OrdinalIgnoreCase);

    public static CheckControlMessage Describe(int id, ICheckControlCatalog? catalog, bool active = true, long? mileage = null)
    {
        var info = catalog?.LookupCheckControl(id);
        var official = catalog?.OfficialCheckControlText(id);
        return new CheckControlMessage
        {
            Id = id,
            Title = info?.Title ?? official?.Text ?? $"Check Control message {id}",
            Description = info?.Description ?? (official is null
                ? "Not in Klemme15's list yet. The message text on the dash says what it is; please add it to the database."
                : null),
            Severity = info?.Severity ?? CcSeverity.Warning,
            Hint = info?.Hint,
            Confidence = info?.Confidence,
            Sources = info?.Sources ?? [],
            OfficialText = official?.Text,
            OfficialSource = official?.Source,
            Active = active,
            MileageKm = mileage,
        };
    }

    /// <summary>Active messages first (most serious first), then the history (latest mileage first).</summary>
    public static IReadOnlyList<CheckControlMessage> Describe(CheckControlReading reading, ICheckControlCatalog? catalog)
    {
        var active = reading.Active.Select(id => Describe(id, catalog)).OrderByDescending(m => m.Severity).ThenBy(m => m.Id);
        var history = reading.History
            .GroupBy(h => h.Id)
            .Select(g => g.OrderByDescending(h => h.MileageKm ?? -1).First())
            .Where(h => !reading.Active.Contains(h.Id))
            .Select(h => Describe(h.Id, catalog, active: false, mileage: h.MileageKm))
            .OrderByDescending(m => m.MileageKm ?? -1);
        return [.. active, .. history];
    }

    /// <summary>The colour the cluster's triangle would show for these messages: red, yellow, or none.</summary>
    public static CcSeverity? Worst(IEnumerable<CheckControlMessage> messages) =>
        messages.Where(m => m.Active).Select(m => (CcSeverity?)m.Severity).DefaultIfEmpty(null).Max();

    public static string SeverityLabel(CcSeverity s) => s switch
    {
        CcSeverity.Critical => "Red: stop safely",
        CcSeverity.Warning => "Yellow: get it looked at",
        _ => "Yellow: advisory",
    };
}
