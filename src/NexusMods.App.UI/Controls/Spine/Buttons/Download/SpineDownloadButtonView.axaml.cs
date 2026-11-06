using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia.ReactiveUI;
using NexusMods.App.UI.Extensions;
using ReactiveUI;
using static NexusMods.App.UI.Helpers.StyleConstants.SpineDownloadButton;

namespace NexusMods.App.UI.Controls.Spine.Buttons.Download;

public partial class SpineDownloadButtonView : ReactiveUserControl<ISpineDownloadButtonViewModel>
{
    public SpineDownloadButtonView()
    {
        InitializeComponent();

        this.WhenActivated(d =>
        {
            this.WhenAnyValue(view => view.ViewModel!.Click)
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(command => ParentButton.Command = command)
                .DisposeWith(d);

            this.WhenAnyValue(view => view.ViewModel!.Progress)
                .Select(p => p.HasValue)
                .BindToClasses(ParentButton, Progress, Idle)
                .DisposeWith(d);

            this.WhenAnyValue(view => view.ViewModel!.IsActive)
                .BindToActive(ParentButton)
                .DisposeWith(d);

            this.WhenAnyValue(view => view.ViewModel!.Progress)
                .Where(p => p.HasValue)
                .Select(p => p.Value.Value * 360)
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(angle => ProgressArc.SweepAngle = angle)
                .DisposeWith(d);

            this.WhenAnyValue(view => view.ViewModel!.Number)
                .Select(n => n.ToString("###0.0"))
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(text => NumberTextBlock.Text = text)
                .DisposeWith(d);

            this.WhenAnyValue(view => view.ViewModel!.Units)
                .Select(n => n.ToUpperInvariant())
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(text => UnitsTextBlock.Text = text)
                .DisposeWith(d);

            this.OneWayBind(ViewModel, vm => vm.ToolTip,
                    v => v.ToolTipTextBlock.Text)
                .DisposeWith(d);
        });
    }
}
