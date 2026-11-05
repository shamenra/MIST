using System;
using System.IO;
using Xunit;

namespace NexusMods.DataModel.Tests;

/// <summary>
/// MIST-006-05A source contract: Nexus-directed telemetry paths must remain
/// disconnected from M.I.S.T. startup, even for profiles with old opt-ins.
/// This is not a network packet-capture test.
/// </summary>
public sealed class MISTTelemetryDisabledContractTests
{
    private static string Source(string path)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "src/NexusMods.App/Services.cs")))
                return File.ReadAllText(Path.Combine(directory.FullName, path));
        }
        throw new DirectoryNotFoundException("Could not find MIST source root");
    }

    [Fact]
    public void OutboundTelemetryServicesAreNotRegistered()
    {
        var text = Source("src/NexusMods.App/Services.cs");
        Assert.DoesNotContain(".AddTelemetry(", text);
        Assert.DoesNotContain(".AddTrackingOld(", text);
        Assert.DoesNotContain(".AddTracking(", text);
        Assert.DoesNotContain(".AddSingleton<ITelemetryProvider", text);
    }

    [Fact]
    public void StoredLegacyTrackingPreferenceIsForcedOff()
    {
        var text = Source("src/NexusMods.App/Program.cs");
        Assert.Contains("settingsManager.Get<TrackingSettings>() with { EnableTracking = false }", text);
        Assert.DoesNotContain("Tracker.SetTracker(", text);
    }

    [Fact]
    public void TrackingOptionIsNoLongerExposedInSettings()
    {
        var text = Source("src/NexusMods.Sdk/Tracking/TrackingSettings.cs");
        Assert.DoesNotContain(".ConfigureProperty(", text);
        Assert.Contains("EnableTracking = false", text);
    }

    [Fact]
    public void WelcomePageDoesNotSolicitOrEnableTelemetry()
    {
        var xaml = Source("src/NexusMods.App.UI/Overlays/Welcome/WelcomeOverlayView.axaml");
        var viewModel = Source("src/NexusMods.App.UI/Overlays/Welcome/WelcomeOverlayViewModel.cs");
        Assert.DoesNotContain("CheckBoxAllowTelemetry", xaml);
        Assert.DoesNotContain("PrivacyPolicyExpander", xaml);
        Assert.DoesNotContain("AllowTelemetry", viewModel);
        Assert.DoesNotContain("Update<TrackingSettings>", viewModel);
    }
}
