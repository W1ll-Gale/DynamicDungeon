using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class MapValidationPass
{
    private readonly List<IMapValidator> _validators = new List<IMapValidator>();
    private int _maxRetries = 0;

    public MapValidationPass AddValidator(IMapValidator validator)
    {
        if (validator == null) throw new ArgumentNullException(nameof(validator));
        _validators.Add(validator);
        return this;
    }

    public MapValidationPass SetMaxRetries(int maxRetries)
    {
        _maxRetries = Mathf.Max(0, maxRetries);
        return this;
    }


    public ValidationReport Run(ValidationContext context, int attemptNumber = 1)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));

        ValidationReport report = new ValidationReport { AttemptNumber = attemptNumber };

        foreach (IMapValidator validator in _validators)
        {
            ValidationResult result;
            try
            {
                result = validator.Validate(context);
            }
            catch (Exception ex)
            {
                result = ValidationResult.Fail(validator.ValidatorName, $"Exception during validation: {ex.Message}");

                Debug.LogError($"[MapValidationPass] Validator '{validator.ValidatorName}' threw: {ex}");
            }

            report.AddResult(result);

            if (!result.IsValid) break;
        }

        return report;
    }

    public ValidationReport RunWithRetry(ValidationContext initialContext, Func<int, GenMap> regenerate, Func<GenMap, ValidationContext> buildContext)
    {
        if (initialContext == null) throw new ArgumentNullException(nameof(initialContext));
        if (regenerate == null) throw new ArgumentNullException(nameof(regenerate));
        if (buildContext == null) throw new ArgumentNullException(nameof(buildContext));

        ValidationContext current = initialContext;
        ValidationReport report = Run(current, 1);

        for (int attempt = 2; !report.IsValid && attempt <= _maxRetries + 1; attempt++)
        {
            Debug.Log($"[MapValidationPass] Attempt {attempt - 1} failed. " +
                      $"Retrying... (max {_maxRetries})");

            GenMap newMap = regenerate(attempt);
            if (newMap == null)
            {
                Debug.LogWarning("[MapValidationPass] regenerate callback returned null. Aborting.");
                break;
            }

            current = buildContext(newMap);
            report = Run(current, attempt);
        }

        if (!report.IsValid)
        {
            Debug.LogWarning($"[MapValidationPass] All {_maxRetries + 1} attempt(s) failed.\n{report}");
        }
        else
        {
            Debug.Log($"[MapValidationPass] Validation passed on attempt #{report.AttemptNumber}.\n{report}");
        }

        return report;
    }
}