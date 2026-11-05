using System.Reactive;
using Microsoft.Extensions.Logging;
using NexusMods.Sdk;
using NexusMods.Sdk.Settings;
using NexusMods.UI.Sdk;
using ReactiveUI;

namespace NexusMods.App.UI.Controls.MiniGameWidget.ComingSoon;

public class ComingSoonMiniGameWidgetViewModel : AViewModel<IComingSoonMiniGameWidgetViewModel>, IComingSoonMiniGameWidgetViewModel
{
    private readonly ILogger<ComingSoonMiniGameWidgetViewModel> _logger;
    private static readonly Uri ViewRoadmapUri = new("https://github.com/shamenra/MIST/blob/mist/development/docs/ROADMAP.md");

    public ComingSoonMiniGameWidgetViewModel(ILogger<ComingSoonMiniGameWidgetViewModel> logger, 
        IOSInterop osInterop,
        ISettingsManager settingsManager)
    {
        _logger = logger;

        ViewRoadmapCommand = ReactiveCommand.Create(() => osInterop.OpenUri(ViewRoadmapUri));
    }

    public ReactiveCommand<Unit, Unit> ViewRoadmapCommand { get; }

}
