namespace Sample.FileImport.Contracts;

public static class FileImportSettingKeys
{
    public const string StrictModeDefault = "modules.sample.fileimport.strictModeDefault";
    public const string MaxRowsPerImport = "modules.sample.fileimport.maxRowsPerImport";
    public const string FailOnFirstError = "modules.sample.fileimport.failOnFirstError";
}

public static class FileImportSettingDefaults
{
    public const bool StrictModeDefault = false;
    public const int MaxRowsPerImport = 1000;
    public const bool FailOnFirstError = false;
}
