using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reactive.Linq;
using System.Text;
using Avalonia.Controls.Models.TreeDataGrid;
using Avalonia.Platform.Storage;
using DynamicData;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NexusMods.Abstractions.Library;
using NexusMods.Abstractions.Library.Installers;
using NexusMods.Abstractions.Loadouts;
using NexusMods.Abstractions.Loadouts.Synchronizers;
using NexusMods.Abstractions.NexusModsLibrary;
using NexusMods.Abstractions.NexusModsLibrary.Models;
using NexusMods.Abstractions.NexusWebApi;
using NexusMods.Sdk.Settings;
using NexusMods.Abstractions.Telemetry;
using NexusMods.App.UI.Controls;
using NexusMods.App.UI.Dialog;
using NexusMods.App.UI.Dialog.Standard;
using NexusMods.App.UI.Dialog.Enums;
using NexusMods.App.UI.Extensions;
using NexusMods.App.UI.Overlays;
using NexusMods.App.UI.Pages.Library;
using NexusMods.App.UI.Pages.LibraryPage.Collections;
using NexusMods.App.UI.Resources;
using NexusMods.App.UI.Windows;
using NexusMods.App.UI.WorkspaceSystem;
using NexusMods.Collections;
using NexusMods.UI.Sdk.Icons;
using NexusMods.MnemonicDB.Abstractions;
using NexusMods.Networking.NexusWebApi;
using NexusMods.Networking.NexusWebApi.UpdateFilters;
using NexusMods.Paths;
using NexusMods.Sdk;
using NexusMods.Sdk.Loadouts;
using NexusMods.UI.Sdk;
using NexusMods.UI.Sdk.Dialog;
using NexusMods.UI.Sdk.Dialog.Enums;
using ObservableCollections;
using OneOf;
using R3;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using NexusMods.Sdk.Library;

namespace NexusMods.App.UI.Pages.LibraryPage;

public class LibraryViewModel : APageViewModel<ILibraryViewModel>, ILibraryViewModel
{
    private readonly IConnection _connection;
    private readonly ILibraryService _libraryService;

    public string EmptyLibrarySubtitleText { get; }

    // MIST-006-14B2B6: observations remain unowned even while shown by the native Library adapter.
    [Reactive] public IReadOnlyList<MISTDiscoveredMod> MISTDiscoveredMods { get; private set; } = Array.Empty<MISTDiscoveredMod>();
    [Reactive] public bool MISTHasDiscoveredMods { get; private set; }
    [Reactive] public string MISTDiscoverySummary { get; private set; } = "No discovery results yet";

