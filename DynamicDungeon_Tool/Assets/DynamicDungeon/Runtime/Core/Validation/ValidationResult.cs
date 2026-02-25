using System.Collections.Generic;

public sealed class ValidationResult
{
    public bool IsValid { get; private set; }
    public string ValidatorName { get; private set; }
    public string Reason { get; private set; }

    public IReadOnlyDictionary<string, float> Diagnostics => _diagnostics;

    private Dictionary<string, float> _diagnostics = new Dictionary<string, float>();

    private ValidationResult() { }

    public static ValidationResult Pass(string validatorName)
    {
        ValidationResult result = new ValidationResult();
        result.IsValid = true;
        result.ValidatorName = validatorName;
        result.Reason = "Passed.";
        return result;
    }

    public static ValidationResult Pass(string validatorName, Dictionary<string, float> diagnostics)
    {
        ValidationResult result = Pass(validatorName);
        if (diagnostics != null) result._diagnostics = diagnostics;
        return result;
    }

    public static ValidationResult Fail(string validatorName, string reason)
    {
        ValidationResult result = new ValidationResult();
        result.IsValid = false;
        result.ValidatorName = validatorName;
        result.Reason = reason;
        return result;
    }

    public static ValidationResult Fail(string validatorName, string reason, Dictionary<string, float> diagnostics)
    {
        ValidationResult result = Fail(validatorName, reason);
        if (diagnostics != null) result._diagnostics = diagnostics;
        return result;
    }

    public override string ToString() => $"[{ValidatorName}] {(IsValid ? "PASS" : "FAIL")}: {Reason}";
}