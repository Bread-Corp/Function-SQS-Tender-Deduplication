using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TenderDeduplication.Models.Validation
{
    /// <summary>
    /// Holds the result of a validation check.
    /// </summary>
    public class ValidationResult
    {
        /// <summary>
        /// Gets whether the validation was successful.
        /// </summary>
        public bool IsValid { get; private set; }

        /// <summary>
        /// Gets the reason for validation failure. Null if valid.
        /// </summary>
        public string? Reason { get; private set; }

        // Private constructor to force use of static methods
        private ValidationResult(bool isValid, string? reason = null)
        {
            IsValid = isValid;
            Reason = reason;
        }

        /// <summary>
        /// Represents a successful validation.
        /// </summary>
        public static ValidationResult Success { get; } = new ValidationResult(true);

        /// <summary>
        /// Creates a failed validation result with a specific reason.
        /// </summary>
        public static ValidationResult Fail(string reason)
        {
            return new ValidationResult(false, reason);
        }
    }
}
