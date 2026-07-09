using System.IO;

namespace ZhongshengGasInspectionHmi.UI.Services;

public static class AppStoragePaths
{
    private const string LegacyAppDataFolderName = "ZhongshengGasInspectionHmi";

    public static string DataDirectory
    {
        get
        {
            var dataDirectory = Path.Combine(ProjectRootDirectory, "Data");
            Directory.CreateDirectory(dataDirectory);
            return dataDirectory;
        }
    }

    public static string ProjectRootDirectory => FindProjectRootDirectory(AppContext.BaseDirectory);

    public static string LegacyAppDataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        LegacyAppDataFolderName);

    public static string GetDataFilePath(string fileName)
    {
        return Path.Combine(DataDirectory, fileName);
    }

    public static void CopyLegacyFileIfNeeded(string fileName)
    {
        var currentPath = GetDataFilePath(fileName);
        if (File.Exists(currentPath))
        {
            return;
        }

        var legacyPath = Path.Combine(LegacyAppDataDirectory, fileName);
        if (!File.Exists(legacyPath))
        {
            return;
        }

        File.Copy(legacyPath, currentPath);
    }

    private static string FindProjectRootDirectory(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ZhongshengGasInspectionHmi.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return AppContext.BaseDirectory;
    }
}
