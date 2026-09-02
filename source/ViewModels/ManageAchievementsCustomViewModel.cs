using Microsoft.Win32;
using Newtonsoft.Json;
using Playnite.SDK;
using PlayniteAchievements.Models.Achievements;
using PlayniteAchievements.Common;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services;
using PlayniteAchievements.Services.Achievements;
using PlayniteAchievements.Services.GameCustomData;
using PlayniteAchievements.Services.Images;
using PlayniteAchievements.ViewModels.Items;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using AsyncCommand = PlayniteAchievements.Common.AsyncCommand;
using ObservableObject = PlayniteAchievements.Common.ObservableObject;
using RelayCommand = PlayniteAchievements.Common.RelayCommand;

namespace PlayniteAchievements.ViewModels
{
    public sealed class ManageAchievementsCustomViewModel : ObservableObject
    {
        private readonly Guid _gameId;
        private readonly string _gameIdText;
        private readonly AchievementOverridesService _achievementOverridesService;
        private readonly GameCustomDataStore _gameCustomDataStore;
        private readonly ManagedCustomIconService _managedCustomIconService;
        private readonly ILogger _logger;
        private readonly CustomAchievementTextImportService _textImportService =
            new CustomAchievementTextImportService();

        private CustomAchievementEditItem _selectedRow;
        private bool _hasChanges;
        private bool _hasRows;
        private bool _hasValidationErrors;
        private bool _isSaving;
        private string _statusText;
        private bool _statusIsError;
        private string _baselineCollectionSignature;

        public ManageAchievementsCustomViewModel(
            Guid gameId,
            AchievementOverridesService achievementOverridesService,
            GameCustomDataStore gameCustomDataStore,
            ManagedCustomIconService managedCustomIconService,
            ILogger logger)
        {
            _gameId = gameId;
            _gameIdText = gameId.ToString("D");
            _achievementOverridesService = achievementOverridesService ?? throw new ArgumentNullException(nameof(achievementOverridesService));
            _gameCustomDataStore = gameCustomDataStore ?? throw new ArgumentNullException(nameof(gameCustomDataStore));
            _managedCustomIconService = managedCustomIconService;
            _logger = logger;

            AchievementRows = new ObservableCollection<CustomAchievementEditItem>();
            AddCommand = new RelayCommand(_ => AddRow(), _ => !IsSaving);
            DuplicateCommand = new RelayCommand(_ => DuplicateSelected(), _ => SelectedRow != null && !IsSaving);
            DeleteCommand = new RelayCommand(_ => DeleteSelected(), _ => SelectedRow != null && !IsSaving);
            PasteImportCommand = new RelayCommand(_ => PasteImport(), _ => !IsSaving);
            ImportFileCommand = new RelayCommand(_ => ImportFile(), _ => !IsSaving);
            ExportTemplateCommand = new RelayCommand(_ => ExportTemplate(), _ => !IsSaving);
            ExportAchievementsCommand = new RelayCommand(_ => ExportAchievements(), _ => HasRows && !IsSaving);
            SaveCommand = new AsyncCommand(_ => SaveAsync(), _ => CanSave);
            RevertCommand = new RelayCommand(_ => Revert(), _ => HasChanges && !IsSaving);
            ClearCommand = new RelayCommand(_ => ClearRows(), _ => HasRows && !IsSaving);

            ReloadData();
        }

        public event EventHandler CustomAchievementsSaved;

        public ObservableCollection<CustomAchievementEditItem> AchievementRows { get; }

        public RelayCommand AddCommand { get; }

        public RelayCommand DuplicateCommand { get; }

        public RelayCommand DeleteCommand { get; }

        public RelayCommand PasteImportCommand { get; }

        public RelayCommand ImportFileCommand { get; }

        public RelayCommand ExportTemplateCommand { get; }

        public RelayCommand ExportAchievementsCommand { get; }

        public AsyncCommand SaveCommand { get; }

        public RelayCommand RevertCommand { get; }

        public RelayCommand ClearCommand { get; }

        public CustomAchievementEditItem SelectedRow
        {
            get => _selectedRow;
            set
            {
                if (SetValueAndReturn(ref _selectedRow, value))
                {
                    OnPropertyChanged(nameof(HasSelectedRow));
                    RaiseCommandStates();
                }
            }
        }

        public bool HasSelectedRow => SelectedRow != null;

        public bool HasRows
        {
            get => _hasRows;
            private set
            {
                if (SetValueAndReturn(ref _hasRows, value))
                {
                    RaiseCommandStates();
                }
            }
        }

        public bool HasChanges
        {
            get => _hasChanges;
            private set
            {
                if (SetValueAndReturn(ref _hasChanges, value))
                {
                    RaiseCommandStates();
                }
            }
        }

        public bool HasValidationErrors
        {
            get => _hasValidationErrors;
            private set
            {
                if (SetValueAndReturn(ref _hasValidationErrors, value))
                {
                    RaiseCommandStates();
                }
            }
        }

        public bool IsSaving
        {
            get => _isSaving;
            private set
            {
                if (SetValueAndReturn(ref _isSaving, value))
                {
                    RaiseCommandStates();
                }
            }
        }

        public bool CanSave => HasChanges && !HasValidationErrors && !IsSaving;

        public IReadOnlyList<CustomAchievementSelectionOption> RarityOptions { get; } =
            new[]
            {
                RarityTier.Common,
                RarityTier.Uncommon,
                RarityTier.Rare,
                RarityTier.UltraRare
            }
            .Select(tier => new CustomAchievementSelectionOption(tier.ToString(), tier.ToDisplayText()))
            .ToList();

