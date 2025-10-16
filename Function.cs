using Microsoft.Extensions.Logging;
using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using TenderDeduplication.Data;
using TenderDeduplication.Interfaces;
using TenderDeduplication.Services;


// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace TenderDeduplication;

/// <summary>
/// AWS Lambda function for deduplicating tender messages from an SQS queue.
/// </summary>
public class Function
{
    private readonly ISqsService _sqsService;
    private readonly ITenderDeduplicatorService _deduplicatorService;
    private readonly ILogger<Function> _logger;
    private readonly IAmazonSQS _sqsClient;

    private readonly string _sourceQueueUrl;
    private readonly string _aiQueueUrl;
    private readonly string _duplicateQueueUrl;

    /// <summary>
    /// Default constructor used by AWS Lambda runtime with dependency injection.
    /// </summary>
    public Function() : this(null, null, null, null) { }

    /// <summary>
    /// Constructor with dependency injection support for testing.
    /// </summary>
    public Function(ISqsService? sqsService, ITenderDeduplicatorService? deduplicatorService, ILogger<Function>? logger, IAmazonSQS? sqsClient)
    {
        var serviceProvider = ConfigureServices();

        _sqsService = sqsService ?? serviceProvider.GetRequiredService<ISqsService>();
        _deduplicatorService = deduplicatorService ?? serviceProvider.GetRequiredService<ITenderDeduplicatorService>();
        _logger = logger ?? serviceProvider.GetRequiredService<ILogger<Function>>();
        _sqsClient = sqsClient ?? serviceProvider.GetRequiredService<IAmazonSQS>();

        // Load and validate required environment variables for queue configuration
        _sourceQueueUrl = Environment.GetEnvironmentVariable("SOURCE_QUEUE_URL") ?? throw new InvalidOperationException("SOURCE_QUEUE_URL environment variable is required.");
        _aiQueueUrl = Environment.GetEnvironmentVariable("AI_QUEUE_URL") ?? throw new InvalidOperationException("AI_QUEUE_URL environment variable is required.");
        _duplicateQueueUrl = Environment.GetEnvironmentVariable("DUPLICATE_QUEUE_URL") ?? throw new InvalidOperationException("DUPLICATE_QUEUE_URL environment variable is required.");

        _logger.LogInformation("Lambda function initialized successfully.");
    }

