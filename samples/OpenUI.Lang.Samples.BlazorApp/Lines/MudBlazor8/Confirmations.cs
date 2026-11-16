using MudBlazor;

namespace OpenUI.Lang.Samples.BlazorApp;

/// <summary>The yes-or-cancel box the pages ask with. MudBlazor 8 names it ShowMessageBox.</summary>
internal static class Confirmations
{
    /// <summary>True when the person picked <paramref name="yes"/>.</summary>
    public static async Task<bool> ConfirmAsync(this IDialogService dialogs, string title, string message, string yes) =>
        await dialogs.ShowMessageBox(title, message, yesText: yes, cancelText: "Cancel") == true;
}
