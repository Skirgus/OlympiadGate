namespace OlympiadGate.Core;

public static class AppPaths
{
    public const string PipeName = "OlympiadGate";

    public static string DataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "OlympiadGate");

    public static string DatabasePath => Path.Combine(DataDirectory, "olympiadgate.db");

    public static string PolicyPath => Path.Combine(DataDirectory, "policies.json");

    public static string LogPath => Path.Combine(DataDirectory, "service.log");
}
