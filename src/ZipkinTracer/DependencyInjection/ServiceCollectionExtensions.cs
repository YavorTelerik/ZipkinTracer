using System;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ZipkinTracer.Internal;
using ZipkinTracer.Models;
using ZipkinTracer.Http;

namespace ZipkinTracer.DependencyInjection
{
    public static class ServiceCollectionExtensions
    {
        public static void AddZipkinTracer(this IServiceCollection services, ZipkinConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            var maxSize = config.MaxQueueSize <= 0 ? 100 : config.MaxQueueSize;

            services.AddSingleton(config);
            services.AddSingleton(new BlockingCollection<Span>(maxSize));
            services.AddSingleton<IServiceEndpoint, ServiceEndpoint>();
            services.AddSingleton<ISpanProcessorTask, SpanProcessorTask>();
            services.AddSingleton<ISpanProcessor, SpanProcessor>();
            services.AddSingleton<ITraceInfoAccessor, TraceInfoAccessor>();

            services.AddScoped<ISpanCollector, SpanCollector>();
            services.AddScoped<IZipkinTracer, ZipkinClient>();
            services.AddScoped<ISpanTracer, SpanTracer>();
        }
    }
}