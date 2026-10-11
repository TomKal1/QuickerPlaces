using System.Threading.Tasks;
using System.Windows;
using QuickerPlaces.Models;
using QuickerPlaces.Services.Revit.Opening;

namespace QuickerPlaces.Views;

/// <summary>
/// The yes/no questions a Revit open asks (such as "Allow Load Once for X
/// next time?"), shown in the app's themed <see cref="MessageForm"/>. The
/// coordinator asks on the UI thread; the dispatcher check is only a guard.
/// </summary>
public sealed class MessageFormRevitPrompts : IRevitOpenPrompts
{
    public Task<bool> AskAsync(string question)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            return Task.FromResult(Ask(question));

        return dispatcher.InvokeAsync(() => Ask(question)).Task;
    }

    private static bool Ask(string question)
        => MessageForm.Show(question, "Revit add-in", MessageFormButtons.YesNo, MessageFormIcon.Question) == MessageFormResult.Yes;
}
