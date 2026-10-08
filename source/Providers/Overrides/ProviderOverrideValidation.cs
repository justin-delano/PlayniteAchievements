namespace PlayniteAchievements.Providers.Overrides
{
    /// <summary>
    /// Result of validating and normalizing a raw override value entered by the user.
    /// </summary>
    public sealed class ProviderOverrideValidation
    {
        /// <summary>Shared "Enter a valid {0}, such as {1}." message for malformed identifiers.</summary>
        public const string InvalidIdErrorKey = "LOCPlayAch_Common_Validation_InvalidId";

        private ProviderOverrideValidation(
            bool isValid,
            string normalizedValue,
            string errorMessageKey,
            string errorExample = null)
        {
            IsValid = isValid;
            NormalizedValue = normalizedValue;
            ErrorMessageKey = errorMessageKey;
            ErrorExample = errorExample;
        }

        public bool IsValid { get; }

        /// <summary>The normalized value to persist when <see cref="IsValid"/> is true (may be null).</summary>
        public string NormalizedValue { get; }

        /// <summary>Localization key for the validation error when <see cref="IsValid"/> is false.</summary>
        public string ErrorMessageKey { get; }

        /// <summary>
        /// Example of a valid value for <see cref="InvalidIdErrorKey"/>, which is formatted with the
        /// descriptor's input label and this example; null for other errors.
        /// </summary>
        public string ErrorExample { get; }

        public static ProviderOverrideValidation Valid(string normalizedValue)
            => new ProviderOverrideValidation(true, normalizedValue, null);

        public static ProviderOverrideValidation Invalid(string errorMessageKey)
            => new ProviderOverrideValidation(false, null, errorMessageKey);

        public static ProviderOverrideValidation InvalidId(string example)
            => new ProviderOverrideValidation(false, null, InvalidIdErrorKey, example);
    }
}
