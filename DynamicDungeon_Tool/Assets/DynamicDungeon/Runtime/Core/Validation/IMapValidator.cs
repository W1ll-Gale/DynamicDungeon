public interface IMapValidator
{
    string ValidatorName { get; }

    ValidationResult Validate(ValidationContext context);
}