using OpenHood.Core;

namespace OpenHood.App.ViewModels;

public enum MarkerKind { Module, FaultSource }

public enum MarkerState
{
    /// <summary>Not asked yet.</summary>
    Idle,
    /// <summary>Being asked right now during a scan.</summary>
    Asking,
    /// <summary>Module answered, no faults.</summary>
    Ok,
    /// <summary>Stored fault(s).</summary>
    Stored,
    /// <summary>A fault is active right now.</summary>
    Active,
    /// <summary>Module did not answer (not fitted, or asleep).</summary>
    Absent,
}

/// <summary>One dot on the car drawing: a module, or the place a fault comes from.</summary>
public sealed class MarkerViewModel : ViewModelBase
{
    private MarkerState _state;
    private bool _isSelected;
    private string _detail = "";

    public MarkerViewModel(MarkerKind kind, string id, string label, SchematicPoint at, Action<MarkerViewModel>? onSelect = null)
    {
        Kind = kind;
        Id = id;
        Label = label;
        X = at.X;
        Y = at.Y;
        SelectCommand = new RelayCommand(() => onSelect?.Invoke(this));
    }

    public MarkerKind Kind { get; }
    public string Id { get; }
    public string Label { get; }
    public double X { get; }
    public double Y { get; }

    /// <summary>Top-left of the 16 x 16 anchor cell, centred on the point.</summary>
    public double Left => X - 8;
    public double Top => Y - 8;

    public bool IsModule => Kind == MarkerKind.Module;
    public bool IsFaultSource => Kind == MarkerKind.FaultSource;

    public EcuDefinition? Ecu { get; init; }
    public List<FaultItemViewModel> Faults { get; } = [];

    public MarkerState State
    {
        get => _state;
        set
        {
            if (!Set(ref _state, value)) return;
            foreach (var p in new[] { nameof(IsIdle), nameof(IsAsking), nameof(IsOk), nameof(IsStored), nameof(IsActive), nameof(IsAbsent), nameof(ShowLabel) })
                Raise(p);
        }
    }

    public bool IsIdle => _state == MarkerState.Idle;
    public bool IsAsking => _state == MarkerState.Asking;
    public bool IsOk => _state == MarkerState.Ok;
    public bool IsStored => _state == MarkerState.Stored;
    public bool IsActive => _state == MarkerState.Active;
    public bool IsAbsent => _state == MarkerState.Absent;

    /// <summary>Module labels are hidden for modules that are not there, to keep the drawing calm.</summary>
    public bool ShowLabel => IsModule && _state != MarkerState.Absent;

    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }

    /// <summary>Tooltip text.</summary>
    public string Detail { get => _detail; set => Set(ref _detail, value); }

    public RelayCommand SelectCommand { get; }
}

/// <summary>A fault as shown in lists: what it is, where it comes from, and which marker shows it.</summary>
public sealed class FaultItemViewModel(Fault fault, EcuDefinition ecu, string? origin, bool originIsComponent) : ViewModelBase
{
    public Fault Fault { get; } = fault;
    public EcuDefinition Ecu { get; } = ecu;
    public string Code => Fault.Code;
    public string Description => Fault.DisplayDescription;
    public string ModuleName => Ecu.Name;
    public bool IsActive => Fault.State == FaultState.Present;
    public bool IsStored => !IsActive;

    public string StateText => Fault.State switch
    {
        FaultState.Present => "Active now",
        FaultState.Intermittent => "Intermittent",
        FaultState.Stored => "Stored",
        _ => "Stored",
    };

    public string SourceText => Fault.Source == FaultSource.Obd2 ? "OBD-II" : "BMW";

    /// <summary>"Check here" text: the component the code points at, or the module itself.</summary>
    public string Where => origin is null ? $"Reported by {Ecu.Name}"
        : originIsComponent ? origin
        : $"{origin} (no specific part known for this code)";

    public string Details => string.Join("   ", Fault.Details.Select(kv => $"{kv.Key}: {kv.Value}"));
    public bool HasDetails => Fault.Details.Count > 0;

    public MarkerViewModel? Marker { get; set; }
    public RelayCommand? ShowCommand { get; set; }
}
