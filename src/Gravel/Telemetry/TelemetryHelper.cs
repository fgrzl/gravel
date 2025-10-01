using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Gravel.Telemetry;

public static class TelemetryHelper
{
    public static ActivityScope StartActivityScope(ActivitySource activitySource, ILogger logger, string name,
        ActivityKind kind, params KeyValuePair<string, object?>[] tags)
    {
        var act = activitySource?.StartActivity(name, kind);
        if (act != null && tags != null)
            foreach (var kv in tags)
            {
                if (kv.Value is null) continue;
                act.SetTag(kv.Key, kv.Value);
            }

        IDisposable? scope = null;
        if (act != null)
        {
            var dict = new Dictionary<string, object?>
            {
                ["trace_id"] = act.TraceId.ToString(),
                ["span_id"] = act.SpanId.ToString(),
                ["trace_flags"] = act.ActivityTraceFlags.ToString()
            };

            scope = logger.BeginScope(dict);
        }

        return new ActivityScope(act, scope);
    }

    // Convenience overload defaulting to Internal kind and no tags
    public static ActivityScope StartActivityScope(ActivitySource activitySource, ILogger logger, string name)
    {
        return StartActivityScope(activitySource, logger, name, ActivityKind.Internal);
    }

    public sealed class ActivityScope(Activity? activity, IDisposable? scope) : IDisposable
    {
        public void Dispose()
        {
            try
            {
                scope?.Dispose();
            }
            finally
            {
                try
                {
                    activity?.Stop();
                }
                catch
                {
                }
            }
        }

        public void SetTag(string key, object? value)
        {
            try
            {
                activity?.SetTag(key, value);
            }
            catch
            {
            }
        }
    }
}