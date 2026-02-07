# PlayniteAchievements Plugin

Project: Playnite extension plugin for aggregating achievement data from multiple gaming platforms.

## Development Context

This is a Playnite plugin built with C# and WPF, targeting .NET Framework 4.6.2.

### Playnite SDK

- Version: 6.14.0
- Package: PlayniteSDK via NuGet (packages/PlayniteSDK.6.14.0/)
- Base Class: Playnite.SDK.Plugins.GenericPlugin
- Entry Point: PlayniteAchievementsPlugin.cs

### Reference Plugins

Best practices and patterns are drawn from reference implementations at:
C:\Users\Justin\Desktop\PlayniteAchievementsReference

Contains:
- FusionX-PlayniteAchievements-Native (primary reference for similar functionality)
- playnite-successstory-plugin (theme integration patterns)
- playnite-plugincommon (shared utilities)
- Various compatibility and theme integration examples

### Build and Package Commands

Clean only:
& "C:/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe" `
  "c:/Users/Justin/Desktop/PlayniteAchievements/source/PlayniteAchievements.csproj" `
  -t:Clean -p:Configuration=Debug

Build only (Debug):
& "C:/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe" `
  "c:/Users/Justin/Desktop/PlayniteAchievements/source/PlayniteAchievements.csproj" `
  -t:Build -p:Configuration=Debug

Clean and build (Debug):
& "C:/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe" `
  "c:/Users/Justin/Desktop/PlayniteAchievements/source/PlayniteAchievements.csproj" `
  -t:Clean,Build -p:Configuration=Debug

Package plugin (creates .pext):
& "C:\Users\Justin\AppData\Local\Playnite\Toolbox.exe" pack `
  C:\Users\Justin\Desktop\PlayniteAchievements\source\bin\Debug `
  C:\Users\Justin\Desktop\PlayniteAchievements\out

## Architecture

### Project Structure

source/
├── Common/              # Shared utilities (ObservableObject, Commands, Collections)
├── Models/              # Data models and DTOs
├── Providers/           # Platform-specific data providers (IDataProvider interface)
├── Services/            # Core services (AchievementManager, Cache, Images)
├── ViewModels/          # MVVM view models
├── Views/               # XAML views and controls
│   └── ThemeIntegration/  # SuccessStory and Native theme controls
├── Localization/        # Resource strings (en_US, fr_FR, etc.)
├── Resources/           # Embedded resources
├── PlayniteAchievementsPlugin.cs  # Plugin entry point
└── PlayniteAchievements.csproj    # Project file

### Key Patterns

Provider Pattern:
- IDataProvider interface defines ScanAsync contract
- Each platform (Steam, RetroAchievements, etc.) implements a provider
- Providers are discovered and registered at runtime

Theme Integration:
- Dual theme support: SuccessStory (legacy) and Native
- Custom elements registered via AddCustomElementSupport
- Control factories dictionary pattern for creating theme controls

MVVM Implementation:
- ObservableObject base with INotifyPropertyChanged
- RelayCommand and AsyncCommand for command binding
- BulkObservableCollection for performance with large datasets

Caching Strategy:
- Memory and disk image caching
- Background update mechanism
- Cache invalidation policies

## Plugin Development Guidelines

When adding new features or providers:

1. New Provider: Implement IDataProvider, follow existing Steam/RetroAchievements patterns
2. Theme Controls: Register in AddCustomElementSupport, create factory method
3. Settings: Add to SettingsViewModel, use PlayniteApi for persistence
4. Logging: Use PlayniteApi.Log for all logging
5. Localization: Add strings to all Localization/*.xaml files

## Dependencies

Key NuGet packages:
- PlayniteSDK 6.14.0
- Newtonsoft.Json 10.0.3
- HtmlAgilityPack 1.11.62
- LiveCharts.Wpf 0.9.7
- SharpCompress 0.32.0
- DiscUtils packages (for ROM handling)

## Testing

Load the built extension into Playnite Developer Mode to test functionality.
