using Playnite.SDK;
using SqlNado;

namespace PlayniteAchievements.Services.Database
{
    internal sealed class SqlNadoSchemaManager
    {
        public const int SchemaVersion = 1;
        private readonly ILogger _logger;

        public SqlNadoSchemaManager(ILogger logger)
        {
            _logger = logger;
        }

        public void EnsureSchema(SQLiteDatabase db)
        {
            db.ExecuteNonQuery("PRAGMA journal_mode = WAL;");
            db.ExecuteNonQuery("PRAGMA synchronous = NORMAL;");
            db.ExecuteNonQuery("PRAGMA foreign_keys = ON;");
            db.ExecuteNonQuery("PRAGMA temp_store = MEMORY;");

            ExecuteSafe(db, @"CREATE TABLE IF NOT EXISTS CacheMetadata (
                Key TEXT PRIMARY KEY NOT NULL,
                Value TEXT NOT NULL
            );");

            ExecuteSafe(db, @"CREATE TABLE IF NOT EXISTS Users (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ProviderName TEXT NOT NULL COLLATE NOCASE,
                ExternalUserId TEXT NOT NULL COLLATE NOCASE,
                DisplayName TEXT NULL,
                IsCurrentUser INTEGER NOT NULL DEFAULT 0,
                FriendSource TEXT NULL,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL,
                UNIQUE (ProviderName, ExternalUserId)
            );");

            ExecuteSafe(db, @"CREATE UNIQUE INDEX IF NOT EXISTS UX_Users_CurrentPerProvider
                ON Users (ProviderName)
                WHERE IsCurrentUser = 1;");

            ExecuteSafe(db, @"CREATE INDEX IF NOT EXISTS IX_Users_CurrentUser_Id
                ON Users (IsCurrentUser, Id);");

            ExecuteSafe(db, @"CREATE TABLE IF NOT EXISTS Games (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ProviderName TEXT NOT NULL COLLATE NOCASE,
                ProviderGameId INTEGER NULL,
                PlayniteGameId TEXT NULL,
                GameName TEXT NULL,
                LibrarySourceName TEXT NULL,
                FirstSeenUtc TEXT NOT NULL,
                LastUpdatedUtc TEXT NOT NULL
            );");

            ExecuteSafe(db, @"CREATE UNIQUE INDEX IF NOT EXISTS UX_Games_Provider_Playnite
                ON Games (ProviderName, PlayniteGameId)
                WHERE PlayniteGameId IS NOT NULL;");

            ExecuteSafe(db, @"CREATE UNIQUE INDEX IF NOT EXISTS UX_Games_Provider_GameId
                ON Games (ProviderName, ProviderGameId)
                WHERE ProviderGameId IS NOT NULL AND ProviderGameId > 0;");

            ExecuteSafe(db, @"CREATE INDEX IF NOT EXISTS IX_Games_PlayniteGameId
                ON Games (PlayniteGameId);");

            ExecuteSafe(db, @"CREATE INDEX IF NOT EXISTS IX_Games_LastUpdatedUtc
                ON Games (LastUpdatedUtc);");

            ExecuteSafe(db, @"CREATE TABLE IF NOT EXISTS AchievementDefinitions (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                GameId INTEGER NOT NULL,
                ApiName TEXT NOT NULL COLLATE NOCASE,
                DisplayName TEXT NULL,
                Description TEXT NULL,
                IconPath TEXT NULL,
                Hidden INTEGER NOT NULL DEFAULT 0,
                GlobalPercentUnlocked REAL NULL,
                ProgressMax INTEGER NULL,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL,
                FOREIGN KEY (GameId) REFERENCES Games(Id) ON DELETE CASCADE,
                UNIQUE (GameId, ApiName)
            );");

            ExecuteSafe(db, @"CREATE INDEX IF NOT EXISTS IX_AchievementDefinitions_GameId
                ON AchievementDefinitions (GameId);");

            ExecuteSafe(db, @"CREATE TABLE IF NOT EXISTS UserGameProgress (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                UserId INTEGER NOT NULL,
                GameId INTEGER NOT NULL,
                CacheKey TEXT NOT NULL COLLATE NOCASE,
                PlaytimeSeconds INTEGER NOT NULL DEFAULT 0,
                NoAchievements INTEGER NOT NULL DEFAULT 0,
                AchievementsUnlocked INTEGER NOT NULL DEFAULT 0,
                TotalAchievements INTEGER NOT NULL DEFAULT 0,
                LastUpdatedUtc TEXT NOT NULL,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL,
                FOREIGN KEY (UserId) REFERENCES Users(Id) ON DELETE CASCADE,
                FOREIGN KEY (GameId) REFERENCES Games(Id) ON DELETE CASCADE,
                UNIQUE (UserId, GameId),
                UNIQUE (UserId, CacheKey)
            );");

            ExecuteSafe(db, @"CREATE INDEX IF NOT EXISTS IX_UserGameProgress_CacheKey
                ON UserGameProgress (CacheKey);");

            ExecuteSafe(db, @"CREATE INDEX IF NOT EXISTS IX_UserGameProgress_LastUpdatedUtc
                ON UserGameProgress (LastUpdatedUtc);");

            ExecuteSafe(db, @"CREATE INDEX IF NOT EXISTS IX_UserGameProgress_User_LastUpdated
                ON UserGameProgress (UserId, LastUpdatedUtc);");

            ExecuteSafe(db, @"CREATE TABLE IF NOT EXISTS UserAchievements (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                UserGameProgressId INTEGER NOT NULL,
                AchievementDefinitionId INTEGER NOT NULL,
                Unlocked INTEGER NOT NULL DEFAULT 0,
                UnlockTimeUtc TEXT NULL,
                ProgressNum INTEGER NULL,
                ProgressDenom INTEGER NULL,
                LastUpdatedUtc TEXT NOT NULL,
                CreatedUtc TEXT NOT NULL,
                FOREIGN KEY (UserGameProgressId) REFERENCES UserGameProgress(Id) ON DELETE CASCADE,
                FOREIGN KEY (AchievementDefinitionId) REFERENCES AchievementDefinitions(Id) ON DELETE CASCADE,
                UNIQUE (UserGameProgressId, AchievementDefinitionId)
            );");

            ExecuteSafe(db, @"CREATE INDEX IF NOT EXISTS IX_UserAchievements_UnlockTimeUtc
                ON UserAchievements (UnlockTimeUtc);");

            ExecuteSafe(db, @"CREATE INDEX IF NOT EXISTS IX_UserAchievements_Definition
                ON UserAchievements (AchievementDefinitionId);");

            db.ExecuteNonQuery(
                "INSERT OR REPLACE INTO CacheMetadata (Key, Value) VALUES (?, ?);",
                "schema_version",
                SchemaVersion.ToString());
        }

        private void ExecuteSafe(SQLiteDatabase db, string sql)
        {
            try
            {
                db.ExecuteNonQuery(sql);
            }
            catch (System.Exception ex)
            {
                _logger?.Error(ex, $"Failed schema SQL: {sql}");
                throw;
            }
        }
    }
}
