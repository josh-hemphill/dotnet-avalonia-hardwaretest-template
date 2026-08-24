using HardwareTest.Core.Hardware;
using HardwareTest.OpenTap.Host;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace HardwareTest.Features.Instruments;

public partial class DiscoveredResourceItem : ReactiveObject
{
    public required string Resource { get; init; }
    public required string Description { get; init; }
    public string Interface { get; init; } = "Other";
    public string Detail { get; init; } = string.Empty;
    public bool LooksLikeAlias { get; init; }
    public bool SupportsMessageQuery { get; init; }

    [Reactive] private string _idnRaw = string.Empty;
    [Reactive] private string _idnSummary = string.Empty;

    public string Title =>
        string.IsNullOrWhiteSpace(Description) || string.Equals(Description, Resource, StringComparison.Ordinal)
            ? Resource
            : Description;

    public string Subtitle
    {
        get
        {
            var parts = new List<string> { Interface };
            if (!string.IsNullOrWhiteSpace(Detail))
            {
                parts.Add(Detail);
            }

            if (LooksLikeAlias)
            {
                parts.Add("Alias?");
            }

            return string.Join(" · ", parts);
        }
    }

    public bool HasIdn => !string.IsNullOrWhiteSpace(IdnSummary);
}

public partial class OpenTapDiscoveredResourceItem : ReactiveObject
{
    public required string Address { get; init; }
    public required string Source { get; init; }
    public required string Kind { get; init; }
    public string Interface { get; init; } = "Other";
    public string Detail { get; init; } = string.Empty;
    public bool LooksLikeAlias { get; init; }
    public bool SupportsMessageQuery { get; init; }

    [Reactive] private string _idnRaw = string.Empty;
    [Reactive] private string _idnSummary = string.Empty;

    public string Title => Address;

    public string Subtitle
    {
        get
        {
            var parts = new List<string> { Interface, Source };
            if (!string.IsNullOrWhiteSpace(Detail))
            {
                parts.Add(Detail);
            }

            if (LooksLikeAlias)
            {
                parts.Add("Alias?");
            }

            return string.Join(" · ", parts);
        }
    }

    public bool HasIdn => !string.IsNullOrWhiteSpace(IdnSummary);
}

public partial class SlotOverrideItemViewModel : ReactiveObject
{
    public SlotOverrideItemViewModel(
        string planId,
        string planDisplayName,
        OpenTapInstrumentSlot slot,
        string? overrideResource,
        bool useMockVisa,
        string? lastIdnSummary = null)
    {
        PlanId = planId;
        PlanDisplayName = planDisplayName;
        SlotName = slot.Name;
        TypeName = slot.TypeName;
        RoleHint = slot.RoleHint;
        PlanDefaultResource = slot.ResourceName;
        UseMockVisa = useMockVisa;
        LastIdnSummary = lastIdnSummary ?? string.Empty;
        OverrideResource = overrideResource ?? string.Empty;
        PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(OverrideResource))
            {
                this.RaisePropertyChanged(nameof(EffectiveResource));
                this.RaisePropertyChanged(nameof(StatusText));
                this.RaisePropertyChanged(nameof(Summary));
                this.RaisePropertyChanged(nameof(IsOverridden));
                this.RaisePropertyChanged(nameof(Readiness));
            }

            if (args.PropertyName == nameof(LastIdnSummary))
            {
                this.RaisePropertyChanged(nameof(HasLastIdn));
            }
        };
    }

    public string PlanId { get; }
    public string PlanDisplayName { get; }
    public string SlotName { get; }
    public string TypeName { get; }
    public string RoleHint { get; }
    public string PlanDefaultResource { get; }
    public bool UseMockVisa { get; }

    [Reactive] private string _overrideResource = string.Empty;
    [Reactive] private string _lastIdnSummary = string.Empty;

    public bool IsOverridden => !string.IsNullOrWhiteSpace(OverrideResource);

    public string EffectiveResource =>
        string.IsNullOrWhiteSpace(OverrideResource) ? PlanDefaultResource : OverrideResource.Trim();

    public StationSlotReadiness Readiness => StationReadinessEvaluator.EvaluateSlot(
        new StationSlotSnapshot
        {
            SlotName = SlotName,
            PlanId = PlanId,
            RoleHint = RoleHint,
            TypeName = TypeName,
            EffectiveResource = EffectiveResource,
        },
        UseMockVisa);

    public string StatusText => Readiness.Kind switch
    {
        StationSlotReadinessKind.Unbound => "Unbound",
        StationSlotReadinessKind.DemoOnly => "Demo only",
        _ => IsOverridden ? "Overridden" : "Ready",
    };

    public bool HasLastIdn => !string.IsNullOrWhiteSpace(LastIdnSummary);

    public string Summary =>
        $"{PlanDisplayName} / {SlotName} ({RoleHint}) → {EffectiveResource}";
}
