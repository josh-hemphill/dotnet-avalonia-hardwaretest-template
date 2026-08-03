using OpenTap;

namespace HardwareTest.OpenTap.Plugins.Mixins;

/// Demo mixin: station-overridable annotation text on any test step.
public sealed class AnnotationMixin : IMixin
{
    [Display("Note", Groups: ["Annotation"], Description: "Free-form bench note (Engineer/Debug station override).", Order: 1)]
    public string Note { get; set; } = string.Empty;

    [Display("Include in report", Groups: ["Annotation"], Description: "When true, Note is intended for report export (demo flag).", Order: 2)]
    public bool IncludeInReport { get; set; }
}
