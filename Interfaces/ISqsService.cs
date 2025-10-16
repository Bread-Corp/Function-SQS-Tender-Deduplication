using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TenderDeduplication.Interfaces
{
    /// <summary>
    /// Defines the contract for interacting with Amazon SQS.
    /// </summary>
    public interface ISqsService
    {
        /// <summary>
        /// Sends a batch of messages to the specified SQS queue.
        /// The message bodies should be the original JSON strings.
        /// </summary>
        /// <param name="queueUrl">The URL of the target SQS queue.</param>
        /// <param name="messages">A list of messages to send, where each item is the string body.</param>
        Task SendMessageBatchAsync(string queueUrl, List<string> messages);

        /// <summary>
        /// Deletes a batch of messages from the specified SQS queue.
        /// </summary>
        /// <param name="queueUrl">The URL of the source SQS queue.</param>
        /// <param name="messages">A list of tuples containing the message ID and its receipt handle.</param>
        Task DeleteMessageBatchAsync(string queueUrl, List<(string id, string receiptHandle)> messages);
    }
}
