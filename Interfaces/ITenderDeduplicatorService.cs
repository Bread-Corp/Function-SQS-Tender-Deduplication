using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TenderDeduplication.Interfaces
{
    /// <summary>
    /// Defines the contract for the service responsible for tender deduplication logic.
    /// </summary>
    public interface ITenderDeduplicatorService
    {
        /// <summary>
        /// Ensures that the tender number cache is loaded from the database.
        /// This method should be called once per Lambda invocation.
        /// </summary>
        Task EnsureCacheIsLoadedAsync();

        /// <summary>
        /// Checks if a tender is a duplicate based on its source and tender number.
        /// </summary>
        /// <param name="source">The source of the tender (e.g., "SARS", "eTenders").</param>
        /// <param name="tenderNumber">The tender number to check.</param>
        /// <returns>True if the tender exists in the cache; otherwise, false.</returns>
        bool IsDuplicate(string source, string tenderNumber);
    }
}
