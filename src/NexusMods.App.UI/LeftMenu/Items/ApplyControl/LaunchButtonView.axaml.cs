using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia.ReactiveUI;
using ReactiveUI;

namespace NexusMods.App.UI.LeftMenu.Items;

public partial class LaunchButtonView : ReactiveUserControl<ILaunchButtonViewModel>
{
    public LaunchButtonView()
    {
        InitializeComponent();
        
        this.WhenActivated(d =>
        {
            var isRunning = ViewModel!.IsRunningObservable;
            var isNotRunning = ViewModel!.IsRunningObservable.Select(running => !running);

            isNotRunning
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(enabled => LaunchButton.IsEnabled = enabled)
                .DisposeWith(d);
            
            // Show progress bar when running
            isRunning
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(visible => LaunchSpinner.IsVisible = visible)
                .DisposeWith(d);
            
            // Show icon when not running
            isNotRunning
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(visible => LaunchIcon.IsVisible = visible)
                .DisposeWith(d);
            
            // Bind the 'launch' button.
            this.WhenAnyValue(view => view.ViewModel!.Command)
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(command => LaunchButton.Command = command)
                .DisposeWith(d);
            
            // Set the 'play' / 'running' text.
            this.WhenAnyValue(view => view.ViewModel!.Label)
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(text => LaunchText.Text = text)
                .DisposeWith(d);
        });
    }
}

