using System.IO;
using System.Windows;
using Microsoft.Win32;
using QuickerPlaces.Models;
using QuickerPlaces.Services;

namespace QuickerPlaces.Views;

/// <summary>
/// The combined Add dialog (Alias + Resource together, SI §6.1's default
/// when the template had no existing data-entry-dialog precedent) and the
/// two single-field edit dialogs, sharing one Window keyed by
/// <see cref="PlaceFormMode"/>. Not instantiated directly — use the static
/// ShowAdd/ShowRenameAlias/ShowEditResource factory methods, which also own
/// committing the result via PlacesService so callers just get back
/// "what happened" rather than re-implementing the commit themselves. This
/// mirrors MessageForm's already-established "dialog calls straight into
/// the service/model layer" pattern rather than routing through an
/// IDialogService.
/// </summary>
public partial class PlaceFormDialog : Window
{
    private readonly PlaceFormMode _mode;
    private readonly PlaceType _type;
    private readonly PlacesService _placesService;
    private readonly Place? _editingPlace;

    private PlaceFormDialog(PlaceFormMode mode, PlaceType type, PlacesService placesService, Place? editingPlace)
    {
        InitializeComponent();

        _mode = mode;
        _type = type;
        _placesService = placesService;
        _editingPlace = editingPlace;

        Title = TitleFor(mode, type);
        ConfigureFields();

        var owner = Application.Current?.MainWindow;
        if (owner is not null && owner.IsLoaded && !ReferenceEquals(owner, this))
        {
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    /// <summary>Set only when Mode is AddFolder/AddUrl and OK committed successfully.</summary>
    public Place? CreatedPlace { get; private set; }

    /// <summary>True if a Rename/Edit committed successfully.</summary>
    public bool Committed { get; private set; }

    public static Place? ShowAdd(PlaceType type, PlacesService placesService)
    {
        var mode = type == PlaceType.Folder ? PlaceFormMode.AddFolder : PlaceFormMode.AddUrl;
        var dialog = new PlaceFormDialog(mode, type, placesService, editingPlace: null);
        dialog.ShowDialog();
        return dialog.CreatedPlace;
    }

    public static bool ShowRenameAlias(Place place, PlacesService placesService)
    {
        var dialog = new PlaceFormDialog(PlaceFormMode.RenameAlias, place.Type, placesService, editingPlace: place);
        dialog.ShowDialog();
        return dialog.Committed;
    }

    public static bool ShowEditResource(Place place, PlacesService placesService)
    {
        var dialog = new PlaceFormDialog(PlaceFormMode.EditResource, place.Type, placesService, editingPlace: place);
        dialog.ShowDialog();
        return dialog.Committed;
    }

    private void ConfigureFields()
    {
        switch (_mode)
        {
            case PlaceFormMode.AddFolder:
                ResourceLabel.Text = "Folder path";
                BrowseButton.Visibility = Visibility.Visible;
                break;

            case PlaceFormMode.AddUrl:
                ResourceLabel.Text = "URL";
                BrowseButton.Visibility = Visibility.Collapsed;
                break;

            case PlaceFormMode.RenameAlias:
                ResourcePanel.Visibility = Visibility.Collapsed;
                AliasTextBox.Text = _editingPlace!.Alias;
                break;

            case PlaceFormMode.EditResource:
                AliasPanel.Visibility = Visibility.Collapsed;
                ResourceLabel.Text = _type == PlaceType.Folder ? "Folder path" : "URL";
                BrowseButton.Visibility = _type == PlaceType.Folder ? Visibility.Visible : Visibility.Collapsed;
                ResourceTextBox.Text = _editingPlace!.Resource;
                break;
        }

        var focusTarget = _mode == PlaceFormMode.EditResource ? (UIElement)ResourceTextBox : AliasTextBox;
        Loaded += (_, _) => focusTarget.Focus();
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var folderDialog = new OpenFolderDialog { Title = "Select a folder" };

        if (!string.IsNullOrWhiteSpace(ResourceTextBox.Text) && Directory.Exists(ResourceTextBox.Text))
            folderDialog.InitialDirectory = ResourceTextBox.Text;

        if (folderDialog.ShowDialog(this) == true)
            ResourceTextBox.Text = folderDialog.FolderName;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        var committed = _mode switch
        {
            PlaceFormMode.AddFolder or PlaceFormMode.AddUrl => TryCommitAdd(),
            PlaceFormMode.RenameAlias => TryCommitRename(),
            PlaceFormMode.EditResource => TryCommitEditResource(),
            _ => false
        };

        if (committed)
            DialogResult = true;
    }

    private bool TryCommitAdd()
    {
        // The persistence outcome (did the save reach disk) is deliberately
        // not surfaced here — this dialog's job stays "did validation
        // pass". HasUnsavedChanges on PlacesService is the durable record
        // of a failed save; a later step's banner reads it there rather
        // than this dialog carrying its own copy of the failure.
        var result = _placesService.TryAdd(AliasTextBox.Text, _type, ResourceTextBox.Text, out var created, out _);
        if (!result.Success)
        {
            ShowError(result.ErrorMessage!);
            return false;
        }

        CreatedPlace = created;
        return true;
    }

    private bool TryCommitRename()
    {
        var result = _placesService.TryRenameAlias(_editingPlace!, AliasTextBox.Text, out _);
        if (!result.Success)
        {
            ShowError(result.ErrorMessage!);
            return false;
        }

        Committed = true;
        return true;
    }

    private bool TryCommitEditResource()
    {
        var result = _placesService.TryEditResource(_editingPlace!, ResourceTextBox.Text, out _);
        if (!result.Success)
        {
            ShowError(result.ErrorMessage!);
            return false;
        }

        Committed = true;
        return true;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private static string TitleFor(PlaceFormMode mode, PlaceType type) => mode switch
    {
        PlaceFormMode.AddFolder => "Add Folder",
        PlaceFormMode.AddUrl => "Add URL",
        PlaceFormMode.RenameAlias => "Rename Alias",
        PlaceFormMode.EditResource => type == PlaceType.Folder ? "Edit Folder Path" : "Edit URL",
        _ => "Place"
    };
}
