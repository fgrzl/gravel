using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Gravel.Telemetry;

/// <summary>
///     Helper methods for starting and managing telemetry activities and scopes.
/// </summary>
public static class TelemetryHelper
{
    /// <summary>
    ///     Starts a telemetry activity and logging scope, attaching tags and returning an <see cref="ActivityScope" /> for
    ///     disposal.
    /// </summary>
    /// <param name="activitySource">The activity source to start the activity from.</param>
    /// <param name="logger">The logger to use for scope creation.</param>
    /// <param name="name">The name of the activity.</param>
    /// <param name="kind">The kind of activity.</param>
    /// <param name="tags">Optional tags to attach to the activity.</param>
    /// <returns>An <see cref="ActivityScope" /> that manages the activity and scope lifetime.</returns>
    public static ActivityScope StartActivityScope(
        ActivitySource activitySource, ILogger logger, string name,
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

    /// <summary>
    ///     Starts a telemetry activity and logging scope with <see cref="ActivityKind.Internal" /> and no tags.
    /// </summary>
    /// <param name="activitySource">The activity source to start the activity from.</param>
    /// <param name="logger">The logger to use for scope creation.</param>
    /// <param name="name">The name of the activity.</param>
    /// <returns>An <see cref="ActivityScope" /> that manages the activity and scope lifetime.</returns>
    public static ActivityScope StartActivityScope(ActivitySource activitySource, ILogger logger, string name)
    {
        return StartActivityScope(activitySource, logger, name, ActivityKind.Internal);
    }

    /// <summary>
    ///     Manages the lifetime of a telemetry activity and logging scope.
    /// </summary>
    public sealed class ActivityScope(Activity? activity, IDisposable? scope) : IDisposable
    {
        /// <summary>
        ///     Disposes the scope and stops the activity.
        /// </summary>
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

        /// <summary>
        ///     Sets a tag on the underlying activity.
        /// </summary>
        /// <param name="key">The tag key.</param>
        /// <param name="value">The tag value.</param>
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
