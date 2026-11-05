using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using NexusMods.Abstractions.Games.FileHashes;
using NexusMods.App.UI.Controls.GameWidget;
using System.IO;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using DynamicData;
using DynamicData.Kernel;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NexusMods.Abstractions.Games;
using NexusMods.App.UI.Controls;
using NexusMods.App.UI.Helpers;
using NexusMods.App.UI.LeftMenu.Items;
using NexusMods.App.UI.Pages.Downloads;
using NexusMods.App.UI.Resources;
using NexusMods.App.UI.WorkspaceSystem;
using NexusMods.MnemonicDB.Abstractions;
using NexusMods.Sdk.Games;
using NexusMods.Sdk.NexusModsApi;
using NexusMods.UI.Sdk;
using NexusMods.UI.Sdk.Icons;
using ReactiveUI;
using R3;

namespace NexusMods.App.UI.LeftMenu.Downloads;

[UsedImplicitly]
public class DownloadsLeftMenuViewModel : AViewModel<IDownloadsLeftMenuViewModel>, IDownloadsLeftMenuViewModel
{
    public WorkspaceId WorkspaceId { get; }
    public ILeftMenuItemViewModel LeftMenuItemAllDownloads { get; }

    private ReadOnlyObservableCollection<ILeftMenuItemViewModel> _leftMenuItemsPerGameDownloads = new([]);
    public ReadOnlyObservableCollection<ILeftMenuItemViewModel> LeftMenuItemsPerGameDownloads => _leftMenuItemsPerGameDownloads;

    public DownloadsLeftMenuViewModel(
        WorkspaceId workspaceId,
        IWorkspaceController workspaceController,
        IServiceProvider serviceProvider)
    {
        WorkspaceId = workspaceId;
        var logger = serviceProvider.GetRequiredService<ILogger<DownloadsLeftMenuViewModel>>();
        var connection = serviceProvider.GetRequiredService<IConnection>();

        // All Downloads menu item
        LeftMenuItemAllDownloads = new LeftMenuItemViewModel(
            workspaceController,
            WorkspaceId,
            new PageData
            {
                FactoryId = DownloadsPageFactory.StaticId,
                Context = new DownloadsPageContext { GameScope = Optional<NexusModsGameId>.None },
            }
        )
        {
            Text = new StringComponent(Language.DownloadsLeftMenu_AllDownloads),
            Icon = IconValues.Download,
        };

        // Per-game downloads (dynamic)
        this.WhenActivated(disposable =>
        {
            Sdk.Loadouts.Loadout.ObserveAll(connection)
                .Filter(loadout => loadout.IsVisible())
                .Group(loadout => loadout.InstallationId)
                .Transform(group => group.Cache.Items.First().InstallationInstance)
                .Transform(gameInstallation => CreatePerGameDownloadItem(gameInstallation, workspaceController, workspaceId, logger))
                .DisposeMany()
                .OnUI()
                .Bind(out _leftMenuItemsPerGameDownloads)
                .Subscribe()
                .DisposeWith(disposable);
        });
    }

    /// <summary>
    /// Match the main game spine's artwork order without changing download data:
    /// Steam's square icon; otherwise its logo over a header; otherwise bundled.
    /// Games not installed via Steam always use their bundled icon.
    /// </summary>
    private static async ValueTask<Bitmap?> LoadPerGameDownloadIconAsync(
        GameInstallation installation,
        ILogger<DownloadsLeftMenuViewModel> logger)
    {
        int size = (int)ImageSizes.LeftMenuIcon.Width;
        if (installation.LocatorResult.Store == GameStore.Steam &&
            MISTSteamBuildIdentityReader.TryReadForGameFolder(
                installation.LocatorResult.Path.ToString(), out var identity))
        {
            var appId = identity!.AppId;

            if (MISTSteamArtworkResolver.TryFindIconArt(appId, out var iconPath))
            {
                try
                {
                    await using var cachedIconStream = File.OpenRead(iconPath);
                    return Bitmap.DecodeToWidth(cachedIconStream, size);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex,
                        "Cannot decode Steam Downloads icon for {GameName}; trying logo artwork",
                        installation.Game.DisplayName);
                }
            }

            if (MISTSteamArtworkResolver.TryFindLogoArt(appId, out var logoPath))
            {
                try
                {
                    string? backdropPath = MISTSteamArtworkResolver.TryFindSidebarBackdrop(
                        appId, out var foundBackdrop) ? foundBackdrop : null;
                    return MISTSteamSidebarIconRenderer.Render(logoPath, backdropPath, size);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex,
                        "Cannot render Steam Downloads logo for {GameName}; using bundled icon",
                        installation.Game.DisplayName);
                }
            }
        }

        return await ImageHelper.LoadGameIconAsync((IGame)installation.Game, size, logger);
    }

    private static ILeftMenuItemViewModel CreatePerGameDownloadItem(
        GameInstallation gameInstallation,
        IWorkspaceController workspaceController,
        WorkspaceId workspaceId,
        ILogger<DownloadsLeftMenuViewModel> logger)
    {
        var viewModel = new LeftMenuItemViewModel(
            workspaceController,
            workspaceId,
            new PageData
            {
                FactoryId = DownloadsPageFactory.StaticId,
                Context = new DownloadsPageContext { GameScope = gameInstallation.Game.NexusModsGameId },
            }
        )
        {
            Text = new StringComponent(string.Format(Language.DownloadsLeftMenu_GameSpecificDownloads, gameInstallation.Game.DisplayName)),
            Icon = IconValues.FolderEditOutline, // Initial fallback icon
        };

        // MIST-006-12D: Downloads uses the same Steam artwork as the game spine.
        // Resolve art from this exact installation; do not match games by name.
        R3.Observable.Return(gameInstallation)
            .SelectAwait((installation, _) => LoadPerGameDownloadIconAsync(installation, logger))
            .AsSystemObservable()
            .WhereNotNull()
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(bitmap => viewModel.Icon = ImageHelper.CreateIconValueFromBitmap(bitmap, IconValues.FolderEditOutline));

        return viewModel;
    }


}
