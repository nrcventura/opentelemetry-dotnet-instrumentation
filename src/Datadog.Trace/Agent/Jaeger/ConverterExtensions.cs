using System;
using System.Collections.Generic;
using Datadog.Trace.Internal;

namespace Datadog.Trace.Agent.Jaeger
{
    internal static class ConverterExtensions
    {
        private const int DaysPerYear = 365;

        // Number of days in 4 years
        private const int DaysPer4Years = (DaysPerYear * 4) + 1;       // 1461

        // Number of days in 100 years
        private const int DaysPer100Years = (DaysPer4Years * 25) - 1;  // 36524

        // Number of days in 400 years
        private const int DaysPer400Years = (DaysPer100Years * 4) + 1; // 146097

        // Number of days from 1/1/0001 to 12/31/1969
        private const int DaysTo1970 = (DaysPer400Years * 4) + (DaysPer100Years * 3) + (DaysPer4Years * 17) + DaysPerYear; // 719,162

        private const long UnixEpochTicks = DaysTo1970 * TimeSpan.TicksPerDay;
        private const long TicksPerMicrosecond = TimeSpan.TicksPerMillisecond / 1000;
        private const long UnixEpochMicroseconds = UnixEpochTicks / TicksPerMicrosecond; // 62,135,596,800,000,000

        public static JaegerSpan ToJaegerSpan(this Span span)
        {
            return new JaegerSpan(
                traceIdLow: span.TraceId.Lower,
                traceIdHigh: span.TraceId.Higher,
                spanId: (long)span.SpanId,
                parentSpanId: (long)(span.Context.ParentId ?? default),
                operationName: span.OperationName,
                flags: 0x1,
                startTime: ToEpochMicroseconds(span.StartTime.UtcDateTime),
                duration: (long)span.Duration.TotalMilliseconds * 1000,
                references: default,
                tags: span.ToJaegerTags(),
                logs: default);
        }

        public static long ToEpochMicroseconds(this DateTime utcDateTime)
        {
            // Truncate sub-microsecond precision before offsetting by the Unix Epoch to avoid
            // the last digit being off by one for dates that result in negative Unix times
            long microseconds = utcDateTime.Ticks / TicksPerMicrosecond;
            return microseconds - UnixEpochMicroseconds;
        }

        public static PooledList<JaegerTag> ToJaegerTags(this Span span)
        {
            var list = PooledList<JaegerTag>.Create();

            foreach (var item in span.Tags.GetAllTags())
            {
                PooledList<JaegerTag>.Add(ref list, ToJaegerTag(new KeyValuePair<string, object>(item.Key, item.Value)));
            }

            // report span status according to https://github.com/open-telemetry/opentelemetry-specification/blob/59bbfb781bb403902e7be79966a6576c47eb704b/specification/trace/sdk_exporters/jaeger.md#status
            switch (span.Status.StatusCode)
            {
                case StatusCode.Ok:
                    PooledList<JaegerTag>.Add(ref list, new JaegerTag("otel.status_code", JaegerTagType.STRING, vStr: "OK"));
                    break;
                case StatusCode.Error:
                    PooledList<JaegerTag>.Add(ref list, new JaegerTag("error", JaegerTagType.BOOL, vBool: true));
                    PooledList<JaegerTag>.Add(ref list, new JaegerTag("otel.status_code", JaegerTagType.STRING, vStr: "ERROR"));
                    var description = span.Status.Description ?? string.Empty;
                    PooledList<JaegerTag>.Add(ref list, new JaegerTag("otel.status_description", JaegerTagType.STRING, vStr: description));
                    break;
            }

            return list;
        }

        public static JaegerTag ToJaegerTag(this KeyValuePair<string, object> attribute)
        {
            return attribute.Value switch
            {
                string s => new JaegerTag(attribute.Key, JaegerTagType.STRING, vStr: s),
                int i => new JaegerTag(attribute.Key, JaegerTagType.LONG, vLong: Convert.ToInt64(i)),
                long l => new JaegerTag(attribute.Key, JaegerTagType.LONG, vLong: l),
                float f => new JaegerTag(attribute.Key, JaegerTagType.DOUBLE, vDouble: Convert.ToDouble(f)),
                double d => new JaegerTag(attribute.Key, JaegerTagType.DOUBLE, vDouble: d),
                bool b => new JaegerTag(attribute.Key, JaegerTagType.BOOL, vBool: b),
                _ => new JaegerTag(attribute.Key, JaegerTagType.STRING, vStr: attribute.Value.ToString()),
            };
        }
    }
}