    // MIST-006-14B2B5.2: observed byte total, including possible settings/logs.
    // Display-only; never used for matching or mod ownership decisions.
    private static string FormatMISTObservedSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        var magnitude = (double)bytes;
        var units = new[] { "B", "KiB", "MiB", "GiB", "TiB" };
        var unit = 0;
        while (magnitude >= 1024 && unit < units.Length - 1)
        {
            magnitude /= 1024;
            unit++;
        }
        return $"{magnitude:0.#} {units[unit]}";
    }

    private void SetMISTDiscoveryResults(
        NexusMods.DataModel.ModDiscovery.MISTModDiscoveryPipeline.Report report,
        bool cached,
        string? sourceDiscoveryPath = null)
    {
        // Preserve the scanner's grouping. A component relationship is NOT an ownership merge.
        var candidates = report.Inventory.CandidateGroups.Select(group =>
        {
            var versions = report.IdentityHints
                .Where(h => h.GroupKey == group.Key && h.Kind == "embedded-version")
                .Select(h => h.Value)
                .Distinct(StringComparer.Ordinal)
                .Take(3)
                .ToArray();
            var versionText = versions.Length == 0
                ? "Version unknown"
                : "Version hint: " + string.Join(", ", versions) + " (unverified)";
            var evidenceText = group.Confidence == "structural-candidate"
                ? "Structural candidate · Unmanaged"
                : "Weak structural candidate · Unmanaged";
            return new MISTDiscoveredMod(group.Key, group.SuggestedName,
                $"{group.Files.Count} files", versionText, evidenceText,
                string.Join("\n", group.Files.Select(file => file.Path)),
                FormatMISTObservedSize(group.Files.Sum(file => Math.Max(0L, file.SizeBytes))),
                group.Files.Sum(file => Math.Max(0L, file.SizeBytes)));
        }).ToArray();

        // MIST-006-14B2B8.4: import only a strictly source-bound, complete,
        // read-only display proposal. Failed or stale proposal => original 28 groups.
        var reconciled = sourceDiscoveryPath is null ? null :
            MISTReconciledDisplay.TryLoad(report, sourceDiscoveryPath);
        if (reconciled is not null) candidates = reconciled;

        MISTDiscoveredMods = candidates;
        Adapter.SetMISTDiscoveredMods(candidates); // display-only overlay; no DB/loadout mutations
        MISTHasDiscoveredMods = candidates.Length > 0;
        var modRows = candidates.Length - (reconciled is not null &&
            report.Inventory.UnattributedFiles.Count > 0 ? 1 : 0);
        var rowDescription = reconciled is null
            ? $"{candidates.Length} candidate groups"
            : $"{modRows} proposed mod entries (from {report.Inventory.CandidateGroups.Count} groups)";
        MISTDiscoverySummary =
            $"{(cached ? "Saved scan · " : "Fresh scan · ")}" +
            $"{rowDescription} · {report.Inventory.InstalledFileCount} files · " +
            $"{report.Inventory.UnattributedFiles.Count} unassociated · 0 adopted by this scan";
    }


    // MIST-006-14B2B5.1: no adoption, synchronization, file writes or database calls.
    // This review is deliberately not connected to the normal Library Install action.
    public async System.Threading.Tasks.Task MISTReviewDiscoveredModAsync(MISTDiscoveredMod candidate)
    {
        var logger = _serviceProvider.GetRequiredService<ILogger<LibraryViewModel>>();
        logger.LogInformation("MIST-006-14B2B5.1 REVIEW candidate={Candidate} files={Count} action=read-only",
            candidate.GroupKey, candidate.FileCountText);

        var body = new StringBuilder();
        body.AppendLine("This is a detected mod candidate, not a managed Library item.");
        body.AppendLine("M.I.S.T. has not adopted, moved, installed, removed, or modified these files.");
        body.AppendLine();
        body.AppendLine("Candidate: " + candidate.Name);
        body.AppendLine("Recognition: " + candidate.RecognitionText);
        body.AppendLine(candidate.VersionText);
        body.AppendLine("Files: " + candidate.FileCountText);
        body.AppendLine();
        body.AppendLine("Game-relative files (first 12):");
        var filePaths = candidate.FilePathsText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var path in filePaths.Take(12)) body.AppendLine("  " + path);
        if (filePaths.Length > 12)
            body.AppendLine($"  ... and {filePaths.Length - 12} more");
        body.AppendLine();
        body.AppendLine("Import into management is not enabled in this milestone.");

        var dialog = DialogFactory.CreateStandardDialog(
            title: "Review discovered mod — " + candidate.Name,
            new StandardDialogParameters { Text = body.ToString() },
            buttonDefinitions: [DialogStandardButtons.Close]);
        await WindowManager.ShowDialog(dialog, DialogWindowType.Modal);
    }

    private void RestoreMISTDiscoveryDisplay()
    {
        // Only the saved output of our own in-game scanner is read, never a manager DB.
        // This cache is for DISPLAY ONLY and can be stale; it never grants file authority.
        if (_loadout.InstallationInstance.Game.GameId.ToString() != "RedEngine.Cyberpunk2077") return;
        try
        {
            var state = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
            if (string.IsNullOrWhiteSpace(state))
                state = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "state");
            var directory = System.IO.Path.Combine(state,
                "ModIntegrationAndSortingTechnology", "mod-discovery");
            if (!System.IO.Directory.Exists(directory)) return;

            var gameRoot = System.IO.Path.GetFullPath(
                _loadout.InstallationInstance.Locations[NexusMods.Sdk.Games.LocationId.Game].Path.ToString());

            foreach (var path in System.IO.Directory.EnumerateFiles(directory, "cyberpunk-*.json")
                         .OrderByDescending(System.IO.File.GetLastWriteTimeUtc).Take(8))
            {
                var info = new System.IO.FileInfo(path);
                if (info.Length > 3 * 1024 * 1024) continue;
                var json = System.IO.File.ReadAllText(path);
                var report = System.Text.Json.JsonSerializer.Deserialize<
                    NexusMods.DataModel.ModDiscovery.MISTModDiscoveryPipeline.Report>(json,
                    new System.Text.Json.JsonSerializerOptions(
                        System.Text.Json.JsonSerializerDefaults.Web));
                if (report?.Inventory?.GameRoot is null) continue;
                if (!string.Equals(System.IO.Path.GetFullPath(report.Inventory.GameRoot), gameRoot,
                        StringComparison.Ordinal)) continue;
                SetMISTDiscoveryResults(report, cached: true, sourceDiscoveryPath: path);
                return;
            }
        }
        catch (Exception exception) when (exception is System.IO.IOException or
                                         UnauthorizedAccessException or
                                         System.Text.Json.JsonException or
                                         ArgumentException or
                                         NotSupportedException)
        {
            _serviceProvider.GetRequiredService<ILogger<LibraryViewModel>>()
                .LogWarning(exception, "MIST-006-14B2B5 saved display unavailable");
        }
    }



    public ReactiveCommand<Unit> UpdateAllCommand { get; }
    public ReactiveCommand<Unit> RefreshUpdatesCommand { get; }
    public ReactiveCommand<Unit> InstallSelectedItemsCommand { get; }

    public ReactiveCommand<Unit> InstallSelectedItemsWithAdvancedInstallerCommand { get; }

    public ReactiveCommand<Unit> UpdateSelectedItemsCommand { get; }
    
    public ReactiveCommand<Unit> UpdateAndKeepOldSelectedItemsCommand { get; }

    public ReactiveCommand<Unit> RemoveSelectedItemsCommand { get; }
    
    public ReactiveCommand<Unit> DeselectItemsCommand { get; }

    public ReactiveCommand<Unit> OpenFilePickerCommand { get; }

    public ReactiveCommand<Unit> OpenNexusModsCommand { get; }
    public ReactiveCommand<Unit> OpenNexusModsCollectionsCommand { get; }

    [Reactive] public int SelectionCount { get; private set; }
    
    [Reactive] public int UpdatableSelectionCount { get; private set; }
    
    [Reactive] public bool HasAnyUpdatesAvailable { get; private set; }

    [Reactive] public bool IsUpdatingAll { get; private set; }

    [Reactive] public IStorageProvider? StorageProvider { get; set; }

    private readonly IServiceProvider _serviceProvider;
    private readonly ILibraryItemInstaller _advancedInstaller;
    private readonly IGameDomainToGameIdMappingCache _gameIdMappingCache;
    private readonly Loadout.ReadOnly _loadout;
    private readonly IModUpdateService _modUpdateService;
    private readonly ILoginManager _loginManager;
    private readonly NexusModsLibrary _nexusModsLibrary;
    private readonly TemporaryFileManager _temporaryFileManager;
    private readonly IWindowNotificationService _notificationService;
    private readonly ILoadoutManager _loadoutManager;

    public LibraryTreeDataGridAdapter Adapter { get; }
    private ReadOnlyObservableCollection<ICollectionCardViewModel> _collections = new([]);
    public ReadOnlyObservableCollection<ICollectionCardViewModel> Collections => _collections;

    private ReadOnlyObservableCollection<InstallationTarget> _installationTargets = new([]);
    public ReadOnlyObservableCollection<InstallationTarget> InstallationTargets => _installationTargets;

    [Reactive] public InstallationTarget? SelectedInstallationTarget { get; set; }

    public LibraryViewModel(
        IWindowManager windowManager,
        IServiceProvider serviceProvider,
        IGameDomainToGameIdMappingCache gameIdMappingCache,
        LoadoutId loadoutId) : base(windowManager)
    {
        _serviceProvider = serviceProvider;
        _gameIdMappingCache = gameIdMappingCache;
        _libraryService = serviceProvider.GetRequiredService<ILibraryService>();
        _nexusModsLibrary = serviceProvider.GetRequiredService<NexusModsLibrary>();
        _connection = serviceProvider.GetRequiredService<IConnection>();
        _modUpdateService = serviceProvider.GetRequiredService<IModUpdateService>();
        _loginManager = serviceProvider.GetRequiredService<ILoginManager>();
        _temporaryFileManager = serviceProvider.GetRequiredService<TemporaryFileManager>();
        _notificationService = serviceProvider.GetRequiredService<IWindowNotificationService>();
        _loadoutManager = serviceProvider.GetRequiredService<ILoadoutManager>();

        var collectionDownloader = new CollectionDownloader(serviceProvider);
        var tileImagePipeline = ImagePipelines.GetCollectionTileImagePipeline(serviceProvider);
        var userAvatarPipeline = ImagePipelines.GetUserAvatarPipeline(serviceProvider);

        var loadout = Loadout.Load(_connection.Db, loadoutId);
        var libraryFilter = new LibraryFilter(loadout, loadout.InstallationInstance.Game);
        
        Adapter = new LibraryTreeDataGridAdapter(serviceProvider, libraryFilter);

        _advancedInstaller = serviceProvider.GetRequiredKeyedService<ILibraryItemInstaller>("AdvancedManualInstaller_Direct");

        TabTitle = Language.LibraryPageTitle;
        TabIcon = IconValues.LibraryOutline;
        
        _loadout = Loadout.Load(_connection.Db, loadoutId.Value);
        var game = _loadout.InstallationInstance.Game;

        EmptyLibrarySubtitleText = string.Format(Language.FileOriginsPageViewModel_EmptyLibrarySubtitleText, game.DisplayName);
        RestoreMISTDiscoveryDisplay();

        DeselectItemsCommand = new ReactiveCommand<Unit>(_ =>
        {
            Adapter.ClearSelection();
        });

        RefreshUpdatesCommand = new ReactiveCommand<Unit>(
            executeAsync: (_, token) => RefreshUpdates(token),
            awaitOperation: AwaitOperation.Switch
        );

        // Create an observable that determines when UpdateAll can execute
        var canUpdateAll = this.WhenAnyValue(vm => vm.IsUpdatingAll)
            .ToObservable()
            .Select(isUpdating => !isUpdating);

        UpdateAllCommand = canUpdateAll.ToReactiveCommand<Unit>(
            executeAsync: (_, cancellationToken) => UpdateAllItems(cancellationToken),
            awaitOperation: AwaitOperation.Sequential,
            initialCanExecute: true,
            configureAwait: false
        );

        // Discovered observations can be selected for navigation, but never grant
        // management commands such as install, delete, or advanced install.
        var hasSelection = Adapter.SelectedModels
            .ObserveChanged()
            .Select(_ => GetSelectedIds().Length > 0);
        
        InstallSelectedItemsCommand = hasSelection.ToReactiveCommand<Unit>(
            executeAsync: (_, cancellationToken) => InstallSelectedItems(useAdvancedInstaller: false, cancellationToken),
            awaitOperation: AwaitOperation.Parallel,
            initialCanExecute: false,
            configureAwait: false
        );

        InstallSelectedItemsWithAdvancedInstallerCommand = hasSelection.ToReactiveCommand<Unit>(
            executeAsync: (_, cancellationToken) => InstallSelectedItems(useAdvancedInstaller: true, cancellationToken),
            awaitOperation: AwaitOperation.Parallel,
            initialCanExecute: false,
            configureAwait: false
        );

        var hasUpdatableSelection = Adapter.SelectedModels
            .ObserveCountChanged()
            .Select(_ => GetSelectedModelsWithUpdates().Any());

        var canUpdateSelected = R3.Observable.CombineLatest(
            hasUpdatableSelection,
            this.WhenAnyValue(vm => vm.IsUpdatingAll).ToObservable(),
            (hasUpdates, isUpdatingAll) => hasUpdates && !isUpdatingAll
        );

        UpdateSelectedItemsCommand = canUpdateSelected.ToReactiveCommand<Unit>(
            executeAsync: (_, cancellationToken) => UpdateSelectedItems(cancellationToken),
            awaitOperation: AwaitOperation.Parallel,
            initialCanExecute: false,
            configureAwait: false
        );

        var canUpdateAndKeepOldSelected = R3.Observable.CombineLatest(
            hasUpdatableSelection,
            this.WhenAnyValue(vm => vm.IsUpdatingAll).ToObservable(),
            (hasUpdates, isUpdatingAll) => hasUpdates && !isUpdatingAll
        );

        UpdateAndKeepOldSelectedItemsCommand = canUpdateAndKeepOldSelected.ToReactiveCommand<Unit>(
            executeAsync: (_, cancellationToken) => UpdateAndKeepOldSelectedItems(cancellationToken),
            awaitOperation: AwaitOperation.Parallel,
            initialCanExecute: false,
            configureAwait: false
        );

        RemoveSelectedItemsCommand = hasSelection.ToReactiveCommand<Unit>(
            executeAsync: (_, cancellationToken) => RemoveSelectedItems(cancellationToken),
            awaitOperation: AwaitOperation.Parallel,
            initialCanExecute: false,
            configureAwait: false
        );

        var canUseFilePicker = this.WhenAnyValue(vm => vm.StorageProvider)
            .ToObservable()
            .WhereNotNull()
            .Select(x => x.CanOpen);

        OpenFilePickerCommand = canUseFilePicker.ToReactiveCommand<Unit>(
            executeAsync: (_, cancellationToken) => AddFilesFromDisk(StorageProvider!, cancellationToken),
            awaitOperation: AwaitOperation.Parallel,
            initialCanExecute: true,
            configureAwait: false
        );

        var osInterop = serviceProvider.GetRequiredService<IOSInterop>();
        OpenNexusModsCommand = new ReactiveCommand<Unit>(execute: _ =>
        {
            var gameDomain = _gameIdMappingCache[game.NexusModsGameId.Value];
            var gameUri = NexusModsUrlBuilder.GetGameUri(gameDomain);
            osInterop.OpenUri(gameUri);
        });

        OpenNexusModsCollectionsCommand = new ReactiveCommand<Unit>(execute: _ =>
        {
            var gameDomain = _gameIdMappingCache[game.NexusModsGameId.Value];
            var gameUri = NexusModsUrlBuilder.GetBrowseCollectionsUri(gameDomain);
            osInterop.OpenUri(gameUri);
        });

        this.WhenActivated(disposables =>
        {
            Disposable.Create(this, static vm => vm.StorageProvider = null).AddTo(disposables);
            Adapter.Activate().AddTo(disposables);

            // MIST-006-14B2B6: this action ONLY opens the existing evidence review dialog.
            Adapter.MISTReviewRequested.SubscribeAwait(
                onNextAsync: async (candidate, _) => await MISTReviewDiscoveredModAsync(candidate),
                awaitOperation: AwaitOperation.Sequential,
                configureAwait: false
            ).AddTo(disposables);

            Adapter.MessageSubject.SubscribeAwait(
                onNextAsync: async (message, cancellationToken) =>
                {
                    await message.Match(
                        async installMessage =>
                        {
                            foreach (var id in installMessage.Ids)
                            {
                                var libraryItem = LibraryItem.Load(_connection.Db, id);
                                if (!libraryItem.IsValid()) continue;
                                await InstallLibraryItem(libraryItem, _loadout, GetInstallationTarget(), cancellationToken);
                            }
                        },
                        async updateAndReplaceMessage => await HandleUpdateAndReplaceMessage(updateAndReplaceMessage, cancellationToken),
                        async updateAndKeepOldMessage => await HandleUpdateAndKeepOldMessage(updateAndKeepOldMessage, cancellationToken),
                        async viewChangelogMessage => await HandleViewChangelogMessage(viewChangelogMessage, cancellationToken),
                        async viewModPageMessage => await HandleViewModPageMessage(viewModPageMessage, cancellationToken),
                        async hideUpdatesMessage => await HandleHideUpdatesMessage(hideUpdatesMessage, cancellationToken),
                        async deleteItemMessage => await HandleDeleteItemMessage(deleteItemMessage, cancellationToken)
                    );
                },
                awaitOperation: AwaitOperation.Parallel,
                configureAwait: false
            ).AddTo(disposables);

            LoadoutQueries2.MutableCollections(_connection, _loadout.LoadoutId)
                .Observe(r => r.GroupId)
                .Transform(r => new InstallationTarget(r.GroupId, r.Name))
                .SortBy(target => target.Id.Value.Value)
                .DistinctUntilChanged()
                .Bind(out _installationTargets)
                .Subscribe()
                .AddTo(disposables);

            CollectionRevisionMetadata.ObserveAll(_connection)
                .FilterImmutable(revision => revision.Collection.GameId == game.NexusModsGameId)
                .OnUI()
                .Transform(ICollectionCardViewModel (revision) => new CollectionCardViewModel(
                    collectionDownloader: collectionDownloader,
                    tileImagePipeline: tileImagePipeline,
                    userAvatarPipeline: userAvatarPipeline,
                    windowManager: WindowManager,
                    workspaceId: WorkspaceId,
                    revision: revision,
                    targetLoadout: _loadout)
                )
                .Bind(out _collections)
                .Subscribe()
                .AddTo(disposables);
            
            // Update the selection count based on the selected models
            Adapter.SelectedModels
                .ObserveChanged()
                .ObserveOnUIThreadDispatcher()
                .Subscribe(_ => 
                {
                    SelectionCount = GetSelectedIds().Length;
                    UpdatableSelectionCount = GetSelectedModelsWithUpdates().Count();
                })
                .AddTo(disposables);

            // Subscribe to mod update service to automatically track "has any updates" state
            _modUpdateService.HasAnyUpdatesObservable()
                .OnUI()
                .Subscribe(hasUpdates => HasAnyUpdatesAvailable = hasUpdates)
                .AddTo(disposables);

            // Auto check updates on entering library.
            R3.Observable
                .Return(Unit.Default)
                .ObserveOnThreadPool()
                .Subscribe(this,static (_, self) => self.RefreshUpdatesCommand.Execute(Unit.Default))
                .AddTo(disposables);
        });
    }

    private async ValueTask HandleUpdateAndReplaceMessage(UpdateAndReplaceMessage updateAndReplaceMessage, CancellationToken cancellationToken)
    {
        var isPremium = _loginManager.IsPremium;
        if (!isPremium)
        {
            await UpdateAndReplaceForMultiModPagesFreeOnly(cancellationToken, [updateAndReplaceMessage.Updates]);
        }
        else
        {
            await UpdateAndReplaceForMultiModPagesPremiumOnly(cancellationToken, [updateAndReplaceMessage.Updates]);
        }
    }

    private async ValueTask UpdateAndReplaceForMultiModPagesFreeOnly(CancellationToken cancellationToken, IEnumerable<ModUpdatesOnModPage> updatesOnPageCollection)
    {
        // Note(sewer): Per design, in the future this will expand the mod rows.
        //              But, due to the TreeDataGrid bug, we can't do that today, yet.
        // Instead update and keep old for free users.
        UpdateAndKeepOldFree(updatesOnPageCollection);
    }

    private async ValueTask UpdateAndReplaceForMultiModPagesPremiumOnly(CancellationToken cancellationToken, IEnumerable<ModUpdatesOnModPage> updatesOnPageCollection)
    {
        // Collect all library items that will be updated by linking with file metadata.
        var newestToCurrentMapping = new Dictionary<NexusModsFileMetadata.ReadOnly, List<NexusModsFileMetadata.ReadOnly>>();
        foreach (var updatesOnPage in updatesOnPageCollection)
            updatesOnPage.NewestToCurrentFileMapping(newestToCurrentMapping);
        
        await UpdateAndReplaceForNewestToCurrentMappingPremiumOnly(cancellationToken, newestToCurrentMapping);
    }

    private async ValueTask UpdateAndReplaceForNewestToCurrentMappingPremiumOnly(CancellationToken cancellationToken, Dictionary<NexusModsFileMetadata.ReadOnly, List<NexusModsFileMetadata.ReadOnly>> newestToCurrentMapping)
    {
        var libraryItemsToUpdate = new List<LibraryItem.ReadOnly>();
        foreach (var (_, currentFiles) in newestToCurrentMapping)
        {
            foreach (var currentFile in currentFiles)
            {
                // Find existing library items for this file metadata
                var existingLibraryItems = NexusModsLibraryItem.FindByFileMetadata(_connection.Db, currentFile);
                foreach (var existingLibraryFile in existingLibraryItems)
                {
                    var currentLibraryItem = existingLibraryFile.AsLibraryItem();
                    libraryItemsToUpdate.Add(currentLibraryItem);
                }
            }
        }

        // Find all affected loadouts
        var collectionsAffected = _libraryService.CollectionsWithLibraryItems(libraryItemsToUpdate, excludeReadOnlyCollections: true);
        var affectedCollectionCount = collectionsAffected.Count;

        // If there is more than 1 non-readonly affected collection, show confirmation dialog
        if (affectedCollectionCount >= 2)
        {
            (ButtonDefinitionId updateButtonId, StandardDialogResult dialogResult) = await ShowInstalledInMultipleCollectionsDialog(collectionsAffected, affectedCollectionCount);
            if (dialogResult.ButtonId != updateButtonId)
            {
                // User cancelled, don't proceed with update
                return;
            }
        }

        var concurrencyLimit = _serviceProvider.GetRequiredService<ISettingsManager>().Get<DownloadSettings>().MaxParallelDownloads;
        var results = await ProcessDownloadsInParallel(newestToCurrentMapping, concurrencyLimit, cancellationToken);

        // Some (but not all) downloads failed, so show relevant dialog.
        if (results.DownloadErrors.Count > 0)
            await ShowSomeDownloadsFailedDialog(results.DownloadErrors, newestToCurrentMapping, cancellationToken);

        // Some installs failed, ask if they want to keep old files
        if (results.LibraryItemReplacementResults.Count(x => x.InstallResult != LibraryItemReplacementResult.Success) > 0)
            await ShowSomeInstallsFailedDialog(results.LibraryItemReplacementResults);

        // Remove all old library items where the install failed.
        var successfullyInstalledItems = results.LibraryItemReplacementResults.Where(x => x.InstallResult == LibraryItemReplacementResult.Success);
        await RemoveOldLibraryItemsIfNotInReadOnlyCollections(successfullyInstalledItems);
    }
    
    private async Task ShowSomeDownloadsFailedDialog(
        ConcurrentBag<DownloadError> downloadErrors,
        Dictionary<NexusModsFileMetadata.ReadOnly, List<NexusModsFileMetadata.ReadOnly>> newestToCurrentMapping,
        CancellationToken ct)
    {
        var modsWhichFailed = new StringBuilder();
        foreach (var err in downloadErrors)
            modsWhichFailed.AppendLine(err.File.Name);

        var cancelFailedDownloads = new DialogButtonDefinition(
            Text: Language.Library_Update_SomeDownloadsFailed_Option_Cancel,
            Id: ButtonDefinitionId.Cancel,
            ButtonAction: ButtonAction.Reject,
            ButtonStyling: ButtonStyling.None
        );
        var retryFailedDownloads = new DialogButtonDefinition(
            Text: Language.Library_Update_SomeDownloadsFailed_Option_Retry,
            Id: ButtonDefinitionId.Accept,
            ButtonAction: ButtonAction.Accept,
            ButtonStyling: ButtonStyling.Default
        );

        var allFailedDialog = DialogFactory.CreateStandardDialog(
            title: Language.Library_Update_SomeDownloadsFailed_Title,
            new StandardDialogParameters()
            {
                Text = string.Format(Language.Library_Update_SomeDownloadsFailed_Description, modsWhichFailed),
            },
            buttonDefinitions: [cancelFailedDownloads, retryFailedDownloads]
        );

        var result = await WindowManager.ShowDialog(allFailedDialog, DialogWindowType.Modal);
        if (result.ButtonId == retryFailedDownloads.Id)
        {
            var downloadErrorsTable = downloadErrors.Select(x => x.File).ToHashSet();
            var failedDownloads = newestToCurrentMapping.Where(x => downloadErrorsTable.Contains(x.Key)).ToDictionary(x => x.Key, x => x.Value);
            await UpdateAndReplaceForNewestToCurrentMappingPremiumOnly(ct, failedDownloads);
        }
    }

    private async Task ShowSomeInstallsFailedDialog(ConcurrentBag<DownloadedProcessingResult> libraryItemReplacementResults)
    {
        var failedToInstallItems = new List<LibraryItem.ReadOnly>();
        foreach (var replaceResult in libraryItemReplacementResults)
        {
            if (replaceResult.InstallResult != LibraryItemReplacementResult.Success)
                failedToInstallItems.Add(replaceResult.NewItem);
        }

        var modNames = string.Join(Environment.NewLine, failedToInstallItems.Select(failed => failed.Name));
        var description = string.Format(Language.Library_Update_SomeInstallsFailed_Description, modNames);

        var deleteButtonId = ButtonDefinitionId.From("Delete");
        var keepButtonId = ButtonDefinitionId.From("Keep");

        var dialog = DialogFactory.CreateStandardDialog(
            title: Language.Library_Update_SomeInstallsFailed_Title,
            new StandardDialogParameters()
            {
                Text = description,
            },
            buttonDefinitions: [
                new DialogButtonDefinition(
                    Language.Library_Update_SomeInstallsFailed_DeleteFiles,
                    deleteButtonId,
                    ButtonAction.Accept,
                    ButtonStyling.Destructive
                ),
                new DialogButtonDefinition(
                    Language.Library_Update_SomeInstallsFailed_KeepFiles,
                    keepButtonId,
                    ButtonAction.Reject,
                    ButtonStyling.None
                )
            ]
        );

        var result = await WindowManager.ShowDialog(dialog, DialogWindowType.Modal);
        // User chose to delete the failed update files
        if (result.ButtonId == deleteButtonId)
            await _libraryService.RemoveLibraryItems(failedToInstallItems);
    }

    private async Task<(ButtonDefinitionId updateButtonId, StandardDialogResult dialogResult)> ShowInstalledInMultipleCollectionsDialog(IReadOnlyDictionary<CollectionGroup.ReadOnly, IReadOnlyList<(LibraryItem.ReadOnly libraryItem, LibraryLinkedLoadoutItem.ReadOnly linkedItem)>> collectionsAffected, int affectedCollectionCount)
    {
        var updateButtonId = ButtonDefinitionId.From("Update");
        var cancelButtonId = ButtonDefinitionId.From("Cancel");

        var dialogDesc = new StringBuilder()
            .AppendLine(Language.Library_Update_InstalledInMultipleCollections_Description1)
            .AppendLine();

        var collectionsGroupedByLoadout = collectionsAffected.Select(collection => collection.Key.AsLoadoutItemGroup().AsLoadoutItem())
            .GroupBy(item => item.Loadout.Name)
            .OrderBy(loadoutGroup => loadoutGroup.Key);

        foreach (var loadoutGroup in collectionsGroupedByLoadout)
        {
            dialogDesc.AppendLine($"{loadoutGroup.Key}:");
            foreach (var collection in loadoutGroup)
                dialogDesc.AppendLine($"{collection.Name}");
            
            dialogDesc.AppendLine();
        }

        dialogDesc.AppendLine(Language.Library_Update_InstalledInMultipleCollections_Description2);

        var confirmDialog = DialogFactory.CreateStandardDialog(
            title: Language.Library_Update_InstalledInMultipleCollections_Title,
            new StandardDialogParameters()
            {
                Text = dialogDesc.ToString(),
            },
            buttonDefinitions: [
                new DialogButtonDefinition(
                        Language.Library_Update_InstalledInMultipleCollections_Cancel,
                        cancelButtonId,
                        ButtonAction.Reject
                    ),
                    new DialogButtonDefinition(
                        string.Format(Language.Library_Update_InstalledInMultipleCollections_Ok, affectedCollectionCount),
                        updateButtonId,
                        ButtonAction.Accept,
                        ButtonStyling.Primary
                    )
            ]
        );

        var dialogResult = await WindowManager.ShowDialog(confirmDialog, DialogWindowType.Modal);
        return (updateButtonId, dialogResult);
    }

    /// <summary>
    /// This downloads a list of mods marked by <see cref="newestToCurrentMapping"/>.
    /// Every mod is installed separately into the library (as a separate transaction).
    /// </summary>
    /// <param name="newestToCurrentMapping">The items to process.</param>
    /// <param name="concurrencyLimit">Amount of items to process at the same time.</param>
    /// <param name="cancellationToken">The token that can be used to cancel this operation.</param>
    /// <returns>A struct containing all the processing results including successful downloads, errors, and mappings.</returns>
    private async Task<DownloadProcessingResults> ProcessDownloadsInParallel(
        Dictionary<NexusModsFileMetadata.ReadOnly, List<NexusModsFileMetadata.ReadOnly>> newestToCurrentMapping,
        int concurrencyLimit,
        CancellationToken cancellationToken)
    {
        var libraryItemReplacementResults = new ConcurrentBag<DownloadedProcessingResult>();
        var downloadErrors = new ConcurrentBag<DownloadError>();

        using var semaphore = new SemaphoreSlim(concurrencyLimit);
        var tasks = new List<Task>();

        // Helper function to process a single download with error handling
        async Task ProcessDownload(SemaphoreSlim sema, NexusModsFileMetadata.ReadOnly newestFile, List<NexusModsFileMetadata.ReadOnly> currentFiles)
        {
            var isTaken = false;
            try
            {
                isTaken = await sema.WaitAsync(Timeout.Infinite, cancellationToken);

                // Try a download.
                await using var tempPath = _temporaryFileManager.CreateFile();
                var job = await _nexusModsLibrary.CreateDownloadJob(tempPath, newestFile, cancellationToken: cancellationToken);
                var libraryFile = await _libraryService.AddDownload(job);
                var newLibraryItem = libraryFile.AsLibraryItem();

                // Map each current file to the new file
                foreach (var currentFile in currentFiles)
                {
                    // Find existing library items for this file metadata
                    var existingLibraryItems = NexusModsLibraryItem.FindByFileMetadata(_connection.Db, currentFile);
                    foreach (var existingLibraryFile in existingLibraryItems) // Should only be one.
                    {
                        var currentLibraryItem = existingLibraryFile.AsLibraryItem();

                        // Try to install the item right away
                        var options = new ReplaceLibraryItemOptions { IgnoreReadOnlyCollections = true };
                        var result = await _libraryService.ReplaceLinkedItemsInAllLoadouts(currentLibraryItem, newLibraryItem, options);
                        libraryItemReplacementResults.Add(new DownloadedProcessingResult()
                        {
                            OldItem = currentLibraryItem,
                            NewItem = newLibraryItem,
                            InstallResult = result,
                        });

                        if (result == LibraryItemReplacementResult.Success)
                            _notificationService.ShowToast(string.Format(Language.ToastNotification_Mod_updated____0_, newLibraryItem.Name), ToastNotificationVariant.Success);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Ignored.
            }
            catch (Exception ex)
            {
                downloadErrors.Add(new DownloadError()
                {
                    File = newestFile,
                    Error = ex,
                });
            }
            finally
            {
                if (isTaken)
                    sema.Release();
            }
        }

        // Create tasks for all downloads
        foreach (var (newestFile, currentFiles) in newestToCurrentMapping)
        {
            tasks.Add(ProcessDownload(semaphore, newestFile, currentFiles));
        }

        // Wait for all tasks to complete
        await Task.WhenAll(tasks);

        return new DownloadProcessingResults(
            libraryItemReplacementResults,
            downloadErrors
        );
    }

    private async ValueTask HandleUpdateAndKeepOldMessage(UpdateAndKeepOldMessage updateAndKeepOldMessage, CancellationToken cancellationToken)
    {
        var updatesOnPage = updateAndKeepOldMessage.Updates;
        
        // Note(sewer)
        // If the user is a free user, they have to go to the website due to API restrictions.
        // For premium, we can start a download directly.
        var isPremium = _loginManager.IsPremium;
        if (!isPremium)
        {
            /*
               // Note(sewer): The commented code here is the correct behaviour
               // as intended per the phase one design. We temporarily need to alter
               // this behaviour due to the TreeDataGrid bug. When TreeDataGrid
               // is fixed, we can revert.

               // If there are multiple mods, we expand the row
               var treeNode = updateMessage.TreeNode;
               if (updatesOnPage.NumberOfModFilesToUpdate > 1)
               {
                   treeNode.IsExpanded = true; // 👈 TreeDataGrid bug. Doesn't handle PropertyChanged right.
               }
               else
               {
                   // Otherwise send them to the download page!!
                   var latestFile = updatesOnPage.NewestFile();
                   var modFileUrl = NexusModsUrlBuilder.CreateModFileDownloadUri(latestFile.Uid.FileId, latestFile.Uid.GameId);
                   var osInterop = _serviceProvider.GetRequiredService<IOSInterop>();
                   await osInterop.OpenUrl(modFileUrl, cancellationToken: cancellationToken);
               }
            */

            UpdateAndKeepOldFree([updatesOnPage]);
        }
        else
        {
            await UpdateAndKeepOldPremium([updatesOnPage], cancellationToken);
        }
    }

    private void UpdateAndKeepOldFree(IEnumerable<ModUpdatesOnModPage> updatesOnPages)
    {
        // Aggregate all unique files across all mod pages
        var newestToCurrentMapping = new Dictionary<NexusModsFileMetadata.ReadOnly, List<NexusModsFileMetadata.ReadOnly>>();
        foreach (var updatesOnPage in updatesOnPages)
            updatesOnPage.NewestToCurrentFileMapping(newestToCurrentMapping);
        
        // Open download page for every unique file
        var osInterop = _serviceProvider.GetRequiredService<IOSInterop>();
        foreach (var newestFile in newestToCurrentMapping.Keys)
        {
            var uri = NexusModsUrlBuilder.GetFileDownloadUri(newestFile.ModPage.GameDomain, newestFile.ModPage.Uid.ModId, newestFile.Uid.FileId, useNxmLink: true, campaign: NexusModsUrlBuilder.CampaignUpdates);
            osInterop.OpenUri(uri);
        }
    }

    private async Task UpdateAndKeepOldPremium(IEnumerable<ModUpdatesOnModPage> updatesOnPages, CancellationToken cancellationToken)
    {
        // Aggregate all unique files across all mod pages
        var newestToCurrentMapping = new Dictionary<NexusModsFileMetadata.ReadOnly, List<NexusModsFileMetadata.ReadOnly>>();
        foreach (var updatesOnPage in updatesOnPages)
            updatesOnPage.NewestToCurrentFileMapping(newestToCurrentMapping);
        
        // Note(sewer): There's usually just 1 file in like 99% of the cases here
        //              so no need to optimize around file reuse and TemporaryFileManager.
        foreach (var newestFile in newestToCurrentMapping.Keys)
        {
            await using var tempPath = _temporaryFileManager.CreateFile();
            var job = await _nexusModsLibrary.CreateDownloadJob(tempPath, newestFile, cancellationToken: cancellationToken);
            await _libraryService.AddDownload(job);
        }
    }

    private ValueTask HandleViewChangelogMessage(ViewChangelogMessage viewChangelogMessage, CancellationToken cancellationToken)
    {
/*
Note(sewer): This method currently sends you to the mod page due to technical limitations.
A summarised quote/explanation from myself below:

- On the site, there is no true 'per file' level changelog, but only a changelog at the mod page level currently.
    - The widget which shows the changes e.g. https://www.nexusmods.com/Core/Libs/Common/Widgets/ModChangeLogPopUp?mod_id=2347&game_id=1704&version=10.0.1 
      cannot be explicitly opened via hyperlink (due to a recent change on the site to block this).
    - From observation, I think those are automatically added when the user sets the version of the file to be the 
      same as the version of a changelog item made on the mod page.
- For rendering it myself: GraphQL API has a `changelogText` field for mod files.
    - When present, the values are listed in the form `#<ModChangelog:0x00007f060e21e880>` .
    - But there's no ModChangelog type or field anywhere in the autogenerated API docs.
    - I imagine you can manually hack this together by using V1 API, but code would be quickly obsolete.
- Some mod authors put the changelog in the file description; rather than on page level changelog.

After asking design, we're choosing to simply open the mod page for now.
*/
        return viewChangelogMessage.Id.Match(
            modPageMetadataId => OpenModPage(modPageMetadataId),
            libraryItemId => OpenModPage(new NexusModsLibraryItem.ReadOnly(_connection.Db, libraryItemId).ModPageMetadataId)
        );
    }

    private ValueTask HandleViewModPageMessage(ViewModPageMessage viewModPageMessage, CancellationToken cancellationToken)
    {
        return viewModPageMessage.Id.Match(
            modPageMetadataId => OpenModPage(modPageMetadataId),
            libraryItemId => OpenModPage(new NexusModsLibraryItem.ReadOnly(_connection.Db, libraryItemId).ModPageMetadataId)
        );
    }
    
    private async ValueTask HandleDeleteItemMessage(DeleteItemMessage deleteItemMessage, CancellationToken cancellationToken)
    {
        var ids = deleteItemMessage.Ids;
        if (ids.Length == 0) return;
        
        var toRemove = ids.Select(id => LibraryItem.Load(_connection.Db, id)).ToArray();
        await LibraryItemRemover.RemoveAsync(_connection, _serviceProvider.GetRequiredService<IOverlayController>(), _libraryService, toRemove);

        _notificationService.ShowToast(Language.ToastNotification_Items_deleted);
    }
    
    private async ValueTask HandleHideUpdatesMessage(HideUpdatesMessage hideUpdatesMessage, CancellationToken cancellationToken)
    {
        var modUpdateFilterService = _serviceProvider.GetRequiredService<IModUpdateFilterService>();

        await hideUpdatesMessage.Id.Match(
            async modPageId =>
            {
                // Handle hiding/showing updates for a mod page
                // First get all library items we have that come from the mod page.
                var allLibraryFilesForThisMod =  NexusModsLibraryItem.All(_connection.Db)
                    .Where(x => x.ModPageMetadataId == modPageId)
                    .Select(x => x.FileMetadata)
                    .ToArray();

                // Note(sewer):
                // Behaviour per captainsandypants (Slack).
                // 'If any children have updates set to hidden, then the parent should have "Show updates" as the menu item.
                // When selected, this will set all children to show updates.'
                //
                // We have to be careful here.
                // We have a list of ***current versions*** of files in the mod page.
                // We need to check if it's possible to update ***any of these current versions*** to a new file.
                //     👉 👉 So for each file we need to check if any of its versions is not ignored as an update.
                var modsWithUpdatesHidden = allLibraryFilesForThisMod.Where(x =>
                    {
                        // Checking all versions is not a bug, it is intended behaviour per design.
                        // Search 'That definition also means older versions should be excluded from update checks.' in Slack.
                        var newerFiles = RunUpdateCheck.GetAllVersionsForExistingFile(x).ToArray();
                        return newerFiles.All(newer => modUpdateFilterService.IsFileHidden(newer.Uid));
                    }
                ).ToArray();

                if (modsWithUpdatesHidden.Length > 0)
                {
                    var allVersionsOfLibraryItemsWithUpdatesHidden =
                        modsWithUpdatesHidden.SelectMany(RunUpdateCheck.GetAllVersionsForExistingFile)
                                             .Select(x => x.Uid);
                    await modUpdateFilterService.ShowFilesAsync(allVersionsOfLibraryItemsWithUpdatesHidden);
                }
                else
                {
                    var allVersionsOfAllFilesForThisMod = allLibraryFilesForThisMod
                        .SelectMany(RunUpdateCheck.GetAllVersionsForExistingFile)
                        .Select(x => x.Uid);
                    await modUpdateFilterService.HideFilesAsync(allVersionsOfAllFilesForThisMod);
                }
            },
            async libraryItemId =>
            {
                // Handle hiding/showing updates for a single file
                var libraryItem = NexusModsLibraryItem.Load(_connection.Db, libraryItemId);
                var fileMetadata = libraryItem.FileMetadata;
                var allVersions = RunUpdateCheck.GetAllVersionsForExistingFile(fileMetadata).ToArray();

                var areAllHidden = allVersions.All(x => modUpdateFilterService.IsFileHidden(x.Uid));

                if (areAllHidden)
                    await modUpdateFilterService.ShowFilesAsync(allVersions.Select(x => x.Uid));
                else
                    await modUpdateFilterService.HideFilesAsync(allVersions.Select(x => x.Uid));
            }
        );
    }

    private ValueTask OpenModPage(NexusModsModPageMetadataId modPageMetadataId)
    {
        var modPage = new NexusModsModPageMetadata.ReadOnly(_connection.Db, modPageMetadataId);
        var url = NexusModsUrlBuilder.GetModUri(modPage.GameDomain, modPage.Uid.ModId);
        var os = _serviceProvider.GetRequiredService<IOSInterop>();
        os.OpenUri(url);
        return ValueTask.CompletedTask;
    }

    // MIST-006-14B2B4: same Refresh button; discovery runs read-only before the existing update check.
    private async ValueTask RefreshUpdates(CancellationToken token)
    {
        if (_loadout.InstallationInstance.Game.GameId.ToString() == "RedEngine.Cyberpunk2077")
        {
            var logger = _serviceProvider.GetRequiredService<ILogger<LibraryViewModel>>();
            var events = new System.Collections.Generic.List<string>();
            void Note(string message)
            {
                var line = $"{DateTimeOffset.Now:O} [MIST-006-14B2B4] {message}";
                events.Add(line);
                logger.LogInformation("{MistDiscovery}", line);
            }
            string? logPath = null;
            try
            {
                var gameRoot = System.IO.Path.GetFullPath(_loadout.InstallationInstance.Locations[NexusMods.Sdk.Games.LocationId.Game].Path.ToString());
                var state = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
                if (string.IsNullOrWhiteSpace(state))
                    state = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "state");
                var output = System.IO.Path.GetFullPath(System.IO.Path.Combine(state, "ModIntegrationAndSortingTechnology", "mod-discovery"));
                var stamp = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ") + "-" + Guid.NewGuid().ToString("N");
                var jsonPath = System.IO.Path.Combine(output, "cyberpunk-" + stamp + ".json");
                logPath = System.IO.Path.Combine(output, "cyberpunk-" + stamp + ".log");
                if (!NexusMods.DataModel.ModDiscovery.MISTScopedCyberpunkDiscovery.IsSafeReportPath(jsonPath, gameRoot) ||
                    !NexusMods.DataModel.ModDiscovery.MISTScopedCyberpunkDiscovery.IsSafeReportPath(logPath, gameRoot))
                    throw new InvalidOperationException("Discovery report path overlaps game directory.");
                System.IO.Directory.CreateDirectory(output);
                Note("START root=" + gameRoot + " mode=read-only");
                var report = await Task.Run(() => NexusMods.DataModel.ModDiscovery.MISTModDiscoveryPipeline.Scan(gameRoot, token), token);
                Note($"INVENTORY files={report.Inventory.InstalledFileCount} groups={report.Inventory.CandidateGroups.Count} unassociated={report.Inventory.UnattributedFiles.Count}");
                foreach (var module in report.Modules) Note($"MODULE {module.Module}: {module.Status}, evidence={module.EvidenceCount}");
                foreach (var group in report.Inventory.CandidateGroups) Note($"GROUP {group.SuggestedName}: {group.Files.Count} files ({group.Confidence})");
                foreach (var file in report.Inventory.UnattributedFiles) Note("UNASSOCIATED " + file.Path);
                foreach (var path in report.Inventory.SkippedPaths) Note("SKIPPED " + path);
                Note($"HINTS identities={report.IdentityHints.Count} fingerprints={report.ContentFingerprints.Count} relationships={report.PossibleRelationships.Count}");
                var json = System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web) { WriteIndented = true });
                await System.IO.File.WriteAllTextAsync(jsonPath, json, token);
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    SetMISTDiscoveryResults(report, cached: false, sourceDiscoveryPath: jsonPath));
                Note("DISPLAY candidates=" + report.Inventory.CandidateGroups.Count + " unmanaged=true");
                Note("REPORT " + jsonPath);
                Note("COMPLETE no mod adoption or game changes");
                _notificationService.ShowToast($"M.I.S.T.: {report.Inventory.CandidateGroups.Count} groups / {report.Inventory.InstalledFileCount} files detected; not imported.", ToastNotificationVariant.Success);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                Note("CANCELLED");
                throw;
            }
            catch (Exception error)
            {
                Note("FAILED " + error);
                logger.LogError(error, "MIST-006-14B2B4 discovery failed");
                _notificationService.ShowToast("M.I.S.T. discovery failed; check application logs.", ToastNotificationVariant.Failure);
            }
            finally
            {
                if (logPath != null)
                    await System.IO.File.WriteAllLinesAsync(logPath, events, CancellationToken.None);
            }
        }
        await _modUpdateService.CheckAndUpdateModPages(token, notify: true);
    }

    private async ValueTask InstallItems(LibraryItemId[] ids, LoadoutItemGroupId targetLoadoutGroup, bool useAdvancedInstaller, CancellationToken cancellationToken)
    {
        var db = _connection.Db;
        var items = ids
            .Select(id => LibraryItem.Load(db, id))
            .Where(x => x.IsValid())
            .ToArray();

        await Parallel.ForAsync(
            fromInclusive: 0,
            toExclusive: items.Length,
            body: (i, innerCancellationToken) => InstallLibraryItem(items[i], _loadout, targetLoadoutGroup, innerCancellationToken, useAdvancedInstaller),
            cancellationToken: cancellationToken
        );
        
        var targetCollection = LoadoutItem.Load(db, targetLoadoutGroup);
    }

    private LibraryItemId[] GetSelectedIds()
    {
        var ids = Adapter.SelectedModels
            .SelectMany(static model => GetLibraryItemIds(model))
            .Distinct()
            .ToArray();

        return ids;
    }
    
    private static IEnumerable<LibraryItemId> GetLibraryItemIds(CompositeItemModel<EntityId> itemModel)
    {
        // Fail closed: an observed, unowned candidate has no LibraryItemIds.
        var itemIds = itemModel.GetOptional<LibraryComponents.LibraryItemIds>(LibraryColumns.Actions.LibraryItemIdsComponentKey);
        return itemIds.HasValue ? itemIds.Value.ItemIds : Array.Empty<LibraryItemId>();
    }

    private IEnumerable<CompositeItemModel<EntityId>> GetSelectedModelsWithUpdates()
    {
        return Adapter.SelectedModels
            .Where(model =>
            {
                var updateComponent = model.GetOptional<LibraryComponents.UpdateAction>(LibraryColumns.Actions.UpdateComponentKey);
                return updateComponent.HasValue && updateComponent.Value.NewFiles.Value.HasAnyUpdates;
            });
    }

    private LoadoutItemGroupId GetInstallationTarget() => (SelectedInstallationTarget?.Id ?? _installationTargets[0].Id).Value;

    private ValueTask InstallSelectedItems(bool useAdvancedInstaller, CancellationToken cancellationToken)
    {
        return InstallItems(GetSelectedIds(), GetInstallationTarget(), useAdvancedInstaller, cancellationToken);
    }

    private async ValueTask InstallLibraryItem(
        LibraryItem.ReadOnly libraryItem,
        LoadoutId loadout,
        LoadoutItemGroupId targetLoadoutGroup,
        CancellationToken cancellationToken,
        bool useAdvancedInstaller = false)
    {
        try
        {
            await _loadoutManager.InstallItem(libraryItem, loadout, parent: targetLoadoutGroup, installer: useAdvancedInstaller ? _advancedInstaller : null);
            var targetCollection  = LoadoutItem.Load(_connection.Db, targetLoadoutGroup);
        }
        catch (OperationCanceledException)
        {
            // User cancelled the installation - this is expected behavior, don't show error
            var logger = _serviceProvider.GetRequiredService<ILogger<LibraryViewModel>>();
            logger.LogInformation("Installation of {LibraryItem} was cancelled by user", libraryItem.Name);
        }
    }

    private async ValueTask RemoveSelectedItems(CancellationToken cancellationToken)
    {
        var db = _connection.Db;
        var toRemove = GetSelectedIds().Select(id => LibraryItem.Load(db, id)).ToArray();
        await LibraryItemRemover.RemoveAsync(_connection, _serviceProvider.GetRequiredService<IOverlayController>(), _libraryService, toRemove);
        
        _notificationService.ShowToast(Language.ToastNotification_Items_deleted);
    }

    private async ValueTask AddFilesFromDisk(IStorageProvider storageProvider, CancellationToken cancellationToken)
    {
        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = true,
            Title = Language.LoadoutGridView_AddMod_FilePicker_Title,
            FileTypeFilter =
            [
                // TODO: fetch from some service
                new FilePickerFileType(Language.LoadoutGridView_AddMod_FileType_Archive)
                {
                    Patterns = ["*.zip", "*.7z", "*.rar"],
                },
            ],
        });

        var paths = files
            .Select(file => file.TryGetLocalPath())
            .NotNull()
            .Select(path => FileSystem.Shared.FromUnsanitizedFullPath(path))
            .Where(path => path.FileExists)
            .ToArray();

        await Parallel.ForAsync(
            fromInclusive: 0,
            toExclusive: paths.Length,
            body: async (i, innerCancellationToken) =>
            {
                var path = paths[i];
                await _libraryService.AddLocalFile(path);
            },
            cancellationToken: cancellationToken
        );
    }

    private ValueTask UpdateSelectedItems(CancellationToken cancellationToken)
    {
        // TEMPORARY: Use conditional default behavior based on Premium status to avoid unwanted dialog for free users
        // Original: return UpdateSelectedItemsInternal(useUpdateAndReplace: true, cancellationToken);
        var isPremium = _loginManager.IsPremium;
        return UpdateSelectedItemsInternal(useUpdateAndReplace: isPremium, cancellationToken);
    }

    private ValueTask UpdateAndKeepOldSelectedItems(CancellationToken cancellationToken)
    {
        return UpdateSelectedItemsInternal(useUpdateAndReplace: false, cancellationToken);
    }

    private async ValueTask UpdateSelectedItemsInternal(bool useUpdateAndReplace, CancellationToken cancellationToken)
    {
        var selectedModels = Adapter.SelectedModels.ToArray();
        // Note(sewer): The selection count is not guaranteed to equal the number of items
        // with updates because the user can select a mixture of items with and without updates
        // in a single selection.
        var withUpdatesOnPage = new List<ModUpdatesOnModPage>(); 
        
        foreach (var model in selectedModels)
        {
            // Check if this model has an update component
            var updateComponent = model.GetOptional<LibraryComponents.UpdateAction>(LibraryColumns.Actions.UpdateComponentKey);
            if (!updateComponent.HasValue) continue;

            var component = updateComponent.Value;
            var updatesOnPage = component.NewFiles.Value;
            
            if (!updatesOnPage.HasAnyUpdates) continue;
            withUpdatesOnPage.Add(updatesOnPage);
        }
        
        // Note(sewer): This should (by definition) be always true, because the user had at least
        // 1 selection with an update (otherwise they wouldn't be able to call this)
        if (withUpdatesOnPage.Count > 0)
        {
            var isPremium = _loginManager.IsPremium;
            if (useUpdateAndReplace)
            {
                if (!isPremium)
                    await UpdateAndReplaceForMultiModPagesFreeOnly(cancellationToken, withUpdatesOnPage);
                else
                    await UpdateAndReplaceForMultiModPagesPremiumOnly(cancellationToken, withUpdatesOnPage);
            }
            else
            {
                if (!isPremium) UpdateAndKeepOldFree(withUpdatesOnPage);
                else
                    await UpdateAndKeepOldPremium(withUpdatesOnPage, cancellationToken);
            }
        }
    }

    private async ValueTask UpdateAllItems(CancellationToken cancellationToken)
    {
        IsUpdatingAll = true;
        try
        {
            var isPremium = _loginManager.IsPremium;

            if (!isPremium)
            {
                var osInterop = _serviceProvider.GetRequiredService<IOSInterop>();
                await PremiumDialog.ShowUpdatePremiumDialog(WindowManager, osInterop);
                return;
            }

            // Filter mod pages to only those for the current game
            var currentGameId = _loadout.InstallationInstance.Game.NexusModsGameId;
            var modPagesWithUpdates = _modUpdateService.GetAllModPagesWithUpdates()
                .Where(pair => 
                {
                    var modPage = NexusModsModPageMetadata.Load(_connection.Db, pair.modPageId);
                    return modPage.IsValid() && modPage.Uid.GameId == currentGameId;
                });
            var allUpdates = modPagesWithUpdates.Select(pair => pair.updates).ToArray();
            
            if (allUpdates.Length > 0)
                await UpdateAndReplaceForMultiModPagesPremiumOnly(cancellationToken, allUpdates);
        }
        finally
        {
            IsUpdatingAll = false;
        }
    }

    /// <summary>
    /// Removes old library items that are not installed in any read-only (e.g. Nexus) collections.
    /// This ensures collections 'remain' installed.
    /// 
    /// Old items where the new library item is invalid (e.g. Replace Failed), are preserved;
    /// cleaning those up happens in <see cref="ShowSomeInstallsFailedDialog"/> in partial fail scenarios.
    /// </summary>
    /// <param name="oldToNewLibraryMapping">The mapping of old library items to their new replacements.</param>
    private async ValueTask RemoveOldLibraryItemsIfNotInReadOnlyCollections(IEnumerable<DownloadedProcessingResult> oldToNewLibraryMapping)
    {
        var oldItemsToRemove = new List<LibraryItem.ReadOnly>();
        
        foreach (var item in oldToNewLibraryMapping)
        {
            // If the new item is not valid (e.g. replace failed), and user chose to 'delete new files' we skip it.
            if (!item.NewItem.IsValid())
                continue;
            
            // Check if the old item is still linked to any collections
            var collectionsWithItem = _libraryService.CollectionsWithLibraryItem(item.OldItem, excludeReadOnlyCollections: false);
            
            // Only remove if the item is NOT in any read-only collections (i.e., only in modifiable collections or no collections)
            var hasReadOnlyCollections = collectionsWithItem.Any(x => x.collection.IsReadOnly);
            if (!hasReadOnlyCollections)
                oldItemsToRemove.Add(item.OldItem);
        }

        if (oldItemsToRemove.Count > 0)
            await _libraryService.RemoveLibraryItems(oldItemsToRemove);
    }
}

