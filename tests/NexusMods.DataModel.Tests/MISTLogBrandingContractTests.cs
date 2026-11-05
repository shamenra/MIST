using System;
using System.IO;
using Xunit;

namespace NexusMods.DataModel.Tests;

/// <summary>
/// MIST-006-09 contract: new logs use the M.I.S.T. prefix while old saved
/// defaults are upgraded and the View app logs action follows the configured path.
/// These are source-level regression checks, not an NLog integration test.
/// </summary>
public sealed class MISTLogBrandingContractTests
{
    private static string Source(string path)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "src/NexusMods.App/Program.cs")))
                return File.ReadAllText(Path.Combine(directory.FullName, path));
        }
        throw new DirectoryNotFoundException("Could not find M.I.S.T. source root");
    }

    [Fact]
    public void DefaultMainAndSlimLogNamesAreMist()
    {
        var settings = Source("src/NexusMods.Sdk/LoggingSettings.cs");
        Assert.Contains("MIST.app.main.current.log", settings);
        Assert.Contains("MIST.app.main.{{##}}.log", settings);
        Assert.Contains("MIST.app.slim.current.log", settings);
        Assert.Contains("MIST.app.slim.{{##}}.log", settings);
    }

    [Fact]
    public void OldConfiguredDefaultsAreMigratedBeforeLoggingStarts()
    {
        var settings = Source("src/NexusMods.Sdk/LoggingSettings.cs");
        var program = Source("src/NexusMods.App/Program.cs");
        Assert.Contains("UpgradeLegacyPaths(LoggingSettings current", settings);
        Assert.Contains("current.MainProcessLogFilePath.Equals(Legacy", settings);
        Assert.Contains("current.SlimProcessArchiveFilePath.Equals(Legacy", settings);
        Assert.Contains("Legacy(\"main.{##}.log\")", settings);
        Assert.Contains("LoggingSettings.UpgradeLegacyPaths", program);
        Assert.Contains("settingsManager.Update<LoggingSettings>", program);
        Assert.Contains("M.I.S.T. log file", program);
    }

    [Fact]
    public void ViewAppLogsUsesConfiguredCurrentMainLog()
    {
        var viewModel = Source("src/NexusMods.App.UI/Controls/TopBar/TopBarViewModel.cs");
        Assert.Contains("loggingSettings.MainProcessLogFilePath.ToPath(fileSystem)", viewModel);
        Assert.Contains("osInterop.OpenFileInDirectory(logPath)", viewModel);
    }
}
