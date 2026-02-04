namespace HeatFlow.Infrastructure.HomeAssistant;

/// <summary>
/// Polityka retry z exponential backoff dla operacji Home Assistant API.
/// </summary>
public static class RetryPolicy
{
    /// <summary>
    /// Wykonuje operację z retry i exponential backoff.
    /// </summary>
    public static async Task<T?> ExecuteWithRetryAsync<T>(
        Func<Task<T?>> operation,
        int maxRetries = 3,
        int baseDelayMs = 1000,
        Func<Exception, bool>? shouldRetry = null) where T : class
    {
        shouldRetry ??= _ => true;

        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (Exception ex) when (attempt < maxRetries && shouldRetry(ex))
            {
                var delay = baseDelayMs * (int)Math.Pow(2, attempt);
                await Task.Delay(delay);
            }
        }

        return null;
    }

    /// <summary>
    /// Wykonuje operację z retry i exponential backoff (dla operacji void).
    /// </summary>
    public static async Task<bool> ExecuteWithRetryAsync(
        Func<Task<bool>> operation,
        int maxRetries = 3,
        int baseDelayMs = 1000,
        Func<Exception, bool>? shouldRetry = null)
    {
        shouldRetry ??= _ => true;

        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (Exception ex) when (attempt < maxRetries && shouldRetry(ex))
            {
                var delay = baseDelayMs * (int)Math.Pow(2, attempt);
                await Task.Delay(delay);
            }
        }

        return false;
    }
}