public readonly record struct InstallMessage(LibraryItemId[] Ids);
public readonly record struct UpdateAndReplaceMessage(ModUpdatesOnModPage Updates, CompositeItemModel<EntityId> TreeNode);
public readonly record struct UpdateAndKeepOldMessage(ModUpdatesOnModPage Updates, CompositeItemModel<EntityId> TreeNode);
public readonly record struct ViewChangelogMessage(OneOf<NexusModsModPageMetadataId, NexusModsLibraryItemId> Id);
public readonly record struct ViewModPageMessage(OneOf<NexusModsModPageMetadataId, NexusModsLibraryItemId> Id);
public readonly record struct HideUpdatesMessage(OneOf<NexusModsModPageMetadataId, NexusModsLibraryItemId> Id);
public readonly record struct DeleteItemMessage(LibraryItemId[] Ids);

public class LibraryTreeDataGridAdapter :
    TreeDataGridAdapter<CompositeItemModel<EntityId>, EntityId>,
    ITreeDataGirdMessageAdapter<OneOf<InstallMessage, UpdateAndReplaceMessage, UpdateAndKeepOldMessage, ViewChangelogMessage, ViewModPageMessage, HideUpdatesMessage, DeleteItemMessage>>
{
    private readonly ILibraryDataProvider[] _libraryDataProviders;
    private readonly LibraryFilter _libraryFilter;
    private readonly IConnection _connection;

    // MIST-006-14B2B6: ephemeral UI-only rows. These are not registered ILibraryDataProviders
    // and never create or link real LibraryItem/Loadout records.
    private readonly SourceCache<CompositeItemModel<EntityId>, EntityId> _mistDiscoveredRows =
        new(static model => model.Key);
    public Subject<MISTDiscoveredMod> MISTReviewRequested { get; } = new();

    // These keys exist only in memory and are never passed to any DB operation.
    // Deliberately high-valued ephemeral namespace. Never dereference these IDs.
    private static EntityId MISTDisplayKey(int group, int file)
    {
        if (group < 1 || group >= 0x10000 || file < 0 || file >= 0x1000000)
            throw new ArgumentOutOfRangeException(nameof(group));
        var raw = 0xFFFE000000000000UL | ((ulong)group << 24) | (uint)file;
        return System.Runtime.CompilerServices.Unsafe.BitCast<ulong, EntityId>(raw);
    }

    private static CompositeItemModel<EntityId> MakeMISTDiscoveredRow(MISTDiscoveredMod candidate, int index)
    {
        var filePaths = candidate.FilePathsText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var children = NexusMods.App.UI.Extensions.ObservableExtensions.ReturnFactory(() =>
        {
            var changes = new List<Change<CompositeItemModel<EntityId>, EntityId>>();
            for (var i = 0; i < filePaths.Length; i++)
            {
                // Child is an observed file path, NOT a downloaded Nexus variant.
                var child = new CompositeItemModel<EntityId>(MISTDisplayKey(index, i + 1));
                child.Add(SharedColumns.Name.NameComponentKey, new NameComponent(value: filePaths[i]));
                child.Add(LibraryColumns.Actions.MISTReviewComponentKey,
                    new LibraryComponents.MISTReviewAction(candidate, isFile: true));
                changes.Add(new Change<CompositeItemModel<EntityId>, EntityId>(
                    ChangeReason.Add, child.Key, child));
            }
            return new ChangeSet<CompositeItemModel<EntityId>, EntityId>(changes);
        });

        var root = new CompositeItemModel<EntityId>(MISTDisplayKey(index, 0))
        {
            HasChildrenObservable = System.Reactive.Linq.Observable.Return(filePaths.Length > 0),
            ChildrenObservable = children,
        };
        root.Add(SharedColumns.Name.NameComponentKey,
            new NameComponent(value: candidate.Name + "  ·  Unmanaged"));
        root.Add(SharedColumns.Name.ImageComponentKey,
            new ImageComponent(value: ImagePipelines.ModPageThumbnailFallback));
        root.Add(LibraryColumns.ItemVersion.CurrentVersionComponentKey,
            new VersionComponent(value: candidate.VersionText == "Version unknown" ? "Unknown" : "Unverified"));
        root.Add(SharedColumns.ItemSize.ComponentKey,
            new SizeComponent(value: NexusMods.Paths.Size.FromLong(candidate.TotalSizeBytes)));
        root.Add(LibraryColumns.Actions.MISTReviewComponentKey,
            new LibraryComponents.MISTReviewAction(candidate, isFile: false));
        return root;
    }

    public void SetMISTDiscoveredMods(IReadOnlyList<MISTDiscoveredMod> candidates)
    {
        // Bound the UI adapter to observed candidate groups, not individual game files.
        // The scanner is responsible for producing the candidate list. No FS writes here.
        if (candidates.Count >= 0x10000)
            throw new InvalidOperationException("Too many display-only candidate groups");
        var models = candidates.Select((candidate, index) => MakeMISTDiscoveredRow(candidate, index + 1)).ToArray();
        _mistDiscoveredRows.Edit(updater =>
        {
            updater.Clear();
            updater.AddOrUpdate(models);
        });
    }

    public Subject<OneOf<InstallMessage, UpdateAndReplaceMessage, UpdateAndKeepOldMessage, ViewChangelogMessage, ViewModPageMessage, HideUpdatesMessage, DeleteItemMessage>> MessageSubject { get; } = new();

    public LibraryTreeDataGridAdapter(IServiceProvider serviceProvider, LibraryFilter libraryFilter) : base(serviceProvider)
    {
        _libraryFilter = libraryFilter;
        _libraryDataProviders = serviceProvider.GetServices<ILibraryDataProvider>().ToArray();
        _connection = serviceProvider.GetRequiredService<IConnection>();
    }

    protected override IObservable<IChangeSet<CompositeItemModel<EntityId>, EntityId>> GetRootsObservable(bool viewHierarchical)
    {
        // Merge display-only discovery models into the exact same native roots/grid.
        return _libraryDataProviders.Select(x => x.ObserveLibraryItems(_libraryFilter))
            .Append(_mistDiscoveredRows.Connect())
            .MergeChangeSets();
    }

    protected override void BeforeModelActivationHook(CompositeItemModel<EntityId> model)
    {
        base.BeforeModelActivationHook(model);

        model.SubscribeToComponentAndTrack<LibraryComponents.MISTReviewAction, LibraryTreeDataGridAdapter>(
            key: LibraryColumns.Actions.MISTReviewComponentKey,
            state: this,
            factory: static (self, itemModel, component) => component.CommandReview.Subscribe(
                (self, component), static (_, state) =>
                {
                    var (adapter, review) = state;
                    adapter.MISTReviewRequested.OnNext(review.Candidate);
                })
        );

        model.SubscribeToComponentAndTrack<LibraryComponents.InstallAction, LibraryTreeDataGridAdapter>(
            key: LibraryColumns.Actions.InstallComponentKey,
            state: this,
            factory: static (self, itemModel, component) => component.CommandInstall.Subscribe((self, itemModel, component), static (_, state) =>
            {
                var (self, model, component) = state;
                var ids = GetLibraryItemIds(model).ToArray();

                self.MessageSubject.OnNext(new InstallMessage(ids));
            })
        );

        model.SubscribeToComponentAndTrack<LibraryComponents.UpdateAction, LibraryTreeDataGridAdapter>(
            key: LibraryColumns.Actions.UpdateComponentKey,
            state: this,
            factory: static (self, itemModel, component) => component.CommandUpdateAndKeepOld
                .SubscribeOnUIThreadDispatcher() // Update payload may expand row for free users, requiring UI thread.
                .Subscribe((self, itemModel, component), static (_, state) =>
            {
                var (self, model, component) = state;
                var newFile = component.NewFiles.Value;
                self.MessageSubject.OnNext(new UpdateAndKeepOldMessage(newFile, model));
            })
        );

        model.SubscribeToComponentAndTrack<LibraryComponents.UpdateAction, LibraryTreeDataGridAdapter>(
            key: LibraryColumns.Actions.UpdateComponentKey,
            state: this,
            factory: static (self, itemModel, component) => component.CommandUpdateAndReplace
                .SubscribeOnUIThreadDispatcher() // Update payload may expand row for free users, requiring UI thread.
                .Subscribe((self, itemModel, component), static (_, state) =>
            {
                var (self, model, component) = state;
                var newFile = component.NewFiles.Value;
                self.MessageSubject.OnNext(new UpdateAndReplaceMessage(newFile, model));
            })
        );

        static OneOf<NexusModsModPageMetadataId, NexusModsLibraryItemId> GetModPageIdOneOfType(IDb db, EntityId entityId)
        {
            var modPageMetadata = NexusModsModPageMetadata.Load(db, entityId);
            if (modPageMetadata.IsValid())
                return OneOf<NexusModsModPageMetadataId, NexusModsLibraryItemId>.FromT0(modPageMetadata.Id);
                
            // Try to load as NexusModsLibraryItem
            var libraryItem = NexusModsLibraryItem.Load(db, entityId);
            if (libraryItem.IsValid())
                return OneOf<NexusModsModPageMetadataId, NexusModsLibraryItemId>.FromT1(libraryItem.Id);
            
            throw new Exception("Unknown type of entity for ViewChangelogAction: " + entityId);
        }

        model.SubscribeToComponentAndTrack<LibraryComponents.ViewChangelogAction, LibraryTreeDataGridAdapter>(
            key: LibraryColumns.Actions.ViewChangelogComponentKey,
            state: this,
            factory: static (self, itemModel, component) => component.CommandViewChangelog.Subscribe((self, itemModel, component), static (_, state) =>
            {
                var (self, model, _) = state;
                var entityId = model.Key;
                
                self.MessageSubject.OnNext(new ViewChangelogMessage(GetModPageIdOneOfType(self._connection.Db, entityId)));
            })
        );

        model.SubscribeToComponentAndTrack<SharedComponents.ViewModPageAction, LibraryTreeDataGridAdapter>(
            key: LibraryColumns.Actions.ViewModPageComponentKey,
            state: this,
            factory: static (self, itemModel, component) => component.CommandViewModPage.Subscribe((self, itemModel, component), static (_, state) =>
            {
                var (self, model, _) = state;
                var entityId = model.Key;

                self.MessageSubject.OnNext(new ViewModPageMessage(GetModPageIdOneOfType(self._connection.Db, entityId)));
            })
        );
        
        model.SubscribeToComponentAndTrack<LibraryComponents.DeleteItemAction, LibraryTreeDataGridAdapter>(
            key: LibraryColumns.Actions.DeleteItemComponentKey,
            state: this,
            factory: static (self, itemModel, component) => component.CommandDeleteItem.Subscribe((self, itemModel, component), static (_, state) =>
            {
                var (self, model, component) = state;
                var ids = GetLibraryItemIds(model).ToArray();

                self.MessageSubject.OnNext(new DeleteItemMessage(ids));
            })
        );

        model.SubscribeToComponentAndTrack<LibraryComponents.HideUpdatesAction, LibraryTreeDataGridAdapter>(
            key: LibraryColumns.Actions.HideUpdatesComponentKey,
            state: this,
            factory: static (self, itemModel, component) => component.CommandHideUpdates.Subscribe((self, itemModel, component), static (_, state) =>
            {
                var (self, model, _) = state;
                var entityId = model.Key;

                self.MessageSubject.OnNext(new HideUpdatesMessage(GetModPageIdOneOfType(self._connection.Db, entityId)));
            })
        );
    }
    
    private static IEnumerable<LibraryItemId> GetLibraryItemIds(CompositeItemModel<EntityId> itemModel)
    {
        var itemIds = itemModel.GetOptional<LibraryComponents.LibraryItemIds>(LibraryColumns.Actions.LibraryItemIdsComponentKey);
        return itemIds.HasValue ? itemIds.Value.ItemIds : Array.Empty<LibraryItemId>();
    }

    protected override IColumn<CompositeItemModel<EntityId>>[] CreateColumns(bool viewHierarchical)
    {
        var nameColumn = ColumnCreator.Create<EntityId, SharedColumns.Name>();

        return
        [
            viewHierarchical ? ITreeDataGridItemModel<CompositeItemModel<EntityId>, EntityId>.CreateExpanderColumn(nameColumn) : nameColumn,
            ColumnCreator.Create<EntityId, LibraryColumns.ItemVersion>(),
            ColumnCreator.Create<EntityId, SharedColumns.ItemSize>(),
            ColumnCreator.Create<EntityId, LibraryColumns.DownloadedDate>(sortDirection: ListSortDirection.Descending),
            ColumnCreator.Create<EntityId, SharedColumns.InstalledDate>(),
            ColumnCreator.Create<EntityId, LibraryColumns.Collections>(),
            ColumnCreator.Create<EntityId, LibraryColumns.Actions>(),
        ];
    }

    private bool _isDisposed;
    protected override void Dispose(bool disposing)
    {
        if (disposing && !_isDisposed)
        {
            MessageSubject.Dispose();
            MISTReviewRequested.Dispose();
            _mistDiscoveredRows.Dispose();
            _isDisposed = true;
        }

        base.Dispose(disposing);
    }
}

