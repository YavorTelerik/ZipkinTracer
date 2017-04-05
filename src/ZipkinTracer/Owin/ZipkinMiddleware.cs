﻿using System;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Http.Internal;
using Microsoft.Extensions.DependencyInjection;
using ZipkinTracer.Internal;
using ZipkinTracer.Http;
using ZipkinTracer.Helpers;
using ZipkinTracer.Models;

namespace ZipkinTracer.Owin
{
    internal class ZipkinMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ZipkinConfig _zipkinConfig;
        private readonly ITraceInfoAccessor _traceInfoAccessor;

        public ZipkinMiddleware(RequestDelegate next, ISpanProcessor spanProcessor,
            ZipkinConfig zipkinConfig, ITraceInfoAccessor traceInfoAccessor)
        {
            if (spanProcessor == null) throw new ArgumentNullException(nameof(spanProcessor));
            if (zipkinConfig == null) throw new ArgumentNullException(nameof(zipkinConfig));
            if (traceInfoAccessor == null) throw new ArgumentNullException(nameof(traceInfoAccessor));

            _next = next;
            _zipkinConfig = zipkinConfig;
            _traceInfoAccessor = traceInfoAccessor;

            spanProcessor.Start();
        }

        public async Task Invoke(HttpContext context)
        {
            SetTraceInfoProperties(context);

            if (_zipkinConfig.Bypass != null && _zipkinConfig.Bypass(context.Request))
            {
                await _next(context);
                return;
            }

            string headerSpanName = context.Request.Headers[TraceInfo.SpanNameHeaderName];

            var spanName = !string.IsNullOrEmpty(headerSpanName)
                ? headerSpanName
                : $"{context.Request.Method} {context.Request.Path}";
            var traceClient = context.RequestServices.GetRequiredService<IZipkinTracer>();
            var span = await traceClient.StartServerTrace(new Uri(context.Request.GetEncodedUrl()), spanName);

            context.Response.OnStarting(
                response =>
                {
                    var httpResponse = response as HttpResponse;
                    if (httpResponse != null)
                    {
                        traceClient.EndServerTrace(span, httpResponse.StatusCode,
                            IsErrorStatusCode(httpResponse.StatusCode)
                                ? ((HttpStatusCode) httpResponse.StatusCode).ToString()
                                : null);
                    }
                    return Task.CompletedTask;
                }, context.Response);

            await _next(context);
        }

        private void SetTraceInfoProperties(HttpContext context)
        {
            string headerTraceId = context.Request.Headers[TraceInfo.TraceIdHeaderName];
            string headerSpanId = context.Request.Headers[TraceInfo.SpanIdHeaderName];
            string headerParentSpanId = context.Request.Headers[TraceInfo.ParentSpanIdHeaderName];
            string headerSampled = context.Request.Headers[TraceInfo.SampledHeaderName];
            var requestPath = context.Request.Path.ToString();

            var traceId = headerTraceId.IsParsableTo128Or64Bit()
                ? headerTraceId
                : TraceIdHelper.GenerateNewTraceId(_zipkinConfig.Create128BitTraceId);
            var spanId = headerSpanId.IsParsableToLong() ? headerSpanId : TraceIdHelper.GenerateHexEncodedInt64Id();
            var parentSpanId = headerParentSpanId.IsParsableToLong() ? headerParentSpanId : string.Empty;
            var isSampled = _zipkinConfig.ShouldBeSampled(headerSampled, requestPath);
            var domain = _zipkinConfig.Domain(context.Request);

            var traceInfo = new TraceInfo(traceId, spanId, isSampled, domain, parentSpanId);
            _traceInfoAccessor.TraceInfo = traceInfo;

            context.Items[TraceInfo.TraceInfoKey] = traceInfo;
        }

        private static bool IsErrorStatusCode(int statusCode)
        {
            return statusCode >= 400 && statusCode <= 599;
        }
    }

    public static class AppBuilderExtensions
    {
        public static void UseZipkinTracer(this IApplicationBuilder app)
        {
            app.UseMiddleware<ZipkinMiddleware>();
        }
    }
}