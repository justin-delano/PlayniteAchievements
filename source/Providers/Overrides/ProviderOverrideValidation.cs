namespace PlayniteAchievements.Providers.Overrides
{
    /// <summary>
    /// Result of validating and normalizing a raw override value entered by the user.
    /// </summary>
    public sealed class ProviderOverrideValidation
    {
        private ProviderOverrideValidation(
            bool isValid,
            string normalizedValue,
            string errorMessageKey,
            string errorMessageFallback)
        {
            IsValid = isValid;
            NormalizedValue = normalizedValue;
            ErrorMessageKey = errorMessageKey;
            ErrorMessageFallback = errorMessageFallback;
        }

        public bool IsValid { get; }

        /// <summary>The normalized value to persist when <see cref="IsValid"/> is true (may be null).</summary>
        public string NormalizedValue { get; }

        /// <summary>Localization key for the validation error when <see cref="IsValid"/> is false.</summary>
        public string ErrorMessageKey { get; }

        /// <summary>Fallback text for the validation error when the localization key is missing.</summary>
        public string ErrorMessageFallback { get; }

        public static ProviderOverrideValidation Valid(string normalizedValue)
            => new ProviderOverrideValidation(true, normalizedValue, null, null);

        public static ProviderOverrideValidation Invalid(string errorMessageKey, string errorMessageFallback)
            => new ProviderOverrideValidation(false, null, errorMessageKey, errorMessageFallback);
    }
}
