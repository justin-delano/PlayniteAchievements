using System;
using System.Collections.Generic;

namespace PlayniteAchievements.Common
{
    /// <summary>
    /// Provides string similarity algorithms for fuzzy matching.
    /// </summary>
    internal static class StringSimilarity
    {
        private const double DefaultWinklerWeightThreshold = 0.7;
        private const int WinklerNumChars = 4;
        private static readonly EqualityComparer<char> CaseInsensitiveComparer = new CaseInsensitiveCharComparer();

        /// <summary>
        /// Returns the Jaro-Winkler similarity between the specified strings using case-insensitive comparison.
        /// The distance is symmetric and falls in the range 0 (no match) to 1 (perfect match).
        /// </summary>
        /// <param name="str">First string.</param>
        /// <param name="str2">Second string.</param>
        /// <param name="winklerWeightThreshold">Weight threshold used to determine whether the similarity score is high enough for prefix bonus. Default is 0.7.</param>
        /// <returns>Similarity between the specified strings.</returns>
        public static double JaroWinklerSimilarityIgnoreCase(string str, string str2, double winklerWeightThreshold = DefaultWinklerWeightThreshold)
        {
            return JaroWinklerSimilarity(str, str2, CaseInsensitiveComparer, winklerWeightThreshold);
        }

        /// <summary>
        /// Returns the Jaro-Winkler similarity between the specified strings.
        /// The distance is symmetric and falls in the range 0 (no match) to 1 (perfect match).
        /// </summary>
        /// <param name="str">First string.</param>
        /// <param name="str2">Second string.</param>
        /// <param name="winklerWeightThreshold">Weight threshold used to determine whether the similarity score is high enough for prefix bonus. Default is 0.7.</param>
        /// <returns>Similarity between the specified strings.</returns>
        public static double JaroWinklerSimilarity(string str, string str2, double winklerWeightThreshold = DefaultWinklerWeightThreshold)
        {
            return JaroWinklerSimilarity(str, str2, EqualityComparer<char>.Default, winklerWeightThreshold);
        }

        /// <summary>
        /// Returns the Jaro-Winkler similarity between the specified strings.
        /// The distance is symmetric and falls in the range 0 (no match) to 1 (perfect match).
        /// </summary>
        /// <param name="str">First string.</param>
        /// <param name="str2">Second string.</param>
        /// <param name="comparer">Comparer used to determine character equality.</param>
        /// <param name="winklerWeightThreshold">Weight threshold used to determine whether the similarity score is high enough for prefix bonus. Winkler's paper used a default value of 0.7.</param>
        /// <returns>Similarity between the specified strings.</returns>
        /// <remarks>
        /// Based on https://gist.github.com/ronnieoverby/2aa19724199df4ec8af6
        /// </remarks>
        public static double JaroWinklerSimilarity(string str, string str2, IEqualityComparer<char> comparer, double winklerWeightThreshold = DefaultWinklerWeightThreshold)
        {
            var lLen1 = str.Length;
            var lLen2 = str2.Length;
            if (lLen1 == 0)
            {
                return lLen2 == 0 ? 1.0 : 0.0;
            }

            var lSearchRange = Math.Max(0, Math.Max(lLen1, lLen2) / 2 - 1);

            var lMatched1 = new bool[lLen1];
            var lMatched2 = new bool[lLen2];

            var lNumCommon = 0;
            for (var i = 0; i < lLen1; ++i)
            {
                var lStart = Math.Max(0, i - lSearchRange);
                var lEnd = Math.Min(i + lSearchRange + 1, lLen2);
                for (var j = lStart; j < lEnd; ++j)
                {
                    if (lMatched2[j])
                    {
                        continue;
                    }

                    if (!comparer.Equals(str[i], str2[j]))
                    {
                        continue;
                    }

                    lMatched1[i] = true;
                    lMatched2[j] = true;
                    ++lNumCommon;
                    break;
                }
            }

            if (lNumCommon == 0)
            {
                return 0.0;
            }

            var lNumHalfTransposed = 0;
            var k = 0;
            for (var i = 0; i < lLen1; ++i)
            {
                if (!lMatched1[i])
                {
                    continue;
                }

                while (!lMatched2[k])
                {
                    ++k;
                }

                if (!comparer.Equals(str[i], str2[k]))
                {
                    ++lNumHalfTransposed;
                }

                ++k;
            }

            var lNumTransposed = lNumHalfTransposed / 2;
            double lNumCommonD = lNumCommon;
            var lWeight = (lNumCommonD / lLen1
                            + lNumCommonD / lLen2
                            + (lNumCommon - lNumTransposed) / lNumCommonD) / 3.0;

            if (lWeight <= winklerWeightThreshold)
            {
                return lWeight;
            }

            var lMax = Math.Min(WinklerNumChars, Math.Min(str.Length, str2.Length));
            var lPos = 0;
            while (lPos < lMax && comparer.Equals(str[lPos], str2[lPos]))
            {
                ++lPos;
            }

            if (lPos == 0)
            {
                return lWeight;
            }

            return lWeight + 0.1 * lPos * (1.0 - lWeight);
        }

        private sealed class CaseInsensitiveCharComparer : EqualityComparer<char>
        {
            public override bool Equals(char x, char y)
            {
                return char.ToUpperInvariant(x) == char.ToUpperInvariant(y);
            }

            public override int GetHashCode(char obj)
            {
                return char.ToUpperInvariant(obj).GetHashCode();
            }
        }
    }
}
