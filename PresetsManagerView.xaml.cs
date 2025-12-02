// PresetsManagerView.xaml.cs
using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;

namespace LaunchPlugin
{
    public partial class PresetsManagerView : UserControl, INotifyPropertyChanged
    {
        private readonly FuelCalcs _vm;

        private RacePreset _editingPreset;      // working copy for the right pane
        private RacePreset _editorSelection;    // local selection for the left list (decoupled from VM.SelectedPreset)
        private string _originalName;           // original name of the working copy, for rename-on-save

        public event PropertyChangedEventHandler PropertyChanged;

        public RacePreset EditingPreset
        {
            get => _editingPreset;
            private set
            {
                _editingPreset = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EditingPreset)));
            }
        }

        /// <summary>
        /// Local selection for the presets list on this view.
        /// This is intentionally NOT bound to VM.SelectedPreset so the Fuel tab combo won't jump.
        /// </summary>
        public RacePreset EditorSelection
        {
            get => _editorSelection;
            set
            {
                if (!ReferenceEquals(_editorSelection, value))
                {
                    _editorSelection = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EditorSelection)));
                    RebuildWorkingCopyFromEditorSelection();
                }
            }
        }

        public PresetsManagerView(FuelCalcs fuelVm)
        {
            InitializeComponent();
            _vm = fuelVm ?? throw new ArgumentNullException(nameof(fuelVm));

            // Use the VM as DataContext for lists/collections, but keep selection local
            DataContext = _vm;

            // Start by mirroring whatever the Fuel tab had selected; from now on selection is local
            EditorSelection = _vm.SelectedPreset;
        }

        /// <summary>
        /// Build/refresh the right-side editor from the local selection.
        /// </summary>
        // Build/refresh the right-side editor from the local selection.
        private void RebuildWorkingCopyFromEditorSelection()
        {
            var s = EditorSelection;
            // No defaults here: blank when nothing selected
            EditingPreset = s != null ? Clone(s) : new RacePreset { Name = "" };
            _originalName = s?.Name ?? "";
        }

        private static RacePreset Clone(RacePreset p) => new RacePreset
        {
            Name = p.Name,
            Type = p.Type,
            RaceMinutes = p.RaceMinutes,
            RaceLaps = p.RaceLaps,
            MandatoryStopRequired = p.MandatoryStopRequired,
            TireChangeTimeSec = p.TireChangeTimeSec,
            MaxFuelLitres = p.MaxFuelLitres,
            ContingencyInLaps = p.ContingencyInLaps,
            ContingencyValue = p.ContingencyValue
        };

        private void OnSaveEdits(object sender, RoutedEventArgs e)
        {
            try
            {
                if (EditingPreset == null || string.IsNullOrWhiteSpace(EditingPreset.Name))
                {
                    MessageBox.Show("Preset must have a name.", "Presets",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Normalize by type (only one of Minutes/Laps should be set)
                if (EditingPreset.Type == RacePresetType.TimeLimited)
                {
                    if (!EditingPreset.RaceMinutes.HasValue || EditingPreset.RaceMinutes.Value < 1)
                        EditingPreset.RaceMinutes = 1;
                    EditingPreset.RaceLaps = null;
                }
                else
                {
                    if (!EditingPreset.RaceLaps.HasValue || EditingPreset.RaceLaps.Value < 1)
                        EditingPreset.RaceLaps = 1;
                    EditingPreset.RaceMinutes = null;
                }

                // Save (VM updates in place, persists, refreshes collection, reapplies if active)
                var saved = _vm.SavePresetEdits(_originalName, Clone(EditingPreset));
                _originalName = saved?.Name ?? EditingPreset.Name; // track new name for subsequent edits

                // Keep editing the same (possibly renamed) preset using LOCAL selection
                EditorSelection = saved ?? _vm.AvailablePresets?.FirstOrDefault(x =>
                    string.Equals(x.Name, _originalName, StringComparison.OrdinalIgnoreCase));

                MessageBox.Show("Preset saved.", "Presets",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Save Preset",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OnCreateNewPreset(object sender, RoutedEventArgs e)
        {
            try
            {
                var created = _vm.CreatePresetFromDefaults();

                // Immediately select it locally so the right-hand editor shows it
                EditorSelection = created;

                // No need to call RebuildWorkingCopy... the EditorSelection setter already rebuilds
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Create Preset",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OnDiscardChanges(object sender, RoutedEventArgs e) => RebuildWorkingCopyFromEditorSelection();

        /// <summary>
        /// This is your "Save Fuel Tab Data as Preset" button (kept as-is, logic unchanged).
        /// It reads the current Fuel screen values via VM and saves a new preset.
        /// </summary>
        private void OnSaveCurrentAsPreset(object sender, RoutedEventArgs e)
        {
            try
            {
                var name = ActionNameBox.Text?.Trim();
                if (string.IsNullOrEmpty(name))
                {
                    MessageBox.Show("Enter a preset name first.", "Presets",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var saved = _vm.SaveCurrentAsPreset(name, overwriteIfExists: false);
                ActionNameBox.Clear();

                // After VM save, reselect the newly created preset locally if it exists
                EditorSelection = saved ?? _vm.AvailablePresets?.FirstOrDefault(x =>
                    string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Save Preset",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OnRenamePreset(object sender, RoutedEventArgs e)
        {
            try
            {
                var sel = EditorSelection;
                if (sel == null) return;

                var newName = ActionNameBox.Text?.Trim();
                if (string.IsNullOrEmpty(newName))
                {
                    MessageBox.Show("Enter the new name.", "Rename Preset",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                _vm.RenamePreset(sel.Name, newName);
                ActionNameBox.Clear();

                // Move local selection to the renamed item and rebuild editor
                EditorSelection = _vm.AvailablePresets?.FirstOrDefault(x =>
                    string.Equals(x.Name, newName, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Rename Preset",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OnDeletePreset(object sender, RoutedEventArgs e)
        {
            try
            {
                var sel = EditorSelection;
                if (sel == null) return;

                var confirm = MessageBox.Show($"Delete preset '{sel.Name}'?",
                    "Delete Preset", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (confirm == MessageBoxResult.Yes)
                {
                    _vm.DeletePreset(sel.Name);
                    // pick first remaining item (or none) and rebuild editor
                    EditorSelection = _vm.AvailablePresets?.FirstOrDefault();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Delete Preset",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
