using System;
using System.Windows;
using QuickerPlaces.ViewModels;

namespace QuickerPlaces.Views;

/// <summary>
/// Save as a new layout, or Rename (configurable canvas plan M5). The
/// workspace does the saving: a refused name leaves the dialog open with the
/// reason, and the name selected for another try.
/// </summary>
public partial class SaveLayoutDialog : Window
{
    private readonly SaveLayoutViewModel _dialog;
    private readonly Func<SaveLayoutViewModel, bool> _save;

    private SaveLayoutDialog(Window owner, SaveLayoutViewModel dialog, Func<SaveLayoutViewModel, bool> save)
    {
        InitializeComponent();
        Owner = owner;
        DataContext = _dialog = dialog;
        _save = save;
        Loaded += (_, _) => SelectName();
    }

    /// <summary>Shows the dialog; true when <paramref name="save"/> accepted it.</summary>
    public static bool Show(Window owner, SaveLayoutViewModel dialog, Func<SaveLayoutViewModel, bool> save)
        => new SaveLayoutDialog(owner, dialog, save).ShowDialog() == true;

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_save(_dialog))
        {
            DialogResult = true;
            return;
        }

        SelectName();
    }

    private void SelectName()
    {
        NameBox.Focus();
        NameBox.SelectAll();
    }
}
