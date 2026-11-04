using System.Reactive.Disposables;
using NexusMods.Abstractions.Games.FileHashes;
using Avalonia.Threading;
using NexusMods.Abstractions.Collections;
using NexusMods.App.UI.Dialog;
using NexusMods.App.UI.Dialog.Enums;
using NexusMods.App.UI.Pages.CollectionDownload;
using NexusMods.App.UI.Pages.LoadoutPage;
using NexusMods.Collections;
using NexusMods.UI.Sdk.Dialog;
using NexusMods.UI.Sdk.Dialog.Enums;
using System.Reactive.Linq;
using DynamicData;
using Microsoft.Extensions.DependencyInjection;
using NexusMods.Abstractions.Loadouts;
using NexusMods.Abstractions.Loadouts.Exceptions;
using NexusMods.Abstractions.Loadouts.Synchronizers;
using NexusMods.App.UI.Controls.Navigation;
using NexusMods.App.UI.Overlays;
using NexusMods.App.UI.Overlays.Generic.MessageBox.Ok;
using NexusMods.App.UI.Pages.Diff.ApplyDiff;
using NexusMods.App.UI.Resources;
using NexusMods.App.UI.Windows;
using NexusMods.App.UI.WorkspaceSystem;
using NexusMods.MnemonicDB.Abstractions;
using NexusMods.Sdk.Games;
using NexusMods.Sdk.Jobs;
using NexusMods.Sdk.Loadouts;
using NexusMods.UI.Sdk;
using R3;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Observable = System.Reactive.Linq.Observable;
using ReactiveCommand = ReactiveUI.ReactiveCommand;
using Unit = System.Reactive.Unit;

namespace NexusMods.App.UI.LeftMenu.Items;

public class ApplyControlViewModel : AViewModel<IApplyControlViewModel>, IApplyControlViewModel
{
    private readonly IConnection _conn;
    private readonly ISynchronizerService _syncService;
    private readonly IJobMonitor _jobMonitor;
    private readonly IWindowNotificationService _notificationService;
    private readonly IGameRegistry _gameRegistry;

    private readonly LoadoutId _loadoutId;
    private readonly IServiceProvider _serviceProvider;
    private readonly GameInstallMetadataId _gameMetadataId;
    [Reactive] private bool CanApply { get; set; } = true;
    [Reactive] public bool IsApplying { get; private set; }

    public ReactiveUI.ReactiveCommand<Unit, Unit> ApplyCommand { get; }
    public ReactiveUI.ReactiveCommand<NavigationInformation, Unit> ShowApplyDiffCommand { get; }

    [Reactive] public bool IsProcessing { get; private set; }
    [Reactive] public string ApplyButtonText { get; private set; } = Language.ApplyControlViewModel__APPLY;

    [Reactive] public string ProcessingText { get; private set; } = "";
    [Reactive] public bool IsLaunchButtonEnabled { get; private set; } = true;

    public ILaunchButtonViewModel LaunchButtonViewModel { get; }

