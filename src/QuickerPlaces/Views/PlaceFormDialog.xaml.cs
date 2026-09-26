using System.IO;
using System.Windows;
using System.Windows.Automation;
using Microsoft.Win32;
using QuickerPlaces.Models;
using QuickerPlaces.Services;

namespace QuickerPlaces.Views;

/// <summary>
/// The combined Add dialog (Alias + Resource together, SI §6.1's default
/// when the template had no existing data-entry-dialog precedent), the
/// two single-field edit dialogs, and Restore Place (the D15 conflict
/// flow), sharing one Window keyed by <see cref="PlaceFormMode"/>. Not
/// instantiated directly — use the static ShowAdd/ShowRenameAlias/
/// ShowEditResource/ShowRestore factory methods, which also own
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
    private readonly RestoreConflict? _conflict;

    // The alias last filled in from the folder path, so a later path change
    // can replace it — but never replace one the user typed.
    private string? _suggestedAlias;

    private PlaceFormDialog(PlaceFormMode mode, PlaceType type, PlacesService placesService, Place? editingPlace,
        RestoreConflict? conflict = null, Window? owner = null)
    {
        InitializeComponent();

        _mode = mode;
        _type = type;
        _placesService = placesService;
        _editingPlace = editingPlace;
        _conflict = conflict;

        Title = TitleFor(mode, type);
        ConfigureFields();

        // Browsing, typing or pasting a folder path fills in its deepest
        // folder name as the alias. Adding only: renaming, editing and
        // restoring already have an alias the user chose.
        if (mode == PlaceFormMode.AddFolder)
            ResourceTextBox.TextChanged += (_, _) => SuggestAlias();

        owner ??= Application.Current?.MainWindow;
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

    /// <summary>True if a Rename/Edit/Restore committed successfully.</summary>
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

    /// <summary>
    /// The D15 conflict flow, shared by every restore path (Ctrl+Z, the
    /// status bar's Undo, Recently Deleted's Restore selected): explains
    /// what now holds the place's alias and/or destination, with both
    /// fields prefilled and editable, and restores it under the edited
    /// values through PlacesService.TryRestore(place, alias, resource, …).
    /// Validation is Add's, shown in the error line as in every mode.
    /// Cancel changes nothing: the place stays in Recently Deleted.
    /// </summary>
    /// <param name="owner">The window to center on; the main window when null. Recently Deleted passes itself.</param>
    /// <returns>True if the place was restored.</returns>
    public static bool ShowRestore(RestoreConflict conflict, PlacesService placesService, Window? owner = null)
    {
        var dialog = new PlaceFormDialog(PlaceFormMode.Restore, conflict.Place.Type, placesService,
            editingPlace: conflict.Place, conflict: conflict, owner: owner);
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

            case PlaceFormMode.Restore:
                ExplanationText.Text = _conflict!.Explanation;
                ExplanationText.Visibility = Visibility.Visible;
                AliasTextBox.Text = _editingPlace!.Alias;
                ResourceLabel.Text = _type == PlaceType.Folder ? "Folder path" : "URL";
                BrowseButton.Visibility = _type == PlaceType.Folder ? Visibility.Visible : Visibility.Collapsed;
                ResourceTextBox.Text = _editingPlace.Resource;
                OkButton.Content = "Restore";
                // A screen reader announces the field, not the text above
                // it, so each field carries the explanation as its help.
                AutomationProperties.SetHelpText(AliasTextBox, _conflict.Explanation);
                AutomationProperties.SetHelpText(ResourceTextBox, _conflict.Explanation);
                break;
        }

        // Restore starts in whichever field is in the way (the alias when
        // both are), with its text selected so typing replaces it.
        var focusTarget = _mode switch
        {
            PlaceFormMode.EditResource => ResourceTextBox,
            PlaceFormMode.Restore when _conflict!.AliasHeldBy is null => ResourceTextBox,
            _ => AliasTextBox
        };
        Loaded += (_, _) =>
        {
            focusTarget.Focus();
            if (_mode == PlaceFormMode.Restore)
                focusTarget.SelectAll();
        };
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var folderDialog = new OpenFolderDialog { Title = "Select a folder" };

        if (!string.IsNullOrWhiteSpace(ResourceTextBox.Text) && Directory.Exists(ResourceTextBox.Text))
            folderDialog.InitialDirectory = ResourceTextBox.Text;

        if (folderDialog.ShowDialog(this) == true)
            ResourceTextBox.Text = folderDialog.FolderName;
    }

    /// <summary>
    /// Sets the alias to the folder path's deepest folder name while the alias
    /// is empty or still the last suggestion. Once the user types their own,
    /// it is left alone. The user can still edit a suggestion before saving,
    /// and a name already in use is reported on Save as usual.
    /// </summary>
    private void SuggestAlias()
    {
        var current = AliasTextBox.Text;
        if (!string.IsNullOrWhiteSpace(current) && current != _suggestedAlias)
            return;

        _suggestedAlias = AliasSuggestion.FromFolderPath(ResourceTextBox.Text) ?? string.Empty;
        AliasTextBox.Text = _suggestedAlias;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        var committed = _mode switch
        {
            PlaceFormMode.AddFolder or PlaceFormMode.AddUrl => TryCommitAdd(),
            PlaceFormMode.RenameAlias => TryCommitRename(),
            PlaceFormMode.EditResource => TryCommitEditResource(),
            PlaceFormMode.Restore => TryCommitRestore(),
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

    private bool TryCommitRestore()
    {
        // The save result is discarded here as in every other mode: the
        // caller refreshes from PlacesService.HasUnsavedChanges (D1 — a
        // failed save still leaves the place restored in memory).
        var result = _placesService.TryRestore(_editingPlace!, AliasTextBox.Text, ResourceTextBox.Text, out _);
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
        PlaceFormMode.Restore => "Restore Place",
        _ => "Place"
    };
}
