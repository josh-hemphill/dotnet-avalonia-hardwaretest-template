namespace InstrumentComponents.OpenTap;

// A distinct MVID with the same assembly identity, never loaded by the fixture.
public static class RebuiltMarker
{
    public const string Value = "different selected payload";
}
