namespace HardwareTest.Authoring;

public enum AuthoringRecoveryArea { CurrentLocation, Sequence, Instruments, Environment }

/// Plain-language recovery copy supplements, and never replaces, the complete diagnostic.
internal static class AuthoringIssuePresentation
{
    internal static string Headline(string code) => code switch
    {
        "INSTRUMENT_UNAVAILABLE" => "An instrument prerequisite is unavailable",
        "INSTRUMENT_DEPENDENCY_MISSING" => "Declare the instrument package",
        "INVALID_OPENTAP_HOME" => "Choose a valid authoring environment",
        "INSTRUMENT_CONFIGURATION" => "Complete the instrument settings",
        "INSTRUMENT_INCOMPATIBLE" => "Choose a compatible instrument",
        "EMPTY_MEASURE" => "Add the first measurement or calculation",
        "MISSING_IDENTITY" => "Add the required DUT identity check",
        "INCOMPLETE_NUMERIC_INPUT" => "Complete a numeric value",
        "DUPLICATE_CHANNEL" => "Give each output a unique channel",
        "MISSING_CHANNEL" => "Connect this input to an earlier output",
        "MISSING_INSTRUMENT" or "MISSING_INSTRUMENT_BINDING" => "Choose an instrument for this step",
        "DUPLICATE_INSTRUMENT" => "Use unique instrument slot names",
        "DUPLICATE_INPUT_CHANNEL" or "INPUT_CHANNEL_CARDINALITY" => "Review this step's input channels",
        "MISSING_LIMITS" => "Set a pass criterion",
        "FORMULA_DEPLOYMENT" => "Review the calculation requirements",
        "FORMULA_EXCLUDED" => "This exploration calculation is not deployed",
        "OPAQUE_REFERENCES" => "Review the imported step's references",
        "LIBRARY_LIFECYCLE_REIMPORT" => "Reimport the instrument lifecycle steps",
        "TF_MISSING_ELAPSED" or "TF_GRID" => "Use a recording with suitable timing",
        _ => "Review this finding"
    };

    internal static string RecoverySummary(string code, string message) => code switch
    {
        "INSTRUMENT_UNAVAILABLE" => "Review the selected package or adapter before compiling. Your current instrument binding is retained.",
        "INSTRUMENT_DEPENDENCY_MISSING" => "Declare Instrument Components, save the requirements, then prepare or import its package.",
        "INVALID_OPENTAP_HOME" => "Select a supported OpenTAP home in Environment. See details for the rejected path.",
        "INSTRUMENT_CONFIGURATION" => "Review the instrument's required values. Entered text remains in your draft.",
        "INSTRUMENT_INCOMPATIBLE" => "Choose hardware that supports the functions used by this plan.",
        "EMPTY_MEASURE" => "Add an acquisition or calculation to the sequence before building this plan.",
        "MISSING_IDENTITY" => "This plan requires a serial number. Add an identity check or review that requirement.",
        "INCOMPLETE_NUMERIC_INPUT" => "Enter a valid number in the affected field; your incomplete entry has been retained.",
        "DUPLICATE_CHANNEL" => "Rename an output channel so later calculations can identify its producer.",
        "MISSING_CHANNEL" => "Choose an existing channel from a preceding supported step.",
        "MISSING_INSTRUMENT" or "MISSING_INSTRUMENT_BINDING" => "Configure a slot in Instruments, then select that slot in this step.",
        "MISSING_LIMITS" => "Choose the threshold or limits that define a passing result.",
        "FORMULA_DEPLOYMENT" => "Review the calculation in its editor. Details retain the complete compiler explanation.",
        "FORMULA_EXCLUDED" => "The source is saved for exploration and is intentionally excluded from the deployed plan.",
        "OPAQUE_REFERENCES" => "The original source is preserved; some references require review in its original editor.",
        _ => message
    };

    internal static string? FieldAction(string? field) => field switch
    {
        "Threshold" => "Edit pass criterion",
        "LimitLow" or "LimitHigh" => "Edit limits",
        "ChannelKey" => "Edit channel",
        _ => null
    };
}
