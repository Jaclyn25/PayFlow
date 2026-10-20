using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace PayFlow.Infrastructure.Messaging;

public static class ResiliencePolicies
{
    public static ResiliencePipeline CreateRabbitMqRetryPipeline(
        int maxRetries = 3,
        double initialDelaySeconds = 2.0,
        ILogger? logger = null)
    {
        return new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = maxRetries,
                Delay = TimeSpan.FromSeconds(initialDelaySeconds),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                OnRetry = args =>
                {
                    logger?.LogWarning(args.Outcome.Exception,
                        "[RESILIENCE] Transient failure on attempt {Attempt}/{MaxRetries}. Retrying in {Delay:F2}s...",
                        args.AttemptNumber + 1, maxRetries, args.RetryDelay.TotalSeconds);
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }
}
