namespace snipv.Services;

public interface IDialogService
{
    Task AlertAsync(string title, string message);
    Task<bool> ConfirmAsync(string title, string message, string accept, string cancel);
}

public class DialogService : IDialogService
{
    private static Page? CurrentPage =>
        Application.Current?.Windows.FirstOrDefault()?.Page;

    public Task AlertAsync(string title, string message) =>
        CurrentPage?.DisplayAlertAsync(title, message, "OK") ?? Task.CompletedTask;

    public Task<bool> ConfirmAsync(string title, string message, string accept, string cancel) =>
        CurrentPage?.DisplayAlertAsync(title, message, accept, cancel) ?? Task.FromResult(false);
}
