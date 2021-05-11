#warning remove this attribute? seems to be unused

using System.ComponentModel.DataAnnotations;

namespace Nexus.Core
{
    public class IsValidNameAttribute : ValidationAttribute
    {
        protected override ValidationResult IsValid(object value, ValidationContext validationContext)
        {
            var name = value as string;

            if (NexusUtilities.CheckNamingConvention(name, out var errorDescription))
                return ValidationResult.Success;
            else
                return new ValidationResult(errorDescription);
        }
    }
}