    /// <summary>
    /// Configures the dependency injection container with all required services.
    /// </summary>
    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // Configure structured logging for CloudWatch integration
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Information);
        });

        // Register AWS clients
        services.AddSingleton<IAmazonSQS, AmazonSQSClient>();

        // Register application services
        services.AddSingleton<ISqsService, SqsService>();
        // Use AddSingleton for the deduplicator to ensure the static cache is managed by a single instance per container.
        services.AddSingleton<ITenderDeduplicatorService, TenderDeduplicatorService>();

        // Configure and register the DbContext
        var connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING") ?? throw new InvalidOperationException("DB_CONNECTION_STRING environment variable is required.");
        services.AddDbContextFactory<ApplicationDbContext>(options =>
            options.UseSqlServer(connectionString)
        );

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Main Lambda entry point that processes SQS events and continues polling for more messages.
    /// </summary>
    public async Task<string> FunctionHandler(SQSEvent evnt, ILambdaContext context)
    {
        var functionStart = DateTime.UtcNow;
        var totalProcessed = 0;
        var totalDuplicates = 0;
        var batchCount = 0;

        _logger.LogInformation("Lambda invocation started. Initial message count: {Count}", evnt.Records.Count);

        try
        {
            // Crucial Step: Ensure the tender cache is loaded from the DB once per invocation.
            await _deduplicatorService.EnsureCacheIsLoadedAsync();

            // Process the initial batch of messages from the trigger event
            if (evnt.Records.Any())
            {
                batchCount++;
                _logger.LogInformation("Processing initial SQS event batch #{BatchNumber}", batchCount);
                var initialResult = await ProcessMessageBatch(evnt.Records);
                totalProcessed += initialResult.processed;
                totalDuplicates += initialResult.duplicates;
            }

            // Continue polling for more messages until the time limit is approaching
            while (context.RemainingTime > TimeSpan.FromSeconds(30))
            {
                var messages = await PollMessagesFromQueue(10); // Max SQS batch size
                if (!messages.Any())
                {
                    _logger.LogInformation("Queue polling complete. No more messages found.");
                    break;
                }

                batchCount++;
                _logger.LogInformation("Processing polled batch #{BatchNumber}. Message count: {Count}", batchCount, messages.Count);
                var batchResult = await ProcessMessageBatch(messages);
                totalProcessed += batchResult.processed;
                totalDuplicates += batchResult.duplicates;

                await Task.Delay(100); // Small delay to prevent aggressive polling
            }

            var duration = (DateTime.UtcNow - functionStart).TotalMilliseconds;
            var result = $"Success. Batches: {batchCount}, Total Processed: {totalProcessed}, Duplicates Found: {totalDuplicates}, Duration: {duration:F0}ms";
            _logger.LogInformation("Lambda execution finished. {Result}", result);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lambda execution failed unexpectedly.");
            throw;
        }
    }

    /// <summary>
    /// Polls for a batch of messages from the source queue.
    /// </summary>
    private async Task<List<SQSEvent.SQSMessage>> PollMessagesFromQueue(int maxMessages)
    {
        var request = new ReceiveMessageRequest
        {
            QueueUrl = _sourceQueueUrl,
            MaxNumberOfMessages = maxMessages,
            WaitTimeSeconds = 2, // Use short polling for responsiveness
            VisibilityTimeout = 300 // 5-minute processing window
        };

        var response = await _sqsClient.ReceiveMessageAsync(request);
        return response.Messages.Select(m => new SQSEvent.SQSMessage
        {
            MessageId = m.MessageId,
            ReceiptHandle = m.ReceiptHandle,
            Body = m.Body,
            Attributes = m.Attributes,
            MessageAttributes = m.MessageAttributes.ToDictionary(a => a.Key, a => new SQSEvent.MessageAttribute { StringValue = a.Value.StringValue, DataType = a.Value.DataType })
        }).ToList();
    }

    /// <summary>
    /// Processes a batch of SQS messages for deduplication and routes them accordingly.
    /// </summary>
    private async Task<(int processed, int duplicates)> ProcessMessageBatch(IList<SQSEvent.SQSMessage> messages)
    {
        var uniqueMessages = new List<string>();
        var duplicateMessages = new List<string>();
        var messagesToDelete = new List<(string id, string receiptHandle)>();

        foreach (var message in messages)
        {
            try
            {
                // Lightweight parse to extract only the necessary fields
                using var jsonDoc = JsonDocument.Parse(message.Body);
                var root = jsonDoc.RootElement;

                var tenderNumber = root.TryGetProperty("tenderNumber", out var numberProp) ? numberProp.GetString() : null;
                var source = root.TryGetProperty("source", out var sourceProp) ? sourceProp.GetString() : null;

                if (string.IsNullOrWhiteSpace(tenderNumber) || string.IsNullOrWhiteSpace(source))
                {
                    _logger.LogWarning("Message {MessageId} is missing 'tenderNumber' or 'source'. Treating as unique to avoid data loss.", message.MessageId);
                    uniqueMessages.Add(message.Body);
                }
                else if (_deduplicatorService.IsDuplicate(source, tenderNumber))
                {
                    _logger.LogInformation("Duplicate found for source '{Source}' with tender number '{TenderNumber}'.", source, tenderNumber);
                    duplicateMessages.Add(message.Body);
                }
                else
                {
                    uniqueMessages.Add(message.Body);
                }

                // Add to delete list regardless of outcome, as it has been processed.
                messagesToDelete.Add((message.MessageId, message.ReceiptHandle));
            }
            catch (JsonException jsonEx)
            {
                _logger.LogError(jsonEx, "Failed to parse JSON for message {MessageId}. Sending to duplicate/error queue.", message.MessageId);
                duplicateMessages.Add(message.Body); // Route malformed JSON to duplicate queue for inspection.
                messagesToDelete.Add((message.MessageId, message.ReceiptHandle));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error processing message {MessageId}. It will be retried after visibility timeout.", message.MessageId);
                // Do not add to delete list, so it can be retried.
            }
        }

        // Route the categorized messages in batches
        var sendUniqueTask = _sqsService.SendMessageBatchAsync(_aiQueueUrl, uniqueMessages);
        var sendDuplicateTask = _sqsService.SendMessageBatchAsync(_duplicateQueueUrl, duplicateMessages);

        // Wait for both routing tasks to complete
        await Task.WhenAll(sendUniqueTask, sendDuplicateTask);

        // Clean up the processed messages from the source queue
        if (messagesToDelete.Any())
        {
            await _sqsService.DeleteMessageBatchAsync(_sourceQueueUrl, messagesToDelete);
        }

        _logger.LogInformation("Batch processed. Unique: {UniqueCount}, Duplicates: {DuplicateCount}, Deleted: {DeletedCount}",
            uniqueMessages.Count, duplicateMessages.Count, messagesToDelete.Count);

        return (messages.Count, duplicateMessages.Count);
    }
}