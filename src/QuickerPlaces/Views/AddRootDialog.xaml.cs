using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using QuickerPlaces.Services;
using QuickerPlaces.Services.Activity;

namespace QuickerPlaces.Views;

/// <summary>The explicit opt-in before the first sample under a root.</summary>
public partial class AddRootDialog : Window
{
    private readonly string? _suggestedEquivalent;

    private AddRootDialog(Window owner, string path, INetworkDriveResolver resolver)
    {
        InitializeComponent();
        Owner = owner;
        RootPathText.Text = path;
        StorageText.Text = "Stored in " + Path.Combine(AppDataFolders.Local, "activity.json") + ".";
        _suggestedEquivalent = resolver.GetNetworkPath(path);
        if (_suggestedEquivalent is not null)
        {
            MappedEquivalentText.Text = $"Also count {_suggestedEquivalent} as this root.";
            MappedEquivalentCheck.Visibility = Visibility.Visible;
        }
    }

    public IReadOnlyList<string> EquivalentPrefixes { get; private set; } = Array.Empty<string>();

    /// <returns>Chosen equivalent prefixes, or null if the user cancelled.</returns>
    public static IReadOnlyList<string>? Show(Window owner, string path, INetworkDriveResolver resolver)
    {
        var dialog = new AddRootDialog(owner, path, resolver);
        return dialog.ShowDialog() == true ? dialog.EquivalentPrefixes : null;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (_suggestedEquivalent is not null && MappedEquivalentCheck.IsChecked == true)
            EquivalentPrefixes = new[] { _suggestedEquivalent };
        DialogResult = true;
    }
}
