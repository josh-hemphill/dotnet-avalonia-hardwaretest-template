namespace HardwareTest.Core.Serialization;

/// Current schema version per persisted document type (greppable; bump deliberately).
public static class SchemaVersions
{
    public const int AppSettings = 1;
    public const int UiState = 1;
    public const int TestRunRecord = 2;
    public const int SuiteRunRecord = 1;
    public const int CrashReport = 1;
}

/// Stable document-type keys for upgrade registration and log messages.
public static class SchemaDocumentTypes
{
    public const string AppSettings = "AppSettings";
    public const string UiState = "UiState";
    public const string TestRunRecord = "TestRunRecord";
    public const string SuiteRunRecord = "SuiteRunRecord";
    public const string CrashReport = "CrashReport";
}
