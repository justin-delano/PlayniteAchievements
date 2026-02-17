using System;
using System.Collections.Generic;
using PlayniteAchievements.Services.Database;
using Xunit;

namespace PlayniteAchievements.SqlNado.Tests
{
    public class SqlNadoCacheBehaviorTests
    {
        [Fact]
        public void ComputeStaleDefinitionIds_ReturnsMissingOnly_CaseInsensitive()
        {
            var existing = new Dictionary<string, long>
            {
                ["Alpha"] = 1,
                ["Bravo"] = 2,
                ["Charlie"] = 3
            };

            var incoming = new[] { "alpha", " CHARLIE " };

            var stale = SqlNadoCacheBehavior.ComputeStaleDefinitionIds(existing, incoming);

            Assert.Single(stale);
            Assert.Contains(2, stale);
        }

        [Fact]
        public void ComputeStaleDefinitionIds_EmptyIncoming_DeletesAllValidIds()
        {
            var existing = new Dictionary<string, long>
            {
                ["A"] = 10,
                ["B"] = 11
            };

            var stale = SqlNadoCacheBehavior.ComputeStaleDefinitionIds(existing, Array.Empty<string>());

            Assert.Equal(2, stale.Count);
            Assert.Contains(10, stale);
            Assert.Contains(11, stale);
        }

        [Fact]
        public void ComputeStaleDefinitionIds_NullIncoming_DeletesAllValidIds()
        {
            var existing = new Dictionary<string, long>
            {
                ["A"] = 10,
                ["B"] = 11
            };

            var stale = SqlNadoCacheBehavior.ComputeStaleDefinitionIds(existing, null);

            Assert.Equal(2, stale.Count);
            Assert.Contains(10, stale);
            Assert.Contains(11, stale);
        }

        [Fact]
        public void ComputeStaleDefinitionIds_IgnoresBlankNamesAndNonPositiveIds()
        {
            var existing = new Dictionary<string, long>
            {
                ["  "] = 1,
                ["Real"] = 0,
                ["Other"] = -5,
                ["Keep"] = 9
            };

            var incoming = new[] { "keep" };

            var stale = SqlNadoCacheBehavior.ComputeStaleDefinitionIds(existing, incoming);

            Assert.Empty(stale);
        }

        [Fact]
        public void ComputeStaleDefinitionIds_DedupesByApiName_AndKeepsOnlyValidStaleIds()
        {
            var existing = new Dictionary<string, long>
            {
                ["One"] = 100,
                ["Two"] = 200,
                ["Three"] = 300
            };

            var incoming = new[] { " two ", "TWO", "three" };

            var stale = SqlNadoCacheBehavior.ComputeStaleDefinitionIds(existing, incoming);

            Assert.Single(stale);
            Assert.Equal(100, stale[0]);
        }

        [Fact]
        public void ComputeStaleDefinitionIds_NullExisting_ReturnsEmpty()
        {
            var stale = SqlNadoCacheBehavior.ComputeStaleDefinitionIds(null, new[] { "A" });
            Assert.Empty(stale);
        }

        [Fact]
        public void ComputeStaleDefinitionIds_EmptyExisting_ReturnsEmpty()
        {
            var stale = SqlNadoCacheBehavior.ComputeStaleDefinitionIds(new Dictionary<string, long>(), new[] { "A" });
            Assert.Empty(stale);
        }

        [Theory]
        [InlineData(0, 0, 0, true)]
        [InlineData(-1, 0, 0, true)]
        [InlineData(0, -1, 0, true)]
        [InlineData(0, 0, -1, true)]
        [InlineData(1, 0, 0, false)]
        [InlineData(0, 2, 0, false)]
        [InlineData(0, 0, 3, false)]
        [InlineData(1, 1, 1, false)]
        public void ShouldMarkLegacyImportDone_UsesFailureAndRemainingCounts(
            int parseFailedCount,
            int dbWriteFailedCount,
            int remainingFileCount,
            bool expected)
        {
            var actual = SqlNadoCacheBehavior.ShouldMarkLegacyImportDone(
                parseFailedCount,
                dbWriteFailedCount,
                remainingFileCount);
            Assert.Equal(expected, actual);
        }
    }
}
