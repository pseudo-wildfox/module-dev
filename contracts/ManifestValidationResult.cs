namespace ModuleDev.Contracts.Validation;

public sealed class ManifestValidationResult
{
    private ManifestValidationResult(
        bool isValid,
        IReadOnlyList<string> errors)
    {
        IsValid = isValid;
        Errors = errors;
    }

    public bool IsValid { get; }

    public IReadOnlyList<string> Errors { get; }

    public static ManifestValidationResult Success()
    {
        return new ManifestValidationResult(
            isValid: true,
            errors: Array.Empty<string>());
    }

    public static ManifestValidationResult Failure(
        params string[] errors)
    {
        return new ManifestValidationResult(
            isValid: false,
            errors);
    }

    public static ManifestValidationResult Failure(
        IReadOnlyList<string> errors)
    {
        return new ManifestValidationResult(
            isValid: false,
            errors);
    }
}