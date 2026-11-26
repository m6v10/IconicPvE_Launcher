using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IconicLauncher.Core.Models;
using IconicLauncher.Core.Services;

namespace IconicLauncher.ViewModels;

public enum GraphicsChoice
{
    Cancel,
    ApplyMedium,
    PlayAnyway
}

public sealed partial class GraphicsWarningDialogViewModel : ObservableObject
{
    private readonly TaskCompletionSource<GraphicsChoice> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);

    [ObservableProperty]
    private bool dontShowAgain;

    public GraphicsWarningDialogViewModel(MainViewModel owner, DayZVideoState state, GraphicsAdvisory advisory)
    {
        Title = advisory.Title;
        Message = advisory.Message;
        CurrentText = $"Your DayZ graphics: {state.PresetLabel.ToUpperInvariant()}";
        AboveText = "Above Medium: " + string.Join(", ", state.AboveMedium.Select(k => k.Label));
    }

    public string Title { get; }

    public string Message { get; }

    public string CurrentText { get; }

    public string AboveText { get; }

    public Task<GraphicsChoice> Result => _result.Task;

    public void Complete(GraphicsChoice choice)
    {
        _result.TrySetResult(choice);
    }

    [RelayCommand]
    private void ApplyMedium() => Complete(GraphicsChoice.ApplyMedium);

    [RelayCommand]
    private void PlayAnyway() => Complete(GraphicsChoice.PlayAnyway);

    [RelayCommand]
    private void Cancel() => Complete(GraphicsChoice.Cancel);
}
