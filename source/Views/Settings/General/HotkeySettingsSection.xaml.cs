using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Playnite.SDK;
using PlayniteAchievements.Models;
using PlayniteAchievements.Models.Settings;

namespace PlayniteAchievements.Views.Settings.General
{
    /// <summary>
    /// General settings: Hotkeys section. Hosts the hotkey enable toggles and the capture
    /// buttons that write captured gestures directly into the persisted settings.
    /// </summary>
    public partial class HotkeySettingsSection : UserControl, IDisposable
    {
        public static readonly DependencyProperty ViewAchievementsHotkeyButtonTextProperty =
            DependencyProperty.Register(
                nameof(ViewAchievementsHotkeyButtonText),
                typeof(string),
                typeof(HotkeySettingsSection),
                new PropertyMetadata(string.Empty));

        public string ViewAchievementsHotkeyButtonText
        {
            get => (string)GetValue(ViewAchievementsHotkeyButtonTextProperty);
            set => SetValue(ViewAchievementsHotkeyButtonTextProperty, value);
        }

        public static readonly DependencyProperty ManageAchievementsHotkeyButtonTextProperty =
            DependencyProperty.Register(
                nameof(ManageAchievementsHotkeyButtonText),
                typeof(string),
                typeof(HotkeySettingsSection),
                new PropertyMetadata(string.Empty));

        public string ManageAchievementsHotkeyButtonText
        {
            get => (string)GetValue(ManageAchievementsHotkeyButtonTextProperty);
            set => SetValue(ManageAchievementsHotkeyButtonTextProperty, value);
        }

        public static readonly DependencyProperty OverviewHotkeyButtonTextProperty =
            DependencyProperty.Register(
                nameof(OverviewHotkeyButtonText),
                typeof(string),
                typeof(HotkeySettingsSection),
                new PropertyMetadata(string.Empty));

        public string OverviewHotkeyButtonText
        {
            get => (string)GetValue(OverviewHotkeyButtonTextProperty);
            set => SetValue(OverviewHotkeyButtonTextProperty, value);
        }

        public static readonly DependencyProperty HotkeyCaptureStatusTextProperty =
            DependencyProperty.Register(
                nameof(HotkeyCaptureStatusText),
                typeof(string),
                typeof(HotkeySettingsSection),
                new PropertyMetadata(string.Empty));

        public string HotkeyCaptureStatusText
        {
            get => (string)GetValue(HotkeyCaptureStatusTextProperty);
            set => SetValue(HotkeyCaptureStatusTextProperty, value);
        }

        private enum HotkeyCaptureTarget
        {
            ViewAchievements,
            ManageAchievements,
            Overview
        }

        private readonly PlayniteAchievementsSettings _settings;
        private readonly PersistedSettingsSubscription _persistedSubscription;
        private HotkeyCaptureTarget? _capturingHotkey;

        public HotkeySettingsSection()
        {
            InitializeComponent();
        }