        public IReadOnlyList<CustomAchievementSelectionOption> TrophyTypeOptions { get; } =
            new[]
            {
                new CustomAchievementSelectionOption(string.Empty, L("LOCPlayAch_Common_None", "None")),
                new CustomAchievementSelectionOption("bronze", L("LOCPlayAch_Trophy_Bronze", "Bronze")),
                new CustomAchievementSelectionOption("silver", L("LOCPlayAch_Trophy_Silver", "Silver")),
                new CustomAchievementSelectionOption("gold", L("LOCPlayAch_Trophy_Gold", "Gold")),
                new CustomAchievementSelectionOption("platinum", L("LOCPlayAch_Trophy_Platinum", "Platinum"))
            };

        public string StatusText
        {
            get => _statusText;
            private set
            {
                if (SetValueAndReturn(ref _statusText, value))
                {
                    OnPropertyChanged(nameof(HasStatusText));
                }
            }
        }

        public bool HasStatusText => !string.IsNullOrWhiteSpace(StatusText);

        public bool StatusIsError
        {
            get => _statusIsError;
            private set => SetValue(ref _statusIsError, value);
        }

        public void ReloadData()
        {
            try
            {
                var data = _gameCustomDataStore.LoadOrDefault(_gameId);
                ReplaceRows((data?.CustomAchievements ?? new List<CustomAchievementDefinition>())
                    .Select(CustomAchievementEditItem.FromDefinition));
                CaptureCollectionBaseline();
                SetStatus(null, false);
                RefreshComputedState();
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"Failed loading custom achievements for gameId={_gameId}.");
                ReplaceRows(Array.Empty<CustomAchievementEditItem>());
                SetStatus(string.Format(L("LOCPlayAch_Status_Failed", "Error: {0}"), ex.Message), true);
            }
        }

        public void RefreshData()
        {
            if (!HasChanges)
            {
                ReloadData();
            }
        }

        private void AddRow()
        {
            var row = CustomAchievementEditItem.CreateNew(AchievementRows.Count + 1);
            AttachRow(row);
            AchievementRows.Add(row);
            SelectedRow = row;
            SetStatus(null, false);
            RefreshComputedState();
        }

        private void DuplicateSelected()
        {
            if (SelectedRow == null)
            {
                return;
            }

            var row = SelectedRow.CloneForDuplicate();
            AttachRow(row);
            var insertIndex = Math.Max(0, AchievementRows.IndexOf(SelectedRow) + 1);
            AchievementRows.Insert(insertIndex, row);
            SelectedRow = row;
            SetStatus(null, false);
            RefreshComputedState();
        }

        private void DeleteSelected()
        {
            if (SelectedRow == null)
            {
                return;
            }

            var row = SelectedRow;
            row.PropertyChanged -= Row_PropertyChanged;
            AchievementRows.Remove(row);
            SelectedRow = AchievementRows.FirstOrDefault();
            SetStatus(null, false);
            RefreshComputedState();
        }

