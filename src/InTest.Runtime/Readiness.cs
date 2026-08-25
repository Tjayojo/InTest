using System.Diagnostics;

namespace InTest.Runtime;

/// <summary>
/// Readiness gating. Framework-neutral.
/// Post-deploy cold start is the largest single source of flaky gates; failing here once
/// with a clear message beats N confusing test failures.
/// </summary>
public static class Readiness
{
    /// <summary>
    /// Statuses that mean the probe path is wrong rather than the service being slow. No
    /// amount of waiting fixes a route that does not exist, and burning the full timeout on
    /// one turns a three-second diagnosis into a two-minute one.
    /// </summary>
    private static readonly int[] TerminalStatuses = [404, 405, 410, 501];

    public static async Task WaitAsync(HttpClient client, ReadinessOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);

        if (!options.Enabled)
        {
            return;
        }

        var deadline = Stopwatch.StartNew();
        var timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        var interval = TimeSpan.FromSeconds(options.IntervalSeconds);
        var consecutive = 0;
        var lastOutcome = "no response";

        while (deadline.Elapsed < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var response = await client.GetAsync(options.Path, cancellationToken).ConfigureAwait(false);
                var status = (int)response.StatusCode;
                lastOutcome = status.ToString();

                if (status == options.ExpectStatus)
                {
                    if (++consecutive >= options.ConsecutiveSuccesses)
                    {
                        return;
                    }
                }
                else if (TerminalStatuses.Contains(status))
                {
                    throw new ReadinessTimeoutException(
                        $"Readiness probe '{options.Path}' returned {status}, which will not change by waiting. " +
                        $"Resolved to '{Resolve(client, options.Path)}'. " +
                        "A path with no leading slash resolves under the API base URL; one with a leading " +
                        "slash resolves against the host root, which is where health endpoints usually live.");
                }
                else
                {
                    consecutive = 0;
                }
            }
            catch (HttpRequestException ex)
            {
                lastOutcome = ex.GetType().Name;
                consecutive = 0;
            }

            if (interval > TimeSpan.Zero)
            {
                await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
            }
        }

        throw new ReadinessTimeoutException(
            $"Service did not become ready within {options.TimeoutSeconds}s (last response: {lastOutcome}). " +
            $"Probed '{Resolve(client, options.Path)}' expecting {options.ExpectStatus}, " +
            $"requiring {options.ConsecutiveSuccesses} consecutive successes.");
    }

    /// <summary>Best-effort absolute form of the probe path, for messages only.</summary>
    private static string Resolve(HttpClient client, string path)
    {
        if (client.BaseAddress is null)
        {
            return path;
        }
        return Uri.TryCreate(client.BaseAddress, path, out var absolute) ? absolute.ToString() : path;
    }
}