    public ApplyControlViewModel(LoadoutId loadoutId, IServiceProvider serviceProvider, IJobMonitor jobMonitor, IOverlayController overlayController, GameRunningTracker gameRunningTracker)
    {
        _loadoutId = loadoutId;
        _serviceProvider = serviceProvider;
        _syncService = serviceProvider.GetRequiredService<ISynchronizerService>();
        _conn = serviceProvider.GetRequiredService<IConnection>();
        _jobMonitor = serviceProvider.GetRequiredService<IJobMonitor>();
        _notificationService = serviceProvider.GetRequiredService<IWindowNotificationService>();
        var windowManager = serviceProvider.GetRequiredService<IWindowManager>();
        _gameRegistry = serviceProvider.GetRequiredService<IGameRegistry>();

        _gameMetadataId = Sdk.Loadouts.Loadout.Load(_conn.Db, loadoutId).InstallationId;

        LaunchButtonViewModel = serviceProvider.GetRequiredService<ILaunchButtonViewModel>();
        LaunchButtonViewModel.LoadoutId = loadoutId;
        
        ApplyCommand = ReactiveCommand.CreateFromTask(async () => await Apply(), 
            canExecute: this.WhenAnyValue(vm => vm.CanApply));
        
        ShowApplyDiffCommand = ReactiveCommand.Create<NavigationInformation>(info =>
        {
            var pageData = new PageData
            {
                FactoryId = ApplyDiffPageFactory.StaticId,
                Context = new ApplyDiffPageContext
                {
                    LoadoutId = _loadoutId,
                },
            };

            var workspaceController = windowManager.ActiveWorkspaceController;

            var behavior = workspaceController.GetOpenPageBehavior(pageData, info);
            var workspaceId = workspaceController.ActiveWorkspaceId;
            workspaceController.OpenPage(workspaceId, pageData, behavior);
        });

        this.WhenActivated(disposables =>
            {
                var isProcessingObservable = _jobMonitor.HasActiveJob<ProcessLoadoutChangesJob>(job => job.LoadoutId.Equals(loadoutId))
                    .Prepend(false);
                
                var loadoutStatuses = Observable.Prepend(Observable.FromAsync(() => _syncService.StatusForLoadout(_loadoutId))
                        .Switch(), LoadoutSynchronizerState.Pending);

                var gameStatuses = _syncService.StatusForGame(_gameMetadataId)
                    .Prepend(GameSynchronizerState.Idle);

                var hasUnmanagingJob = _jobMonitor.HasActiveJob<UnmanageGameJob>(job =>
                {
                    if (!_gameRegistry.TryGetMetadata(job.Installation, out var metadata)) return false;
                    return metadata.GameInstallMetadataId == _gameMetadataId;
                }).Prepend(false);

                // Note(sewer):
                // Fire an initial value with StartWith because CombineLatest requires all stuff to have latest values.
                // In any case, we should prevent Apply from being available while a file is in use.
                // A file may be in use because:
                // - The user launched the game externally (e.g. through Steam).
                //     - Approximate this by seeing if any EXE in any of the game folders are running.
                //     - This is done in 'Synchronize' method.
                // - They're running a tool from within the App.
                //     - Check running jobs.
                loadoutStatuses.CombineLatest(isProcessingObservable, gameStatuses, gameRunningTracker.GetWithCurrentStateAsStarting(), hasUnmanagingJob)
                    .OnUI()
                    .Subscribe(status =>
                    {
                        var (ldStatus, isProcessing,  gameStatus, running, isUnmanaging) = status;
                        IsProcessing = isProcessing;
                        CanApply = !isProcessing
                                   && !running
                                   && gameStatus != GameSynchronizerState.Busy
                                   && ldStatus != LoadoutSynchronizerState.Pending
                                   && ldStatus != LoadoutSynchronizerState.Current
                                   && !isUnmanaging;
                        IsLaunchButtonEnabled = !isProcessing 
                                                && !running
                                                && gameStatus != GameSynchronizerState.Busy
                                                && ldStatus == LoadoutSynchronizerState.Current
                                                && !isUnmanaging;
                        
                    })
                    .DisposeWith(disposables);

                _jobMonitor.HasActiveJob<SynchronizeLoadoutJob>(job => job.LoadoutId == loadoutId)
                    .Prepend(_jobMonitor.Jobs.Any(job => job.Definition is SynchronizeLoadoutJob sJob && sJob.LoadoutId == loadoutId))
                    .OnUI()
                    .Subscribe(isApplying => IsApplying = isApplying)
                    .DisposeWith(disposables);
                
                _jobMonitor.ObserveActiveJobs<SynchronizeLoadoutJob>()
                    .Prepend(ChangeSet<IJob, JobId>.Empty)
                    .QueryWhenChanged(jobs =>
                        {
                            if (jobs.Items.FirstOrDefault()?.Definition is SynchronizeLoadoutJob sJob && sJob.LoadoutId == loadoutId)
                                return sJob.StatusMessage.AsSystemObservable();
                            return new BindableReactiveProperty<string>(value: "").AsSystemObservable();
                        }
                    ).Switch()
                    .OnUI()
                    .Subscribe(status => ProcessingText = status)
                    .DisposeWith(disposables);
            }
        );
    }