        private void PasteImport()
        {
            try
            {
                if (!Clipboard.ContainsText())
                {
                    SetStatus(L("LOCPlayAch_ManageAchievements_Custom_NoClipboardText", "Clipboard does not contain text."), true);
                    return;
                }

                ImportText(Clipboard.GetText());
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, $"Failed importing custom achievements from clipboard for gameId={_gameId}.");
                SetStatus(string.Format(L("LOCPlayAch_Status_Failed", "Error: {0}"), ex.Message), true);
            }
        }

        private void ImportFile()
        {
            var dialog = new OpenFileDialog
            {
                Filter = CustomAchievementsPackageFilter,
                CheckFileExists = true,
                Multiselect = false
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            try
            {
                MergeImportedDefinitions(_gameCustomDataStore.ImportCustomAchievementsPackage(_gameId, dialog.FileName));
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, $"Failed importing custom achievements from '{dialog.FileName}' for gameId={_gameId}.");
                SetStatus(string.Format(L("LOCPlayAch_Status_Failed", "Error: {0}"), ex.Message), true);
            }
        }

        private void ImportText(string text)
        {
            MergeImportedDefinitions(_textImportService.Import(text));
        }

        private void MergeImportedDefinitions(CustomAchievementTextImportResult result)
        {
            if (result.HasErrors)
            {
                SetStatus(string.Join(Environment.NewLine, result.Errors.Take(8)), true);
                return;
            }

            if (result.Definitions.Count == 0)
            {
                SetStatus(L("LOCPlayAch_ManageAchievements_Custom_NoImportRows", "No importable achievements were found."), true);
                return;
            }

            var updated = 0;
            var added = 0;
            var byId = AchievementRows
                .Where(row => row != null && !string.IsNullOrWhiteSpace(row.NormalizedId))
                .GroupBy(row => row.NormalizedId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var definition in result.Definitions)
            {
                var id = CustomAchievementProjectionService.NormalizeId(definition.Id);
                if (!string.IsNullOrWhiteSpace(id) && byId.TryGetValue(id, out var existing))
                {
                    existing.ApplyDefinition(definition, preserveOriginalId: true);
                    updated++;
                    continue;
                }

                var row = CustomAchievementEditItem.FromDefinition(definition);
                row.MarkAsNewImport();
                AttachRow(row);
                AchievementRows.Add(row);
                added++;
            }

            SelectedRow = AchievementRows.LastOrDefault();
            SetStatus(
                string.Format(
                    L("LOCPlayAch_ManageAchievements_Custom_ImportSummary", "Imported {0} rows ({1} added, {2} updated). Save to apply."),
                    result.Definitions.Count,
                    added,
                    updated),
                false);
            RefreshComputedState();
        }

        private const string CustomAchievementsPackageFilter =
            "Playnite Achievements Custom Achievements (*.pacustom)|*.pacustom";

        private void ExportTemplate()
        {
            ExportPackage("custom-achievements-template.pacustom", Array.Empty<CustomAchievementDefinition>(), "custom achievement template");
        }

        private void ExportAchievements()
        {
            var definitions = BuildValidatedDefinitions(out _, out var errors);
            if (errors.Count > 0)
            {
                SetStatus(string.Join(Environment.NewLine, errors.Take(8)), true);
                RefreshComputedState();
                return;
            }

            ExportPackage("custom-achievements.pacustom", definitions, "custom achievements");
        }

        private void ExportPackage(
            string defaultFileName,
            IReadOnlyList<CustomAchievementDefinition> definitions,
            string description)
        {
            var dialog = new SaveFileDialog
            {
                Filter = CustomAchievementsPackageFilter,
                AddExtension = true,
                DefaultExt = GameCustomDataStore.CustomAchievementsPackageFileExtension,
                FileName = defaultFileName,
                OverwritePrompt = true
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            try
            {
                var destinationPath = dialog.FileName;
                if (!destinationPath.EndsWith(GameCustomDataStore.CustomAchievementsPackageFileExtension, StringComparison.OrdinalIgnoreCase))
                {
                    destinationPath += GameCustomDataStore.CustomAchievementsPackageFileExtension;
                }

                _gameCustomDataStore.ExportCustomAchievementsPackage(_gameId, definitions, destinationPath);
                SetStatus(L("LOCPlayAch_Status_Succeeded", "Success!"), false);
            }
            catch (Exception ex)
            {
                _logger?.Warn(ex, $"Failed exporting {description} to '{dialog.FileName}'.");
                SetStatus(string.Format(L("LOCPlayAch_Status_Failed", "Error: {0}"), ex.Message), true);
            }
        }

        private async Task SaveAsync()
        {
            if (!CanSave)
            {
                return;
            }

            try
            {
                IsSaving = true;
                var definitions = BuildValidatedDefinitions(out var renameMap, out var errors);
                if (errors.Count > 0)
                {
                    SetStatus(string.Join(Environment.NewLine, errors.Take(8)), true);
                    RefreshComputedState();
                    return;
                }

                await MaterializeIconSourcesAsync(definitions, errors).ConfigureAwait(true);
                if (errors.Count > 0)
                {
                    SetStatus(string.Join(Environment.NewLine, errors.Take(8)), true);
                    RefreshComputedState();
                    return;
                }

                _achievementOverridesService.SetCustomAchievements(_gameId, definitions, renameMap);
                CommitRows(definitions);
                SetStatus(L("LOCPlayAch_Status_Succeeded", "Success!"), false);
                RefreshComputedState();
                CustomAchievementsSaved?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"Failed saving custom achievements for gameId={_gameId}.");
                SetStatus(string.Format(L("LOCPlayAch_Status_Failed", "Error: {0}"), ex.Message), true);
            }
            finally
            {
                IsSaving = false;
            }
        }

        private void Revert()
        {
            ReloadData();
        }

        private void ClearRows()
        {
            var result = MessageBox.Show(
                L("LOCPlayAch_ManageAchievements_Custom_ClearConfirm", "Clear all custom achievements for this game?"),
                L("LOCPlayAch_Title_PluginName", "Playnite Achievements"),
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.OK)
            {
                return;
            }

            foreach (var row in AchievementRows)
            {
                row.PropertyChanged -= Row_PropertyChanged;
            }

            AchievementRows.Clear();
            SelectedRow = null;
            SetStatus(null, false);
            RefreshComputedState();
        }

        private List<CustomAchievementDefinition> BuildValidatedDefinitions(
            out Dictionary<string, string> renameMap,
            out List<string> errors)
        {
            renameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            errors = new List<string>();
            var definitions = new List<CustomAchievementDefinition>();
            var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var explicitIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < AchievementRows.Count; i++)
            {
                var row = AchievementRows[i];
                if (row == null || row.IsBlank)
                {
                    continue;
                }

                var explicitId = row.NormalizedId;
                if (!string.IsNullOrWhiteSpace(explicitId) && !explicitIds.Add(explicitId))
                {
                    errors.Add($"Row {i + 1}: duplicate custom ID '{explicitId}'.");
                    row.ValidationMessage = $"Duplicate ID '{explicitId}'.";
                    continue;
                }

                var definition = row.ToDefinition(usedIds, out var rowErrors);
                if (rowErrors.Count > 0)
                {
                    var message = string.Join(" ", rowErrors);
                    row.ValidationMessage = message;
                    errors.Add($"Row {i + 1}: {message}");
                    continue;
                }

                row.ValidationMessage = null;
                definitions.Add(definition);

                var oldApiName = row.OriginalApiName;
                var newApiName = CustomAchievementProjectionService.BuildApiName(definition.Id);
                if (!string.IsNullOrWhiteSpace(oldApiName) &&
                    !string.IsNullOrWhiteSpace(newApiName) &&
                    !string.Equals(oldApiName, newApiName, StringComparison.OrdinalIgnoreCase))
                {
                    renameMap[oldApiName] = newApiName;
                }
            }

            return definitions;
        }

        private async Task MaterializeIconSourcesAsync(
            IReadOnlyList<CustomAchievementDefinition> definitions,
            ICollection<string> errors)
        {
            if (_managedCustomIconService == null || definitions == null || definitions.Count == 0)
            {
                return;
            }

            var fileStems = AchievementIconCachePathBuilder.BuildFileStems(
                definitions.Select(definition => CustomAchievementProjectionService.BuildApiName(definition.Id)));

            foreach (var definition in definitions)
            {
                var apiName = CustomAchievementProjectionService.BuildApiName(definition.Id);
                if (!fileStems.TryGetValue(apiName, out var fileStem) || string.IsNullOrWhiteSpace(fileStem))
                {
                    continue;
                }

                definition.UnlockedIconPath = await MaterializeIconSourceAsync(
                    definition.UnlockedIconPath,
                    fileStem,
                    AchievementIconVariant.Unlocked,
                    errors).ConfigureAwait(true);
                definition.LockedIconPath = await MaterializeIconSourceAsync(
                    definition.LockedIconPath,
                    fileStem,
                    AchievementIconVariant.Locked,
                    errors).ConfigureAwait(true);
            }
        }

        private async Task<string> MaterializeIconSourceAsync(
            string value,
            string fileStem,
            AchievementIconVariant variant,
            ICollection<string> errors)
        {
            var normalized = NormalizeText(value);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return null;
            }

            if (_managedCustomIconService.IsManagedCustomIconPath(normalized, _gameIdText))
            {
                return normalized;
            }

            if (IsHttpUrl(normalized))
            {
                try
                {
                    await _managedCustomIconService
                        .MaterializeCustomIconAsync(
                            normalized,
                            _gameIdText,
                            fileStem,
                            variant,
                            CancellationToken.None,
                            overwriteExistingTarget: true)
                        .ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    _logger?.Warn(ex, $"Failed caching custom achievement icon URL '{normalized}' for gameId={_gameId}.");
                }

                return normalized;
            }

            if (!File.Exists(normalized))
            {
                errors?.Add($"Icon file does not exist: {normalized}");
                return normalized;
            }

            var managedPath = await _managedCustomIconService
                .MaterializeCustomIconAsync(
                    normalized,
                    _gameIdText,
                    fileStem,
                    variant,
                    CancellationToken.None,
                    overwriteExistingTarget: true)
                .ConfigureAwait(true);

            return string.IsNullOrWhiteSpace(managedPath) ? normalized : managedPath;
        }

        private void CommitRows(IReadOnlyList<CustomAchievementDefinition> definitions)
        {
            ReplaceRows((definitions ?? Array.Empty<CustomAchievementDefinition>())
                .Select(CustomAchievementEditItem.FromDefinition));
            CaptureCollectionBaseline();
        }

        private void ReplaceRows(IEnumerable<CustomAchievementEditItem> rows)
        {
            foreach (var row in AchievementRows)
            {
                row.PropertyChanged -= Row_PropertyChanged;
            }

            AchievementRows.Clear();
            foreach (var row in rows ?? Enumerable.Empty<CustomAchievementEditItem>())
            {
                AttachRow(row);
                AchievementRows.Add(row);
            }

            SelectedRow = AchievementRows.FirstOrDefault();
        }

        private void AttachRow(CustomAchievementEditItem row)
        {
            if (row == null)
            {
                return;
            }

            row.PropertyChanged -= Row_PropertyChanged;
            row.PropertyChanged += Row_PropertyChanged;
        }

        private void Row_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e == null || e.PropertyName == nameof(CustomAchievementEditItem.ValidationMessage))
            {
                return;
            }

            SetStatus(null, false);
            RefreshComputedState();
        }

        private void RefreshComputedState()
        {
            HasRows = AchievementRows.Count > 0;
            var errors = new List<string>();
            _ = BuildValidatedDefinitions(out _, out errors);
            HasValidationErrors = errors.Count > 0;
            HasChanges = !string.Equals(BuildCollectionSignature(), _baselineCollectionSignature, StringComparison.Ordinal);
            RaiseCommandStates();
        }

        private void CaptureCollectionBaseline()
        {
            _baselineCollectionSignature = BuildCollectionSignature();
        }

        private string BuildCollectionSignature()
        {
            return JsonConvert.SerializeObject(AchievementRows
                .Where(row => row != null)
                .Select(row => row.StateSignature)
                .ToList());
        }

        private void RaiseCommandStates()
        {
            AddCommand.RaiseCanExecuteChanged();
            DuplicateCommand.RaiseCanExecuteChanged();
            DeleteCommand.RaiseCanExecuteChanged();
            PasteImportCommand.RaiseCanExecuteChanged();
            ImportFileCommand.RaiseCanExecuteChanged();
            ExportTemplateCommand.RaiseCanExecuteChanged();
            ExportAchievementsCommand.RaiseCanExecuteChanged();
            SaveCommand.RaiseCanExecuteChanged();
            RevertCommand.RaiseCanExecuteChanged();
            ClearCommand.RaiseCanExecuteChanged();
        }

        private void SetStatus(string value, bool isError)
        {
            StatusText = value;
            StatusIsError = isError;
        }

        private static bool IsHttpUrl(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    value.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
        }

        private static string NormalizeText(string value)
        {
            var normalized = (value ?? string.Empty).Trim();
            return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
        }

        private static string L(string key, string fallback)
        {
            var value = ResourceProvider.GetString(key);
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }
    }

    public sealed class CustomAchievementEditItem : ObservableObject
    {
        private string _id;
        private string _displayName;
        private string _description;
        private bool _unlocked;
        private DateTime? _unlockTime;
        private string _unlockedIconPath;
        private string _lockedIconPath;
        private string _pointsText;
        private string _trophyType;
        private bool _hidden;
        private string _rarity;
        private string _globalPercentUnlockedText;
        private string _rarityInput;
        private bool _rarityInputInvalid;
        private string _progressNumText;
        private string _progressDenomText;
        private string _validationMessage;
        private string _baselineSignature;
        private TimeMode _selectedTimeMode;
        private int _selectedHour;
        private int _selectedMinute;
        private string _timeText;
        private bool _isValidTime = true;
        private bool _isUpdatingFromText;
        private bool _isApplyingPickerUpdate;

        private static readonly string[] TimeModeDisplayNames = { "AM", "PM", "24hr" };

        public string OriginalApiName { get; private set; }

        public bool IsNew { get; private set; }

        public string Id
        {
            get => _id;
            set => SetValue(ref _id, value);
        }

        public string DisplayName
        {
            get => _displayName;
            set => SetValue(ref _displayName, value);
        }

        public string Description
        {
            get => _description;
            set => SetValue(ref _description, value);
        }

        public bool Unlocked
        {
            get => _unlocked;
            set
            {
                if (SetValueAndReturn(ref _unlocked, value))
                {
                    if (!value)
                    {
                        UnlockTime = null;
                    }

                    OnPropertyChanged(nameof(CanEditUnlockTime));
                    OnPropertyChanged(nameof(DisplayIcon));
                }
            }
        }

        public DateTime? UnlockTime
        {
            get => _unlockTime;
            set
            {
                if (SetValueAndReturn(ref _unlockTime, value))
                {
                    OnPropertyChanged(nameof(HasUnlockTime));
                    OnPropertyChanged(nameof(CanEditUnlockTime));
                    OnPropertyChanged(nameof(UnlockTimeLocal));
                    OnPropertyChanged(nameof(UnlockDate));
                    OnPropertyChanged(nameof(UnlockTimeOfDay));

                    if (!_isApplyingPickerUpdate)
                    {
                        InitializeTimePickerFromUnlockTime();
                    }

                    if (value.HasValue && !_unlocked)
                    {
                        _unlocked = true;
                        OnPropertyChanged(nameof(Unlocked));
                        OnPropertyChanged(nameof(CanEditUnlockTime));
                        OnPropertyChanged(nameof(DisplayIcon));
                    }
                }
            }
        }

        public string UnlockedIconPath
        {
            get => _unlockedIconPath;
            set
            {
                if (SetValueAndReturn(ref _unlockedIconPath, value))
                {
                    OnPropertyChanged(nameof(UnlockedPreviewPath));
                    OnPropertyChanged(nameof(LockedPreviewPath));
                    OnPropertyChanged(nameof(DisplayIcon));
                }
            }
        }

        public string LockedIconPath
        {
            get => _lockedIconPath;
            set
            {
                if (SetValueAndReturn(ref _lockedIconPath, value))
                {
                    OnPropertyChanged(nameof(LockedPreviewPath));
                    OnPropertyChanged(nameof(DisplayIcon));
                }
            }
        }

        public string PointsText
        {
            get => _pointsText;
            set => SetValue(ref _pointsText, value);
        }

        public string TrophyType
        {
            get => _trophyType;
            set => SetValue(ref _trophyType, value);
        }

        public bool Hidden
        {
            get => _hidden;
            set => SetValue(ref _hidden, value);
        }

        public string Rarity
        {
            get => _rarity;
            set
            {
                if (SetValueAndReturn(ref _rarity, value))
                {
                    OnPropertyChanged(nameof(RarityTier));
                }
            }
        }

        public string GlobalPercentUnlockedText
        {
            get => _globalPercentUnlockedText;
            set => SetValue(ref _globalPercentUnlockedText, value);
        }

        /// <summary>
        /// The single rarity editor's text. A number (optionally ending in %) sets the global
        /// unlock percent and derives the tier from the rarity thresholds; a tier name or its
        /// display text sets the tier and clears the percent. Anything else is flagged invalid.
        /// </summary>
        public string RarityInput
        {
            get => _rarityInput;
            set
            {
                if (SetValueAndReturn(ref _rarityInput, value))
                {
                    ApplyRarityInput(value);
                }
            }
        }

        private void ApplyRarityInput(string value)
        {
            var normalized = NormalizeText(value);
            _rarityInputInvalid = false;
            if (string.IsNullOrWhiteSpace(normalized))
            {
                Rarity = null;
                GlobalPercentUnlockedText = null;
                return;
            }

            var percentText = normalized.TrimEnd('%').Trim();
            if (double.TryParse(percentText, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent) &&
                percent >= 0 &&
                percent <= 100)
            {
                GlobalPercentUnlockedText = percentText;
                Rarity = PercentRarityHelper.GetRarityTier(percent).ToString();
                return;
            }

            var tier = TryMatchRarityTier(normalized);
            if (tier.HasValue)
            {
                Rarity = tier.Value.ToString();
                GlobalPercentUnlockedText = null;
                return;
            }

            _rarityInputInvalid = true;
            Rarity = null;
            GlobalPercentUnlockedText = null;
        }

        private static RarityTier? TryMatchRarityTier(string text)
        {
            if (RarityTierExtensions.TryParse(text, out var parsed))
            {
                return parsed;
            }

            foreach (RarityTier tier in Enum.GetValues(typeof(RarityTier)))
            {
                if (string.Equals(tier.ToDisplayText(), text, StringComparison.OrdinalIgnoreCase))
                {
                    return tier;
                }
            }

            return null;
        }

        private void SyncRarityInputFromState()
        {
            _rarityInputInvalid = false;
            string input;
            if (!string.IsNullOrWhiteSpace(GlobalPercentUnlockedText))
            {
                input = GlobalPercentUnlockedText.TrimEnd('%') + "%";
            }
            else if (RarityTierExtensions.TryParse(Rarity, out var tier))
            {
                input = tier.ToDisplayText();
            }
            else
            {
                input = Rarity;
            }

            SetValue(ref _rarityInput, input, nameof(RarityInput));
        }

        public string ProgressNumText
        {
            get => _progressNumText;
            set => SetValue(ref _progressNumText, value);
        }

        public string ProgressDenomText
        {
            get => _progressDenomText;
            set => SetValue(ref _progressDenomText, value);
        }

        public string ValidationMessage
        {
            get => _validationMessage;
            set
            {
                if (SetValueAndReturn(ref _validationMessage, value))
                {
                    OnPropertyChanged(nameof(HasValidationMessage));
                }
            }
        }

        public bool HasValidationMessage => !string.IsNullOrWhiteSpace(ValidationMessage);

        public string NormalizedId => CustomAchievementProjectionService.NormalizeId(Id);

        public bool HasUnlockTime
        {
            get => UnlockTime.HasValue;
            set
            {
                if (value)
                {
                    if (!Unlocked)
                    {
                        Unlocked = true;
                    }

                    if (!UnlockTime.HasValue)
                    {
                        UnlockTime = DateTime.UtcNow;
                    }
                }
                else
                {
                    UnlockTime = null;
                }
            }
        }

        public bool CanEditUnlockTime => Unlocked && HasUnlockTime;

        public DateTime? UnlockTimeLocal
        {
            get => UnlockTime?.ToLocalTime();
            set
            {
                if (value.HasValue)
                {
                    UnlockTime = value.Value.Kind == DateTimeKind.Unspecified
                        ? DateTime.SpecifyKind(value.Value, DateTimeKind.Local).ToUniversalTime()
                        : value.Value.ToUniversalTime();
                }
                else
                {
                    UnlockTime = null;
                }
            }
        }

        public DateTime? UnlockDate
        {
            get => UnlockTimeLocal?.Date;
            set
            {
                if (value.HasValue)
                {
                    var existingTime = UnlockTimeLocal?.TimeOfDay ?? TimeSpan.FromHours(12);
                    UnlockTimeLocal = value.Value.Date + existingTime;
                }
                else if (!Unlocked)
                {
                    UnlockTime = null;
                }
            }
        }

        public TimeSpan? UnlockTimeOfDay
        {
            get => UnlockTimeLocal?.TimeOfDay;
            set
            {
                if (value.HasValue && UnlockDate.HasValue)
                {
                    UnlockTimeLocal = UnlockDate.Value.Date + value.Value;
                }
            }
        }

        public IEnumerable<string> AvailableTimeModes => TimeModeDisplayNames;

        public string TimeText
        {
            get => _timeText;
            set
            {
                if (_timeText != value)
                {
                    _timeText = value;
                    ValidateAndApplyTimeText();
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsValidTime));
                }
            }
        }

        public bool IsValidTime => _isValidTime;

        public string SelectedTimeModeText
        {
            get => _selectedTimeMode switch
            {
                TimeMode.TwentyFourHour => "24hr",
                TimeMode.PM => "PM",
                _ => "AM"
            };
            set
            {
                var newMode = value switch
                {
                    "24hr" => TimeMode.TwentyFourHour,
                    "PM" => TimeMode.PM,
                    _ => TimeMode.AM
                };

                if (_selectedTimeMode == newMode)
                {
                    return;
                }

                var previousMode = _selectedTimeMode;
                _selectedTimeMode = newMode;
                if (newMode == TimeMode.TwentyFourHour)
                {
                    _selectedHour = Convert12To24Hour(_selectedHour, previousMode);
                }
                else if (previousMode == TimeMode.TwentyFourHour)
                {
                    if (_selectedHour == 0)
                    {
                        _selectedHour = 12;
                    }
                    else if (_selectedHour > 12)
                    {
                        _selectedHour -= 12;
                    }
                }

                SetTimeTextFromSelection();
                OnPropertyChanged(nameof(SelectedTimeModeText));
                UpdateUnlockTimeFromPicker();
            }
        }

        public string DisplayIcon => Unlocked
            ? AchievementIconResolver.GetUnlockedDisplayIcon(UnlockedIconPath)
            : AchievementIconResolver.GetLockedDisplayIcon(UnlockedIconPath, LockedIconPath);

        public string UnlockedPreviewPath => AchievementIconResolver.GetUnlockedDisplayIcon(UnlockedIconPath);

        public string LockedPreviewPath => AchievementIconResolver.GetLockedDisplayIcon(UnlockedIconPath, LockedIconPath);

        public RarityTier RarityTier =>
            RarityTierExtensions.TryParse(Rarity, out var rarity) ? rarity : RarityTier.Common;

        public bool IsBlank =>
            string.IsNullOrWhiteSpace(Id) &&
            string.IsNullOrWhiteSpace(DisplayName) &&
            string.IsNullOrWhiteSpace(Description);

        public bool HasChanges => IsNew || !string.Equals(BuildSignature(), _baselineSignature, StringComparison.Ordinal);

        public string StateSignature => BuildSignature();

        public static CustomAchievementEditItem CreateNew(int index)
        {
            var row = new CustomAchievementEditItem
            {
                DisplayName = "New Achievement " + Math.Max(1, index).ToString(CultureInfo.InvariantCulture),
                Rarity = "Common",
                IsNew = true
            };
            row.SyncRarityInputFromState();
            row.CaptureBaseline();
            return row;
        }

        public static CustomAchievementEditItem FromDefinition(CustomAchievementDefinition definition)
        {
            var row = new CustomAchievementEditItem();
            row.ApplyDefinition(definition, preserveOriginalId: false);
            row.OriginalApiName = CustomAchievementProjectionService.BuildApiName(definition?.Id);
            row.IsNew = false;
            row.CaptureBaseline();
            return row;
        }

        public void ApplyDefinition(CustomAchievementDefinition definition, bool preserveOriginalId)
        {
            if (definition == null)
            {
                return;
            }

            SuppressNotifications = true;
            Id = definition.Id;
            DisplayName = definition.DisplayName;
            Description = definition.Description;
            Unlocked = definition.Unlocked;
            UnlockTime = definition.UnlockTimeUtc;
            UnlockedIconPath = definition.UnlockedIconPath;
            LockedIconPath = definition.LockedIconPath;
            PointsText = FormatInt(definition.Points);
            TrophyType = definition.TrophyType;
            Hidden = definition.Hidden;
            Rarity = definition.Rarity;
            GlobalPercentUnlockedText = FormatDouble(definition.GlobalPercentUnlocked);
            SyncRarityInputFromState();
            ProgressNumText = FormatInt(definition.ProgressNum);
            ProgressDenomText = FormatInt(definition.ProgressDenom);
            ValidationMessage = null;
            SuppressNotifications = false;

            if (!preserveOriginalId)
            {
                OriginalApiName = CustomAchievementProjectionService.BuildApiName(definition.Id);
            }

            OnPropertyChanged(string.Empty);
        }

        public void MarkAsNewImport()
        {
            IsNew = true;
        }

        public CustomAchievementEditItem CloneForDuplicate()
        {
            var definition = ToDefinition(new HashSet<string>(StringComparer.OrdinalIgnoreCase), out _);
            definition.Id = null;
            definition.DisplayName = string.IsNullOrWhiteSpace(definition.DisplayName)
                ? "Copy"
                : definition.DisplayName + " Copy";
            var row = FromDefinition(definition);
            row.OriginalApiName = null;
            row.IsNew = true;
            row.Id = null;
            row.CaptureBaseline();
            return row;
        }

        public CustomAchievementDefinition ToDefinition(ISet<string> usedIds, out List<string> errors)
        {
            errors = new List<string>();
            usedIds ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var displayName = NormalizeText(DisplayName);
            if (string.IsNullOrWhiteSpace(displayName))
            {
                errors.Add("Title is required.");
                return null;
            }

            var id = CustomAchievementProjectionService.NormalizeId(Id);
            if (string.IsNullOrWhiteSpace(id))
            {
                id = CustomAchievementProjectionService.GenerateId(displayName, usedIds);
            }

            if (!usedIds.Add(id))
            {
                errors.Add($"Duplicate ID '{id}'.");
            }

            var definition = new CustomAchievementDefinition
            {
                Id = id,
                DisplayName = displayName,
                Description = NormalizeText(Description),
                Unlocked = Unlocked,
                UnlockedIconPath = NormalizeText(UnlockedIconPath),
                LockedIconPath = NormalizeText(LockedIconPath),
                TrophyType = NormalizeText(TrophyType),
                Hidden = Hidden,
                Rarity = string.IsNullOrWhiteSpace(Rarity) ? "Common" : Rarity.Trim()
            };

            if (CanEditUnlockTime && !IsValidTime)
            {
                errors.Add("Unlock time is invalid.");
            }

            definition.UnlockTimeUtc = definition.Unlocked
                ? NormalizeUtc(UnlockTime)
                : null;
            if (definition.UnlockTimeUtc.HasValue)
            {
                definition.Unlocked = true;
            }

            definition.Points = ParseNullableInt(PointsText, errors, "Points", allowZero: true);
            definition.GlobalPercentUnlocked = ParseNullablePercent(GlobalPercentUnlockedText, errors);
            definition.ProgressNum = ParseNullableInt(ProgressNumText, errors, "Progress", allowZero: true);
            definition.ProgressDenom = ParseNullableInt(ProgressDenomText, errors, "Progress total", allowZero: false);

            if (!string.IsNullOrWhiteSpace(TrophyType) && NormalizeTrophyType(TrophyType) == null)
            {
                errors.Add("Trophy type must be bronze, silver, gold, or platinum.");
            }
            else
            {
                definition.TrophyType = NormalizeTrophyType(TrophyType);
            }

            if (_rarityInputInvalid ||
                (!string.IsNullOrWhiteSpace(Rarity) && !RarityTierExtensions.TryParse(Rarity, out _)))
            {
                errors.Add("Rarity must be a percent from 0 to 100, or Common, Uncommon, Rare, or Ultra Rare.");
            }

            if (definition.ProgressNum.HasValue &&
                definition.ProgressDenom.HasValue &&
                definition.ProgressNum.Value > definition.ProgressDenom.Value)
            {
                errors.Add("Progress cannot be greater than progress total.");
            }

            return definition;
        }

        public void CaptureBaseline()
        {
            _baselineSignature = BuildSignature();
            IsNew = false;
            OnPropertyChanged(nameof(HasChanges));
        }

        private string BuildSignature()
        {
            return JsonConvert.SerializeObject(new
            {
                Id,
                DisplayName,
                Description,
                Unlocked,
                UnlockTimeUtc = FormatDate(UnlockTime),
                UnlockedIconPath,
                LockedIconPath,
                PointsText,
                TrophyType,
                Hidden,
                Rarity,
                GlobalPercentUnlockedText,
                RarityInput,
                ProgressNumText,
                ProgressDenomText
            });
        }

        private void ValidateAndApplyTimeText()
        {
            if (_isUpdatingFromText)
            {
                return;
            }

            if (!TryParseTimeText(_timeText, _selectedTimeMode, out var parsedHour, out var parsedMinute))
            {
                _isValidTime = false;
                return;
            }

            _selectedHour = parsedHour;
            _selectedMinute = parsedMinute;
            _isValidTime = true;
            UpdateUnlockTimeFromPicker();
        }

        private static bool TryParseTimeText(string input, TimeMode mode, out int hour, out int minute)
        {
            hour = 0;
            minute = 0;
            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            var parts = input.Trim().Split(':');
            if (parts.Length != 2 ||
                !int.TryParse(parts[0].Trim(), out hour) ||
                !int.TryParse(parts[1].Trim(), out minute))
            {
                return false;
            }

            if (minute < 0 || minute > 59)
            {
                return false;
            }

            var minHour = mode == TimeMode.TwentyFourHour ? 0 : 1;
            var maxHour = mode == TimeMode.TwentyFourHour ? 23 : 12;
            return hour >= minHour && hour <= maxHour;
        }

        private static string FormatTimeText(int hour, int minute, TimeMode mode)
        {
            return mode == TimeMode.TwentyFourHour
                ? $"{hour:D2}:{minute:D2}"
                : $"{hour}:{minute:D2}";
        }

        private void SetTimeTextFromSelection()
        {
            _isUpdatingFromText = true;
            try
            {
                _timeText = FormatTimeText(_selectedHour, _selectedMinute, _selectedTimeMode);
                _isValidTime = true;
                OnPropertyChanged(nameof(TimeText));
                OnPropertyChanged(nameof(IsValidTime));
            }
            finally
            {
                _isUpdatingFromText = false;
            }
        }

        private void UpdateUnlockTimeFromPicker()
        {
            if (!UnlockDate.HasValue || !_isValidTime)
            {
                return;
            }

            var hour24 = _selectedTimeMode == TimeMode.TwentyFourHour
                ? _selectedHour
                : Convert12To24Hour(_selectedHour, _selectedTimeMode);

            _isApplyingPickerUpdate = true;
            try
            {
                UnlockTimeLocal = UnlockDate.Value.Date + new TimeSpan(hour24, _selectedMinute, 0);
            }
            finally
            {
                _isApplyingPickerUpdate = false;
            }
        }

        private static int Convert12To24Hour(int hour12, TimeMode mode)
        {
            if (mode == TimeMode.TwentyFourHour)
            {
                return hour12;
            }

            if (mode == TimeMode.AM)
            {
                return hour12 == 12 ? 0 : hour12;
            }

            return hour12 == 12 ? 12 : hour12 + 12;
        }

        private static void Convert24To12Hour(int hour24, out int hour12, out TimeMode mode)
        {
            if (hour24 == 0)
            {
                hour12 = 12;
                mode = TimeMode.AM;
            }
            else if (hour24 < 12)
            {
                hour12 = hour24;
                mode = TimeMode.AM;
            }
            else if (hour24 == 12)
            {
                hour12 = 12;
                mode = TimeMode.PM;
            }
            else
            {
                hour12 = hour24 - 12;
                mode = TimeMode.PM;
            }
        }

        private void InitializeTimePickerFromUnlockTime()
        {
            if (UnlockTimeLocal.HasValue)
            {
                var time = UnlockTimeLocal.Value.TimeOfDay;
                Convert24To12Hour(time.Hours, out _selectedHour, out _selectedTimeMode);
                _selectedMinute = time.Minutes;
            }
            else
            {
                _selectedHour = 12;
                _selectedMinute = 0;
                _selectedTimeMode = TimeMode.PM;
            }

            SetTimeTextFromSelection();
            OnPropertyChanged(nameof(SelectedTimeModeText));
        }

        private static DateTime? NormalizeUtc(DateTime? value)
        {
            if (!value.HasValue || value.Value == DateTime.MinValue)
            {
                return null;
            }

            var date = value.Value;
            return date.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(date, DateTimeKind.Utc)
                : date.ToUniversalTime();
        }

        private static int? ParseNullableInt(
            string value,
            ICollection<string> errors,
            string label,
            bool allowZero)
        {
            var normalized = NormalizeText(value);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return null;
            }

            if (int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) &&
                parsed >= (allowZero ? 0 : 1))
            {
                return parsed;
            }

            errors.Add(label + (allowZero
                ? " must be a non-negative integer."
                : " must be a positive integer."));
            return null;
        }

        private static double? ParseNullablePercent(string value, ICollection<string> errors)
        {
            var normalized = NormalizeText(value)?.TrimEnd('%');
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return null;
            }

            if (double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) &&
                parsed >= 0 &&
                parsed <= 100)
            {
                return parsed;
            }

            errors.Add("Percent must be between 0 and 100.");
            return null;
        }

        private static string NormalizeTrophyType(string value)
        {
            var normalized = NormalizeText(value);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return null;
            }

            switch (normalized.ToLowerInvariant())
            {
                case "bronze":
                case "silver":
                case "gold":
                case "platinum":
                    return normalized.ToLowerInvariant();
                default:
                    return null;
            }
        }

        private static string FormatDate(DateTime? value)
        {
            return value.HasValue ? value.Value.ToUniversalTime().ToString("u", CultureInfo.InvariantCulture) : null;
        }

        private static string FormatInt(int? value)
        {
            return value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : null;
        }

        private static string FormatDouble(double? value)
        {
            return value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : null;
        }

        private static string NormalizeText(string value)
        {
            var normalized = (value ?? string.Empty).Trim();
            return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
        }
    }

    public sealed class CustomAchievementSelectionOption
    {
        public CustomAchievementSelectionOption(string value, string displayName)
        {
            Value = value;
            DisplayName = displayName;
        }

        public string Value { get; }

        public string DisplayName { get; }
    }
}
