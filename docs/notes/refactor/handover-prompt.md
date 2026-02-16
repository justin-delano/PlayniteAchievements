# Handover Prompt: Game Context Refactor

You are implementing a refactor to fix fullscreen theme integration stale data issues
in the PlayniteAchievements plugin. The current approach uses a "restore" callback
pattern which is architecturally inelegant. You will implement the SuccessStory pattern
where each window gets its own isolated DataContext.

## Context

**Project**: Playnite extension plugin for aggregating achievement data from multiple
gaming platforms. Built with C# and WPF, targeting .NET Framework 4.6.2.

**Working Directory**: `C:\Users\Justin\Desktop\PlayniteAchievements`

**Reference Implementation**: `C:\Users\Justin\Desktop\PlayniteAchievementsReference\playnite-successstory-plugin\`

## Problem

When clicking a game from the achievement overview window in fullscreen mode:
1. The popup window shows that game's achievements
2. But the underlying game detail view also changes to show that game's data
3. When closing the popup, the underlying view has wrong data

**Current fix (inelegant)**: Mutate global state, then restore it on window close.

**Target solution (SuccessStory pattern)**: Each window gets its own isolated DataContext.

## Your Task

Implement the refactor as described in `docs/notes/refactor/game-context-refactor-plan.md`.

### Key Steps

1. **Create `GameAchievementContext` class** at `source/Models/ThemeIntegration/GameAchievementContext.cs`
   - Should contain per-game achievement properties (same shape as ThemeData's per-game section)
   - Constructor takes Game, IPlayniteAPI, and SingleGameSnapshot
   - Properties: GameId, GameName, CoverImagePath, HasAchievements, AchievementCount,
     UnlockedCount, LockedCount, ProgressPercentage, AllAchievements, etc.

2. **Add factory method to ThemeIntegrationService**
   - `public GameAchievementContext CreateGameContext(Guid gameId)`
   - Fetches game from database, gets achievement data, builds snapshot, creates context

3. **Modify FullscreenWindowService.OpenGameWindow**
   - Call factory to create isolated context for the game
   - Pass this context as DataContext instead of `_settings`
   - Remove `_restoreSelectedGameThemeData` field and constructor parameter

4. **Remove restore logic**
   - From FullscreenWindowService: Remove restore callback
   - From ThemeIntegrationService: Remove `RestoreSelectedGameThemeData` method
   - From PlayniteAchievementsPlugin: Remove restore callback wiring

5. **Keep overview window using _settings**
   - The overview window (AchievementsWindow style) should continue using `_settings`
   - Only game-specific windows (GameAchievementsWindow style) use isolated context

### Important Constraints

- Do NOT trigger achievement scans in the synchronous data population
- Build command: `"C:/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe" "c:/Users/Justin/Desktop/PlayniteAchievements/source/PlayniteAchievements.csproj" -t:Clean,Build -p:Configuration=Debug`
- Themes like Aniki ReMake bind via `{PluginSettings Plugin=PlayniteAchievements, Path=HasAchievements}`
- The popup window binds directly to DataContext properties (e.g., `{Binding HasAchievements}`)

### Reference Files to Study

1. `source/Models/ThemeIntegration/ThemeData.cs` - Current per-game properties shape
2. `source/Models/ThemeIntegration/SingleGameSnapshot.cs` - Immutable snapshot structure
3. `source/Services/ThemeIntegration/FullscreenWindowService.cs` - Window creation
4. `source/Services/ThemeIntegration/ThemeIntegrationService.cs` - Data population
5. SuccessStory reference: `SuccessStoryOneGameView.xaml.cs` for the constructor pattern

### Success Criteria

1. Build succeeds
2. Opening a game window from the overview does NOT affect the underlying view
3. Closing the game window leaves the underlying view unchanged
4. Aniki ReMake theme bindings work correctly
5. No "restore" logic needed

### After Completion

Create atomic commits for each logical change. Use conventional commit format:
- `feat(theme): add GameAchievementContext for isolated window data`
- `refactor(fullscreen): use isolated context for game windows`
- `refactor(fullscreen): remove restore callback pattern`

Return with questions if any ambiguity exists rather than interpreting incorrectly.