/// <summary>
/// Results from processing downloads in parallel.
/// </summary>
/// <param name="LibraryItemReplacementResults">
///     Mapping of all the old mods to new mods; along with their replacement result.
///     This excludes items where downloads failed.
/// </param>
/// <param name="DownloadErrors">Items which failed to download.</param>
public readonly record struct DownloadProcessingResults(
    ConcurrentBag<DownloadedProcessingResult> LibraryItemReplacementResults,
    ConcurrentBag<DownloadError> DownloadErrors
);

/// <summary>
/// The result of downloading and installing a given old->new mod tuple.
/// </summary>
public struct DownloadedProcessingResult
{
    /// <summary>
    /// The old version of the mod.
    /// </summary>
    public LibraryItem.ReadOnly OldItem;

    /// <summary>
    /// The new version of the mod that was downloaded.
    /// </summary>
    /// <remarks>
    ///     This item may be invalid if the user decides to delete the file when <see cref="InstallResult"/>
    ///     is not success.
    /// </remarks>
    public LibraryItem.ReadOnly NewItem;
    
    /// <summary>
    /// The install result for <see cref="NewItem"/>.
    /// </summary>
    public LibraryItemReplacementResult InstallResult;
}

/// <summary>
/// An error which happened when downloading a new version of a library item.
/// </summary>
public struct DownloadError
{
    /// <summary>
    /// The file which we tried to download.
    /// </summary>
    public NexusModsFileMetadata.ReadOnly File;
    
    /// <summary>
    /// The error which occurred during the download or installation of the file.
    /// </summary>
    public Exception Error;
}
