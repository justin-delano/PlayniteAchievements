<h1 align="center">Playnite Achievements</h1>

<p align="center">
<img src="Images/badge-bronze.svg" width="32" height="32" alt="Bronze">
<img src="Images/badge-silver.svg" width="32" height="32" alt="Silver">
<img src="Images/badge-gold.svg" width="32" height="32" alt="Gold">
<img src="Images/badge-platinum.svg" width="32" height="32" alt="Platinum">
<img src="Images/badge-perfect.svg" width="32" height="32" alt="Perfect">
</p>

<div align="center">

[![Release](https://img.shields.io/github/v/release/justin-delano/PlayniteAchievements?logo=github)](https://github.com/justin-delano/PlayniteAchievements/releases/latest)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![Playnite SDK](https://img.shields.io/badge/Playnite%20SDK-6.14.0-blue)](https://playnite.link/)
[![Downloads](https://img.shields.io/github/downloads/justin-delano/PlayniteAchievements/total)](https://github.com/justin-delano/PlayniteAchievements/releases)

</div>

<p align="center">
A modern Playnite extension plugin for aggregating achievement data from multiple gaming platforms. Bring your gaming accomplishments together in one place with beautiful visualizations and seamless theme integration.
</p>

## Why PlayniteAchievements?

| Traditional Approaches                | PlayniteAchievements                                     |
| ------------------------------------- | -------------------------------------------------------- |
| Limited platform support              | Modular architecture planning to support 10+ platforms   |
| Basic visualization tools             | Rich charts with rarity breakdowns and progress tracking |
| Single theme compatibility            | Dual integration: legacy and modern native controls      |
| Fullscreen helper required            | Complete fullscreen integration included.                |
| Manual profile configuration required | Compatible with existing Steam privacy settings          |

## Screenshots

### Sidebar View

<img src="Images/sidebar-view1.png" alt="Sidebar Overview" width="600">
<img src="Images/sidebar-view2.png" alt="Sidebar Single Game View" width="600">

### Single Game View

<img src="Images/single-game-view.png" alt="Single Game View" width="600">

### Setup Page

<img src="Images/setup-view.png" alt="Setup Page" width="600">

### Theme Integration

<img src="Images/aniki-remake1.png" alt="Aniki-Remake Integration" width="600">

## Features

### Multi-Platform Support

Track achievements across all your gaming platforms in one place.

**Currently Available**:

- Steam
- RetroAchievements

**Coming Soon**:

- GOG Galaxy, Epic Games Store, EA App
- Xbox (PC and Xbox 360 via Xenia)
- PlayStation Network (via RPCS3, ShadPS4 emulators)
- Battle.net, Exophase, GameJolt

### Beautiful Visualizations

See your progress at a glance with charts and statistics:

- Achievement progression timelines
- Rarity breakdowns (Common, Rare, UltraRare)
- Multiple view modes: compact lists, detailed grids, progress bars
- Custom icons for hidden achievements

### Seamless Theme Integration

Compatible with existing themes out of the box.

**Legacy Compatibility**: Drop-in replacement for SuccessStory themes. All familiar controls function without modification.

**Native Controls**: Modern themes utilize native PlayniteAchievements controls for improved performance and integration.

#### [Aniki-ReMake](https://github.com/Mike-Aniki/Aniki-ReMake/tree/main)

<img src="Images/aniki-remake2.png" alt="Aniki-Remake Integration" width="600">
<img src="Images/aniki-remake3.png" alt="Aniki-Remake Integration" width="600">


Additional theme support planned. See the [Theme Integration Wiki](https://github.com/justindelano/PlayniteAchievements/wiki) for details.

### Fast & Reliable

- Intelligent caching maintains responsive performance
- Background updates operate without interrupting library browsing
- Graceful error handling continues scanning when individual games fail
- Configurable scan intervals for automatic updates

### Flexible Scanning

Choose how and when achievements are fetched:

- **Quick Scan**: Scans only recently played games for fast updates
- **Full Scan**: Refreshes achievement data for your entire library
- **Installed Games**: Limits scanning to games currently installed
- **Favorites**: Scans only your favorited titles
- **Selected**: Manually choose specific games to scan
- **Single Game**: View detailed achievement data for one game
- **Auto-Scan**: Automatically scans when new games are added to your library, or you finish a play session.

## Roadmap

**In Development**:

- GOG Galaxy provider
- Epic Games Store integration
- Additional native controls
- GamerScore compatibility

**Planned**:

- EA App, Xbox, PlayStation platforms
- Integration with [FriendsAchievementFeed](https://github.com/justin-delano/playnite-friendsachievementfeed-plugin)

Additional platforms and features are released as development completes.

## Installation

1. Download the latest `.pext` file from [Releases](https://github.com/justindelano/PlayniteAchievements/releases/latest)
2. Install via Playnite's addon browser or drag-drop into Playnite
3. Configure your platform credentials in Settings → Extensions → PlayniteAchievements

**Requirements**: Playnite 10+

## For Theme Developers

PlayniteAchievements provides comprehensive integration options for theme developers:

### SuccessStory Legacy Compatibility

Existing themes using SuccessStory controls work without modification. All familiar properties are supported:

| SuccessStory Property                                         | PlayniteAchievements Property                                         |
| ------------------------------------------------------------- | --------------------------------------------------------------------- |
| `{PluginSettings Plugin=SuccessStory, Path=HasData}`        | `{PluginSettings Plugin=PlayniteAchievements, Path=HasData}`        |
| `<ContentControl x:Name="SuccessStory_PluginCompactList"/>` | `<ContentControl x:Name="PlayniteAchievements_PluginCompactList"/>` |

Switching to Playnite Achievements is as easy as finding and replacing "SuccessStory" with "PlayniteAchievements" in your files.

### Native Controls (In Development)

Modern themes can use native PlayniteAchievements controls for better performance and additional features:

```xml
<Controls:AchievementList
    Achievements="{Binding GameAchievements}"
    HorizontalAlignment="Stretch"
    VerticalAlignment="Stretch" />
```

### Documentation

For detailed integration guides, property references, and code examples, see the [Theme Integration Wiki](https://github.com/justindelano/PlayniteAchievements/wiki).

## Support & Contributing

- **Issues**: [GitHub Issue Tracker](https://github.com/justindelano/PlayniteAchievements/issues)
- **Discussions**: [GitHub Discussions](https://github.com/justindelano/PlayniteAchievements/discussions)
- **Translations**: Contributions welcome - submit a pull request with updated localization files

## License

MIT License - see [LICENSE](LICENSE) for details.

## Credits

Many thanks to everyone on the Playnite Discord for their expertise, feedback, and beta testing!
