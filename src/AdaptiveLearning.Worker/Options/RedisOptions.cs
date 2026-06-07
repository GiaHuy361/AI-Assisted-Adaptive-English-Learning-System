namespace AdaptiveLearning.Worker.Options;

public class RedisOptions
{
    public const string Position = "Redis";

    public string ConnectionString { get; set; } = string.Empty;
    public string InstanceName { get; set; } = string.Empty;
    public int DefaultTtlMinutes { get; set; } = 60;
}
