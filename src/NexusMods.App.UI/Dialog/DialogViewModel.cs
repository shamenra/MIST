using NexusMods.App.UI.Dialog.Enums;
using NexusMods.UI.Sdk;
using NexusMods.UI.Sdk.Dialog;
using ReactiveUI;

namespace NexusMods.App.UI.Dialog;

public class DialogViewModel : ReactiveObject, IDialogViewModel
{
    public ViewModelActivator Activator { get; }
    public R3.ReactiveCommand<ButtonDefinitionId, ButtonDefinitionId> ButtonPressCommand { get; }
    public string WindowTitle { get; }
    public DialogWindowSize DialogWindowSize { get; }
    public IViewModelInterface? ContentViewModel { get; }
    public DialogButtonDefinition[] ButtonDefinitions { get; }
    private bool _showChrome;
    public bool ShowChrome
    {
        get => _showChrome;
        set => this.RaiseAndSetIfChanged(ref _showChrome, value);
    }

    private StandardDialogResult _result = new();
    public StandardDialogResult Result
    {
        get => _result;
        set => this.RaiseAndSetIfChanged(ref _result, value);
    }

    public DialogViewModel(string title, DialogButtonDefinition[] buttonsDefinitions, IViewModelInterface contentViewModel, DialogWindowSize dialogWindowSize, bool showChrome) {
        Activator = new ViewModelActivator();
        WindowTitle = title;
        DialogWindowSize = dialogWindowSize;
        ContentViewModel = contentViewModel;
        ButtonDefinitions = buttonsDefinitions;
        ShowChrome = showChrome;
        Result = new StandardDialogResult();
        
        ButtonPressCommand = new R3.ReactiveCommand<ButtonDefinitionId, ButtonDefinitionId>(id =>
            {
                Console.WriteLine(id);
                Result = new StandardDialogResult
                {
                    ButtonId = id,
                    InputText = contentViewModel is IDialogStandardContentViewModel standardContentViewModel
                        ? standardContentViewModel.InputText
                        : string.Empty
                };
                return id;
            }
        );
    }
    
}
