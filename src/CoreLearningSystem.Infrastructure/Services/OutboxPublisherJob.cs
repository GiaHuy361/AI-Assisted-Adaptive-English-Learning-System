using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using CoreLearningSystem.Domain.Entities;
using CoreLearningSystem.Domain.Enums;
using CoreLearningSystem.Infrastructure.Persistence;

namespace CoreLearningSystem.Infrastructure.Services;

public class OutboxPublisherJob
{
    private readonly AppDbContext _context;
    private readonly IProducer<string, string> _producer;
    private readonly BackgroundJobExecutor _executor;
    private readonly ILogger<OutboxPublisherJob> _logger;

    public OutboxPublisherJob(
        AppDbContext context,
        IProducer<string, string> producer,
        BackgroundJobExecutor executor,
        ILogger<OutboxPublisherJob> logger)
    {
        _context = context;
        _producer = producer;
        _executor = executor;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        await _executor.ExecuteAsync("outbox-publisher", async (executionId, token) =>
        {
            var processed = 0;
            var succeeded = 0;
            var failed = 0;
            var skipped = 0;

            // Load Pending or Failed messages that haven't exceeded retry limit (e.g. 5 attempts)
            var messages = await _context.OutboxMessages
                .Where(m => (m.Status == OutboxStatus.Pending || m.Status == OutboxStatus.Failed) && m.RetryCount < 5)
                .OrderBy(m => m.OccurredAt)
                .Take(50) // Batch of 50
                .ToListAsync(token);

            if (messages.Count == 0)
            {
                return (processed, succeeded, failed, skipped);
            }

            _logger.LogInformation("OutboxPublisherJob: Found {Count} pending outbox messages.", messages.Count);

            foreach (var msg in messages)
            {
                processed++;

                await using var tx = await _context.Database.BeginTransactionAsync(token);
                try
                {
                    // Re-fetch message inside transaction
                    var dbMsg = await _context.OutboxMessages.FindAsync(new object[] { msg.Id }, token);
                    if (dbMsg == null || dbMsg.Status == OutboxStatus.Published)
                    {
                        await tx.RollbackAsync(token);
                        skipped++;
                        continue;
                    }

                    var kafkaHeaders = new Headers();
                    if (!string.IsNullOrEmpty(dbMsg.HeadersJson))
                    {
                        var headersDict = JsonSerializer.Deserialize<Dictionary<string, string>>(dbMsg.HeadersJson);
                        if (headersDict != null)
                        {
                            foreach (var kvp in headersDict)
                            {
                                if (kvp.Key == "correlation-id" || kvp.Key == "event-id")
                                {
                                    if (Guid.TryParse(kvp.Value, out var guidVal))
                                    {
                                        kafkaHeaders.Add(kvp.Key, guidVal.ToByteArray());
                                        continue;
                                    }
                                }
                                kafkaHeaders.Add(kvp.Key, System.Text.Encoding.UTF8.GetBytes(kvp.Value));
                            }
                        }
                    }

                    var kafkaMsg = new Message<string, string>
                    {
                        Key = dbMsg.AggregateId,
                        Value = dbMsg.Payload,
                        Headers = kafkaHeaders
                    };

                    _logger.LogInformation("OutboxPublisherJob: Delivering event ID {EventId} (Type: {Type}) to topic {Topic}...",
                        dbMsg.EventId, dbMsg.EventType, dbMsg.Topic);

                    // Produce to Kafka
                    var deliveryResult = await _producer.ProduceAsync(dbMsg.Topic, kafkaMsg, token);

                    dbMsg.Status = OutboxStatus.Published;
                    dbMsg.ProcessedAt = DateTime.UtcNow;
                    dbMsg.RetryCount++;

                    _context.OutboxMessages.Update(dbMsg);
                    await _context.SaveChangesAsync(token);
                    await tx.CommitAsync(token);

                    succeeded++;
                }
                catch (Exception ex)
                {
                    await tx.RollbackAsync(token);
                    failed++;

                    // Re-fetch outside transaction to record failure stats
                    try
                    {
                        var failMsg = await _context.OutboxMessages.FindAsync(new object[] { msg.Id }, token);
                        if (failMsg != null)
                        {
                            failMsg.RetryCount++;
                            failMsg.LastError = ex.Message;
                            if (failMsg.RetryCount >= 5)
                            {
                                failMsg.Status = OutboxStatus.Failed; // Marked failed completely after max retries
                            }
                            else
                            {
                                failMsg.Status = OutboxStatus.Failed; // Kept in Failed status for next retries
                            }
                            _context.OutboxMessages.Update(failMsg);
                            await _context.SaveChangesAsync(token);
                        }
                    }
                    catch (Exception writeEx)
                    {
                        _logger.LogError(writeEx, "Failed to write outbox failure statistics for message {MsgId}", msg.Id);
                    }

                    _logger.LogError(ex, "OutboxPublisherJob: Failed to publish message {MsgId}", msg.Id);
                }
            }

            return (processed, succeeded, failed, skipped);
        }, cancellationToken);
    }
}
