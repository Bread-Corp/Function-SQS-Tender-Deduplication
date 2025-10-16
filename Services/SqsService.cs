using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TenderDeduplication.Interfaces;

namespace TenderDeduplication.Services
{
    /// <summary>
    /// Production-ready SQS service implementation for message routing.
    /// </summary>
    public class SqsService : ISqsService
    {
        private readonly IAmazonSQS _sqsClient;
        private readonly ILogger<SqsService> _logger;

        public SqsService(IAmazonSQS sqsClient, ILogger<SqsService> logger)
        {
            _sqsClient = sqsClient ?? throw new ArgumentNullException(nameof(sqsClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc/>
        public async Task SendMessageBatchAsync(string queueUrl, List<string> messages)
        {
            if (string.IsNullOrWhiteSpace(queueUrl))
                throw new ArgumentException("Queue URL cannot be null or empty.", nameof(queueUrl));

            if (messages == null || !messages.Any())
            {
                _logger.LogDebug("No messages provided for batch send to {QueueUrl}", queueUrl);
                return;
            }

            _logger.LogInformation("Starting batch send of {MessageCount} messages to queue: {QueueUrl}", messages.Count, queueUrl);

            // SQS allows up to 10 messages per batch
            const int batchSize = 10;
            for (int i = 0; i < messages.Count; i += batchSize)
            {
                var batchMessages = messages.Skip(i).Take(batchSize).ToList();
                var entries = batchMessages.Select((msgBody, index) => new SendMessageBatchRequestEntry
                {
                    Id = $"msg_{i + index}",
                    MessageBody = msgBody
                }).ToList();

                var request = new SendMessageBatchRequest
                {
                    QueueUrl = queueUrl,
                    Entries = entries
                };

                try
                {
                    var response = await _sqsClient.SendMessageBatchAsync(request);
                    if (response.Failed.Any())
                    {
                        foreach (var failed in response.Failed)
                        {
                            _logger.LogError("Failed to send message {Id} to {QueueUrl}. Code: {Code}, Reason: {Message}",
                                failed.Id, queueUrl, failed.Code, failed.Message);
                        }
                        // Throw an exception to ensure the Lambda invocation fails and messages can be retried.
                        throw new InvalidOperationException($"Failed to send {response.Failed.Count} of {entries.Count} messages to {queueUrl}.");
                    }
                    _logger.LogInformation("Successfully sent batch of {Count} messages to {QueueUrl}", entries.Count, queueUrl);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Exception while sending message batch to {QueueUrl}", queueUrl);
                    throw;
                }
            }
        }

        /// <inheritdoc/>
        public async Task DeleteMessageBatchAsync(string queueUrl, List<(string id, string receiptHandle)> messages)
        {
            if (string.IsNullOrWhiteSpace(queueUrl))
                throw new ArgumentException("Queue URL cannot be null or empty.", nameof(queueUrl));

            if (messages == null || !messages.Any())
            {
                _logger.LogDebug("No messages provided for batch delete from {QueueUrl}", queueUrl);
                return;
            }

            _logger.LogInformation("Starting batch delete of {MessageCount} messages from queue: {QueueUrl}", messages.Count, queueUrl);

            const int batchSize = 10;
            for (int i = 0; i < messages.Count; i += batchSize)
            {
                var batchMessages = messages.Skip(i).Take(batchSize).ToList();
                var entries = batchMessages.Select(m => new DeleteMessageBatchRequestEntry
                {
                    Id = m.id,
                    ReceiptHandle = m.receiptHandle
                }).ToList();

                var request = new DeleteMessageBatchRequest
                {
                    QueueUrl = queueUrl,
                    Entries = entries
                };

                try
                {
                    var response = await _sqsClient.DeleteMessageBatchAsync(request);
                    if (response.Failed.Any())
                    {
                        // Log failures but don't throw. A failed delete means the message will be reprocessed, which is acceptable.
                        foreach (var failed in response.Failed)
                        {
                            _logger.LogWarning("Failed to delete message {Id} from {QueueUrl}. Code: {Code}, Reason: {Message}",
                                failed.Id, queueUrl, failed.Code, failed.Message);
                        }
                    }
                    _logger.LogInformation("Successfully processed delete batch for {Count} messages from {QueueUrl}", entries.Count, queueUrl);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Exception while deleting message batch from {QueueUrl}. Messages may be reprocessed.", queueUrl);
                    // Do not rethrow here to allow the function to complete. Reprocessing is the fallback.
                }
            }
        }
    }
}
