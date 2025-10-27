using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using TenderDeduplication.Interfaces;
using TenderDeduplication.Models.Validation;

namespace TenderDeduplication.Services
{
    /// <summary>
    /// Service for performing validation checks on tenders, such as checking for expired closing dates.
    /// </summary>
    public class TenderValidationService : ITenderValidationService
    {
        private readonly ILogger<TenderValidationService> _logger;
        private static readonly TimeZoneInfo _sastTimeZone = TimeZoneInfo.FindSystemTimeZoneById("South Africa Standard Time");

        public TenderValidationService(ILogger<TenderValidationService> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Validates a tender by checking its closing date against the current date, handling time zones.
        /// </summary>
        /// <param name="tenderRoot">The root JsonElement of the tender message.</param>
        /// <returns>A ValidationResult.</returns>
        public ValidationResult ValidateTender(JsonElement tenderRoot)
        {
            // Get the current date (UTC)
            var today = DateTime.UtcNow.Date;

            // 1. Try to get the "closingDate" property.
            if (!tenderRoot.TryGetProperty("closingDate", out var closingDateElement) ||
                 closingDateElement.ValueKind == JsonValueKind.Null)
            {
                // No closing date provided, so it passes this check.
                return ValidationResult.Success;
            }

            // 2. Try to parse the date.
            if (!closingDateElement.TryGetDateTime(out var closingDate))
            {
                _logger.LogWarning("Could not parse 'closingDate' value: {ClosingDateText}. Tender will be processed.", closingDateElement.ToString());
                return ValidationResult.Success;
            }

            DateTime closingDateUtc;

            // 3. Check the 'Kind' of the parsed DateTime.
            if (closingDate.Kind == DateTimeKind.Unspecified)
            {
                // The date string had no time zone info (e.g., "2025-10-27T11:00:00").
                // MUST assume it's SAST as per the business logic.
                _logger.LogWarning("Closing date {date} has no time zone. Assuming South Africa Standard Time (SAST).", closingDate);
                try
                {
                    closingDateUtc = TimeZoneInfo.ConvertTimeToUtc(closingDate, _sastTimeZone);
                }
                catch (Exception ex)
                {
                    // This could happen if the date is ambiguous during daylight saving (not an issue for SAST, but good practice)
                    _logger.LogError(ex, "Failed to convert Unspecified time {date} from SAST to UTC. Letting tender pass.", closingDate);
                    return ValidationResult.Success;
                }
            }
            else
            {
                // The date was either Local (had an offset like +02:00) or Utc (had a 'Z').
                // .ToUniversalTime() will handle both cases correctly.
                closingDateUtc = closingDate.ToUniversalTime();
            }

            // 4. This is the core check, now 100% in UTC.
            // We compare the .Date part of both UTC DateTimes.
            if (closingDateUtc.Date < today)
            {
                // The tender is closed.
                string reason = $"Tender is closed. The closing date ({closingDateUtc.Date:yyyy-MM-dd} UTC) is before today's date ({today:yyyy-MM-dd} UTC).";

                string? tenderNumber = tenderRoot.TryGetProperty("tenderNumber", out var num) ? num.GetString() : "Unknown";
                _logger.LogInformation("Rejecting tender {TenderNumber}: {Reason}", tenderNumber, reason);

                return ValidationResult.Fail(reason);
            }

            // If we're here, the tender is not expired.
            return ValidationResult.Success;
        }
    }
}