    private async Task Apply()
    {
        var loadout = Sdk.Loadouts.Loadout.Load(_conn.Db, _loadoutId);
        try
        {
            // MIST-003C.1:
            // Unknown Steam metadata is an advisory for
            // ordinary mod operations, not universal denial.
            var installation = loadout.InstallationInstance;

            if (installation.LocatorResult.Store == GameStore.Steam)
            {
                var hashesDb = await _serviceProvider
                    .GetRequiredService<IFileHashesService>()
                    .GetFileHashesDb();

                var advisory =
                    SteamResetPreflight.GetManagementAdvisory(
                        installation.LocatorResult.Store,
                        installation.LocatorResult.StoreIdentifier,
                        installation.LocatorResult.LocatorIds,
                        hashesDb);

                if (!string.IsNullOrWhiteSpace(advisory))
                {
                    var windowManager = _serviceProvider
                        .GetRequiredService<IWindowManager>();

                    // A loadout switch invokes the protected
                    // original-game reset path. Do not offer
                    // Continue when that reset is blocked.
                    var requiresLoadoutSwitch =
                        GameInstallMetadata.LastSyncedLoadout
                            .TryGetValue(
                                loadout.Installation,
                                out var activeLoadoutId)
                        && activeLoadoutId != loadout.Id;

                    if (requiresLoadoutSwitch)
                    {
                        var blockedDialog =
                            DialogFactory.CreateStandardDialog(
                                title: "M.I.S.T. — Reset unavailable",
                                new StandardDialogParameters
                                {
                                    Text =
                                        advisory
                                        + "\\n\\nSwitching loadouts requires "
                                        + "restoring the original game state. "
                                        + "M.I.S.T. cannot safely verify that "
                                        + "reset from the available Steam "
                                        + "metadata. This operation remains "
                                        + "blocked."
                                },
                                buttonDefinitions:
                                [
                                    DialogStandardButtons.Cancel
                                ]);

                        await windowManager.ShowDialog(
                            blockedDialog,
                            DialogWindowType.Modal);

                        return;
                    }

                    var confirmation =
                        DialogFactory.CreateStandardDialog(
                            title: "M.I.S.T. — Compatibility notice",
                            new StandardDialogParameters
                            {
                                Text =
                                    advisory
                                    + "\\n\\nYou may continue with "
                                    + "ordinary mod-management operations. "
                                    + "M.I.S.T. will still enforce its "
                                    + "filesystem and ownership checks. "
                                    + "Continue applying this loadout?"
                            },
                            buttonDefinitions:
                            [
                                DialogStandardButtons.Cancel,
                                new DialogButtonDefinition(
                                    "Continue",
                                    ButtonDefinitionId.Accept,
                                    ButtonAction.Accept,
                                    ButtonStyling.Primary)
                            ]);

                    var response = await windowManager.ShowDialog(
                        confirmation,
                        DialogWindowType.Modal);

                    if (response.ButtonId != ButtonDefinitionId.Accept)
                        return;
                }
            }

            // The synchronizer remains the enforcement layer.
            // A dialog response never bypasses its file guards.
            await Task.Run(async () =>
            {
                await _syncService.Synchronize(_loadoutId);
            });
            
            _notificationService.ShowToast(Language.ToastNotification_Applied__0__successfully, ToastNotificationVariant.Success);
        }
        catch (ExecutableInUseException)
        {
            await MessageBoxOkViewModel.ShowGameAlreadyRunningError(
                _serviceProvider,
                loadout.Installation.Name);
        }
        catch (Exception error) when (
            error is InvalidOperationException or
            IOException or
            AggregateException)
        {
            // MIST-003C.2:
            // Do not hide filesystem authorization failures
            // or incomplete recovery behind a success toast.
            //
            // The synchronizer remains responsible for
            // enforcing safety. This is presentation only.
            var dialog = DialogFactory.CreateStandardDialog(
                title: "M.I.S.T. — Apply could not finish",
                new StandardDialogParameters
                {
                    Text =
                        "M.I.S.T. could not complete this Apply " +
                        "operation. Some filesystem changes may " +
                        "require review before retrying." +
                        "\\n\\nDetails:\\n" +
                        error.Message
                },
                buttonDefinitions:
                [
                    new DialogButtonDefinition(
                        "OK",
                        ButtonDefinitionId.Accept,
                        ButtonAction.Accept,
                        ButtonStyling.Primary)
                ]);

            var windowManager = _serviceProvider
                .GetRequiredService<IWindowManager>();

            await windowManager.ShowDialog(
                dialog,
                DialogWindowType.Modal);
        }
    }
}
