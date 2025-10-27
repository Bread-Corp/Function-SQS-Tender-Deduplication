using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using TenderDeduplication.Models.Validation;

namespace TenderDeduplication.Interfaces
{
    /// <summary>
    /// Defines a service for performing validation checks on tenders.
    /// </summary>
    public interface ITenderValidationService
    {
        /// <summary>
        /// Validates a tender based on its JSON representation.
        /// Currently checks if the tender's closing date has passed.
        /// </summary>
        /// <param name="tenderRoot">The root JsonElement of the tender message.</param>
        /// <returns>A ValidationResult indicating if the tender is valid and a reason if not.</returns>
        ValidationResult ValidateTender(JsonElement tenderRoot);
    }
}
