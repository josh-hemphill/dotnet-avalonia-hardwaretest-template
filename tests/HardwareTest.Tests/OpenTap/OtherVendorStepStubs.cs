namespace OtherVendor;

/// <summary>
/// Unrelated type with a colliding step name. Not a <c>TestStep</c> so plugin
/// search does not register it from the test assembly.
/// </summary>
public sealed class SafeShutdownStep;
