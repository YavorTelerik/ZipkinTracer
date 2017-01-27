﻿using System;
using Microsoft.AspNetCore.Http;
using ZipkinTracer.Helpers;

namespace ZipkinTracer
{
    /// <summary>
    /// TraceProvider class
    /// </summary>
    internal class TraceProvider : ITraceProvider
    {
        public const string TraceIdHeaderName = "X-B3-TraceId";
        public const string SpanIdHeaderName = "X-B3-SpanId";
        public const string ParentSpanIdHeaderName = "X-B3-ParentSpanId";
        public const string SampledHeaderName = "X-B3-Sampled";

        private const string Key = "ZipkinTracer.TraceProvider";

        /// <summary>
        /// Gets a TraceId
        /// </summary>
        public string TraceId { get; }

        /// <summary>
        /// Gets a SpanId
        /// </summary>
        public string SpanId { get; }

        /// <summary>
        /// Gets a ParentSpanId
        /// </summary>
        public string ParentSpanId { get; }

        /// <summary>
        /// Gets IsSampled
        /// </summary>
        public bool IsSampled { get; }

        /// <summary>
        /// Initializes a new instance of the TraceProvider class.
        /// </summary>
        /// <param name="config">ZipkinConfig instance</param>
        /// <param name="contextAccessor">httpContextAccessor</param>
        public TraceProvider(ZipkinConfig config, IHttpContextAccessor contextAccessor)
        {
            string headerTraceId = null;
            string headerSpanId = null;
            string headerParentSpanId = null;
            string headerSampled = null;
            string requestPath = null;

            var context = contextAccessor.HttpContext;

            if (context != null)
            {
                object value;
                if (context.Items.TryGetValue(Key, out value))
                {
                    // set properties from context's item.
                    var provider = (ITraceProvider)value;
                    TraceId = provider.TraceId;
                    SpanId = provider.SpanId;
                    ParentSpanId = provider.ParentSpanId;
                    IsSampled = provider.IsSampled;
                    return;
                }

                // zipkin use the following X-Headers to propagate the trace information
                headerTraceId = context.Request.Headers[TraceIdHeaderName];
                headerSpanId = context.Request.Headers[SpanIdHeaderName];
                headerParentSpanId = context.Request.Headers[ParentSpanIdHeaderName];
                headerSampled = context.Request.Headers[SampledHeaderName];

                requestPath = context.Request.Path.ToString();
            }
            
            TraceId = headerTraceId.IsParsableTo128Or64Bit() ? headerTraceId : GenerateNewTraceId(config.Create128BitTraceId);
            SpanId = headerSpanId.IsParsableToLong() ? headerSpanId : GenerateHexEncodedInt64Id();
            ParentSpanId = headerParentSpanId.IsParsableToLong() ? headerParentSpanId : string.Empty;
            IsSampled = config.ShouldBeSampled(headerSampled, requestPath);
            
            if (SpanId == ParentSpanId)
            {
                throw new ArgumentException("x-b3-SpanId and x-b3-ParentSpanId must not be the same value.");
            }

            context?.Items.Add(Key, this);
        }

        /// <summary>
        /// private constructor to accept property values
        /// </summary>
        /// <param name="traceId"></param>
        /// <param name="spanId"></param>
        /// <param name="parentSpanId"></param>
        /// <param name="isSampled"></param>
        private TraceProvider(string traceId, string spanId, string parentSpanId, bool isSampled)
        {
            TraceId = traceId;
            SpanId = spanId;
            ParentSpanId = parentSpanId;
            IsSampled = isSampled;
        }

        /// <summary>
        /// Gets a Trace for outgoing HTTP request.
        /// </summary>
        /// <returns>The trace</returns>
        public ITraceProvider GetNext()
        {
            return new TraceProvider(
                TraceId,
                GenerateHexEncodedInt64Id(),
                SpanId,
                IsSampled);
        }

        /// <summary>
        /// Generate a traceId.
        /// </summary>
        /// <param name="create128Bit">true for 128bit, false for 64 bit</param>
        /// <returns></returns>
        private string GenerateNewTraceId(bool create128Bit)
        {
            return create128Bit ? Guid.NewGuid().ToString("N") : GenerateHexEncodedInt64Id();
        }

        /// <summary>
        /// Generate a hex encoded Int64 from new Guid.
        /// </summary>
        /// <returns>The hex encoded int64</returns>
        private string GenerateHexEncodedInt64Id()
        {
            return Convert.ToString(BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 0), 16);
        }
    }
}