        internal HotkeySettingsSection(PlayniteAchievementsSettings settings)
            : this()
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));

            _persistedSubscription = new PersistedSettingsSubscription(
                _settings,
                OnPersistedPropertyChanged,
                UpdateHotkeyButtonTexts);

            UpdateHotkeyButtonTexts();
        }

        private void OnPersistedPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PersistedSettings.ViewAchievementsHotkey) ||
                e.PropertyName == nameof(PersistedSettings.ManageAchievementsHotkey) ||
                e.PropertyName == nameof(PersistedSettings.OverviewHotkey))
            {
                UpdateHotkeyButtonTexts();
            }
        }

        private void HotkeyCapture_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button &&
                button.CommandParameter is string targetName &&
                Enum.TryParse(targetName, out HotkeyCaptureTarget target))
            {
                StartHotkeyCapture(target, button);
            }
        }

        private void ResetHotkey_Click(object sender, RoutedEventArgs e)
        {
            var persisted = _settings?.Persisted;
            if (persisted == null)
            {
                return;
            }

            if (!(sender is Button { CommandParameter: string targetName }) ||
                !Enum.TryParse(targetName, out HotkeyCaptureTarget target))
            {
                return;
            }

            switch (target)
            {
                case HotkeyCaptureTarget.ViewAchievements:
                    persisted.ViewAchievementsHotkey = PersistedSettings.DefaultViewAchievementsHotkey;
                    break;
                case HotkeyCaptureTarget.ManageAchievements:
                    persisted.ManageAchievementsHotkey = PersistedSettings.DefaultManageAchievementsHotkey;
                    break;
                case HotkeyCaptureTarget.Overview:
                    persisted.OverviewHotkey = PersistedSettings.DefaultOverviewHotkey;
                    break;
                default:
                    return;
            }

            EndHotkeyCapture();
        }

        private void HotkeyCapture_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!_capturingHotkey.HasValue)
            {
                return;
            }

            var target = _capturingHotkey.Value;
            if ((target == HotkeyCaptureTarget.ViewAchievements &&
                 !ReferenceEquals(sender, ViewAchievementsHotkeyCaptureButton)) ||
                (target == HotkeyCaptureTarget.ManageAchievements &&
                 !ReferenceEquals(sender, ManageAchievementsHotkeyCaptureButton)) ||
                (target == HotkeyCaptureTarget.Overview &&
                 !ReferenceEquals(sender, OverviewHotkeyCaptureButton)))
            {
                return;
            }

            e.Handled = true;
            var key = GetEffectiveHotkeyCaptureKey(e);
            if (key == Key.Escape)
            {
                EndHotkeyCapture();
                return;
            }

            if (key == Key.Back || key == Key.Delete)
            {
                SetCapturedHotkey(target, string.Empty);
                EndHotkeyCapture();
                return;
            }

            if (!AchievementHotkeyGesture.TryCreate(key, Keyboard.Modifiers, out var gesture))
            {
                HotkeyCaptureStatusText = L(
                    "LOCPlayAch_Hotkeys_InvalidShortcut",
                    "Unsupported shortcut. Press a letter, digit, function key, or a modified shortcut.");
                return;
            }

            if (IsDuplicateHotkey(target, gesture))
            {
                HotkeyCaptureStatusText = L(
                    "LOCPlayAch_Hotkeys_DuplicateShortcut",
                    "That shortcut is already assigned.");
                return;
            }

            SetCapturedHotkey(target, gesture.ToString());
            EndHotkeyCapture();
        }

        private void StartHotkeyCapture(HotkeyCaptureTarget target, Button button)
        {
            _capturingHotkey = target;
            HotkeyCaptureStatusText = L("LOCPlayAch_Hotkeys_CapturePrompt", "Press a shortcut...");

            if (target == HotkeyCaptureTarget.ViewAchievements)
            {
                ViewAchievementsHotkeyButtonText = L("LOCPlayAch_Hotkeys_CaptureButton", "Press keys...");
                ManageAchievementsHotkeyButtonText = FormatHotkeyButtonText(
                    _settings?.Persisted?.ManageAchievementsHotkey);
                OverviewHotkeyButtonText = FormatHotkeyButtonText(
                    _settings?.Persisted?.OverviewHotkey);
            }
            else if (target == HotkeyCaptureTarget.ManageAchievements)
            {
                ManageAchievementsHotkeyButtonText = L("LOCPlayAch_Hotkeys_CaptureButton", "Press keys...");
                ViewAchievementsHotkeyButtonText = FormatHotkeyButtonText(
                    _settings?.Persisted?.ViewAchievementsHotkey);
                OverviewHotkeyButtonText = FormatHotkeyButtonText(
                    _settings?.Persisted?.OverviewHotkey);
            }
            else
            {
                OverviewHotkeyButtonText = L("LOCPlayAch_Hotkeys_CaptureButton", "Press keys...");
                ViewAchievementsHotkeyButtonText = FormatHotkeyButtonText(
                    _settings?.Persisted?.ViewAchievementsHotkey);
                ManageAchievementsHotkeyButtonText = FormatHotkeyButtonText(
                    _settings?.Persisted?.ManageAchievementsHotkey);
            }

            button?.Focus();
            Keyboard.Focus(button);
        }

        private void EndHotkeyCapture()
        {
            _capturingHotkey = null;
            HotkeyCaptureStatusText = string.Empty;
            UpdateHotkeyButtonTexts();
        }

        private void SetCapturedHotkey(HotkeyCaptureTarget target, string hotkey)
        {
            var persisted = _settings?.Persisted;
            if (persisted == null)
            {
                return;
            }

            if (target == HotkeyCaptureTarget.ViewAchievements)
            {
                persisted.ViewAchievementsHotkey = hotkey;
            }
            else if (target == HotkeyCaptureTarget.ManageAchievements)
            {
                persisted.ManageAchievementsHotkey = hotkey;
            }
            else
            {
                persisted.OverviewHotkey = hotkey;
            }
        }

        private bool IsDuplicateHotkey(HotkeyCaptureTarget target, AchievementHotkeyGesture gesture)
        {
            if (gesture == null || gesture.IsEmpty)
            {
                return false;
            }

            var persisted = _settings?.Persisted;
            if (persisted == null)
            {
                return false;
            }

            return IsMatchingHotkey(target, HotkeyCaptureTarget.ViewAchievements, persisted.ViewAchievementsHotkey, gesture) ||
                   IsMatchingHotkey(target, HotkeyCaptureTarget.ManageAchievements, persisted.ManageAchievementsHotkey, gesture) ||
                   IsMatchingHotkey(target, HotkeyCaptureTarget.Overview, persisted.OverviewHotkey, gesture);
        }

        private static bool IsMatchingHotkey(
            HotkeyCaptureTarget currentTarget,
            HotkeyCaptureTarget comparedTarget,
            string comparedText,
            AchievementHotkeyGesture gesture)
        {
            if (currentTarget == comparedTarget)
            {
                return false;
            }

            return AchievementHotkeyGesture.TryParse(comparedText, out var otherGesture) &&
                   otherGesture != null &&
                   !otherGesture.IsEmpty &&
                   gesture.Equals(otherGesture);
        }

        private void UpdateHotkeyButtonTexts()
        {
            if (_capturingHotkey.HasValue)
            {
                return;
            }

            var persisted = _settings?.Persisted;
            ViewAchievementsHotkeyButtonText = FormatHotkeyButtonText(persisted?.ViewAchievementsHotkey);
            ManageAchievementsHotkeyButtonText = FormatHotkeyButtonText(persisted?.ManageAchievementsHotkey);
            OverviewHotkeyButtonText = FormatHotkeyButtonText(persisted?.OverviewHotkey);
        }

        private string FormatHotkeyButtonText(string hotkey)
        {
            return string.IsNullOrWhiteSpace(hotkey)
                ? L("LOCPlayAch_Common_None", "None")
                : hotkey;
        }

        private static Key GetEffectiveHotkeyCaptureKey(KeyEventArgs e)
        {
            if (e == null)
            {
                return Key.None;
            }

            if (e.Key == Key.System)
            {
                return e.SystemKey;
            }

            if (e.Key == Key.ImeProcessed)
            {
                return e.ImeProcessedKey;
            }

            return e.Key;
        }

        public void Dispose()
        {
            _persistedSubscription?.Dispose();
        }

        private static string L(string key, string fallback)
        {
            var value = ResourceProvider.GetString(key);
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }
    }
}
