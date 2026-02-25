using System.Collections.Generic;
using System.Text;

public sealed class ValidationReport
{
    private readonly List<ValidationResult> _results = new List<ValidationResult>();

    public IReadOnlyList<ValidationResult> Results => _results;

    public bool IsValid
    {
        get
        {
            foreach (ValidationResult r in _results)
            {
                if (!r.IsValid) return false;
            }
            return true;
        }
    }

    public int AttemptNumber { get; set; } = 1;

    public void AddResult(ValidationResult result)
    {
        if (result != null) _results.Add(result);
    }

    public override string ToString()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"=== Validation Report (Attempt #{AttemptNumber}) ===");
        sb.AppendLine($"Overall: {(IsValid ? "VALID" : "INVALID")}");
        sb.AppendLine();

        foreach (ValidationResult r in _results)
        {
            sb.AppendLine(r.ToString());
        }

        return sb.ToString();
    }
}