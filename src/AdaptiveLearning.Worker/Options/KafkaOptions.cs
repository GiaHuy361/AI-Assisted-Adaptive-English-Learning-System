namespace AdaptiveLearning.Worker.Options;

public class KafkaOptions
{
    public const string Position = "Kafka";

    public string BootstrapServers { get; set; } = string.Empty;
    public string ConsumerGroupId { get; set; } = string.Empty;
    public bool EnableAutoCommit { get; set; } = true;
    public string AutoOffsetReset { get; set; } = "Earliest";
    public int RetryCount { get; set; } = 3;
    public int RetryDelaySeconds { get; set; } = 5;
    public string DeadLetterTopic { get; set; } = string.Empty;
}
