using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TenderDeduplication.Data;
using TenderDeduplication.Interfaces;

namespace TenderDeduplication.Services
{
    /// <summary>
    /// Service to handle the logic of checking for duplicate tenders using an in-memory cache.
    /// The cache is static to persist across multiple batches within a single Lambda invocation.
    /// </summary>
    public class TenderDeduplicatorService : ITenderDeduplicatorService
    {
        // Changed the injected type from ApplicationDbContext to the factory
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
        private readonly ILogger<TenderDeduplicatorService> _logger;

        // Static cache to hold tender numbers. It's cleared when the Lambda execution environment is recycled.
        private static Dictionary<string, HashSet<string>>? _tenderCache;
        private static readonly object _cacheLock = new object();

        // Update the constructor to accept the factory
        public TenderDeduplicatorService(IDbContextFactory<ApplicationDbContext> contextFactory, ILogger<TenderDeduplicatorService> logger)
        {
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc/>
        public async Task EnsureCacheIsLoadedAsync()
        {
            // Double-check locking to ensure thread safety and prevent multiple initializations.
            if (_tenderCache != null)
            {
                return;
            }

            lock (_cacheLock)
            {
                if (_tenderCache != null)
                {
                    return;
                }

                // Initialize the cache inside the lock
                _tenderCache = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            }

            _logger.LogInformation("In-memory tender cache is not initialized. Populating from database...");

            try
            {
                // Create tasks to fetch all tender numbers. Each task will create its own DbContext via the factory.
                var sarsTask = LoadSourceTenderNumbersAsync("SARS", context => context.SarsTenders.Select(t => t.TenderNumber));
                var eTendersTask = LoadSourceTenderNumbersAsync("eTenders", context => context.eTenders.Select(t => t.TenderNumber));
                var eskomTask = LoadSourceTenderNumbersAsync("Eskom", context => context.EskomTenders.Select(t => t.TenderNumber));
                var transnetTask = LoadSourceTenderNumbersAsync("Transnet", context => context.TransnetTenders.Select(t => t.TenderNumber));
                var sanralTask = LoadSourceTenderNumbersAsync("SANRAL", context => context.SanralTenders.Select(t => t.TenderNumber));

                // Await all database queries to complete
                await Task.WhenAll(sarsTask, eTendersTask, eskomTask, transnetTask, sanralTask);

                _logger.LogInformation("Tender cache populated successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to populate tender cache from the database. Resetting cache.");
                // In case of failure, reset the cache to null so the next invocation can retry.
                _tenderCache = null;
                throw; // Rethrow to fail the invocation, as we can't proceed without the cache.
            }
        }

        /// <inheritdoc/>
        public bool IsDuplicate(string source, string tenderNumber)
        {
            if (_tenderCache == null)
            {
                // This should not happen if EnsureCacheIsLoadedAsync is called first.
                throw new InvalidOperationException("Tender cache has not been initialized.");
            }

            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(tenderNumber))
            {
                return false; // Cannot be a duplicate without source or number.
            }

            // Fast O(1) lookup
            return _tenderCache.TryGetValue(source, out var tenderNumbers) && tenderNumbers.Contains(tenderNumber);
        }

        /// <summary>
        /// A helper method to create a DbContext, execute the query, and load the results into the cache.
        /// </summary>
        private async Task LoadSourceTenderNumbersAsync(string sourceKey, Func<ApplicationDbContext, IQueryable<string>> queryBuilder)
        {
            // Create a new DbContext instance from the factory just for this query
            await using var context = await _contextFactory.CreateDbContextAsync();

            // Build and execute the query on the new, temporary context
            var query = queryBuilder(context);
            var tenderNumbers = await query.ToListAsync();
            var numberSet = new HashSet<string>(tenderNumbers, StringComparer.OrdinalIgnoreCase);

            lock (_cacheLock)
            {
                _tenderCache[sourceKey] = numberSet;
            }

            _logger.LogInformation("Loaded {Count} tender numbers for source: {Source}", numberSet.Count, sourceKey);
        }
    }
}