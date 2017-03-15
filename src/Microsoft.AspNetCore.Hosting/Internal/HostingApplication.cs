// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace Microsoft.AspNetCore.Hosting.Internal
{
    public class HostingApplication : IHttpApplication<HostingApplication.Context>
    {
#if NETSTANDARD2_0
        private const string ActivityName = "Microsoft.AspNetCore.Hosting.Activity";
#endif
        private const string DiagnosticsBeginRequestKey = "Microsoft.AspNetCore.Hosting.BeginRequest";
        private const string DiagnosticsEndRequestKey = "Microsoft.AspNetCore.Hosting.EndRequest";
        private const string DiagnosticsUnhandledExceptionKey = "Microsoft.AspNetCore.Hosting.UnhandledException";

        private static readonly double TimestampToTicks = TimeSpan.TicksPerSecond / (double)Stopwatch.Frequency;

        private readonly RequestDelegate _application;
        private readonly ILogger _logger;
        private readonly DiagnosticSource _diagnosticSource;
        private readonly IHttpContextFactory _httpContextFactory;
        private readonly ActivityTrackingOptions _activityTrackingOptions;

        public HostingApplication(
            RequestDelegate application,
            ILogger logger,
            DiagnosticSource diagnosticSource,
            IHttpContextFactory httpContextFactory,
            IOptions<ActivityTrackingOptions> activityTrackingOptions)
        {
            _application = application;
            _logger = logger;
            _diagnosticSource = diagnosticSource;
            _httpContextFactory = httpContextFactory;
            _activityTrackingOptions = activityTrackingOptions.Value;
        }

        // Set up the request
        public Context CreateContext(IFeatureCollection contextFeatures)
        {
            var context = new Context();
            var httpContext = _httpContextFactory.Create(contextFeatures);

            // These enabled checks are virtual dispatch and used twice and so cache to locals
            var diagnoticsEnabled = _diagnosticSource.IsEnabled(DiagnosticsBeginRequestKey);
            var loggingEnabled = _logger.IsEnabled(LogLevel.Information);

            if (HostingEventSource.Log.IsEnabled())
            {
                // To keep the hot path short we defer logging in this function to non-inlines
                RecordRequestStartEventLog(httpContext);
            }

            // Only make call GetTimestamp if its value will be used, i.e. of the listenters is enabled
            var startTimestamp = (diagnoticsEnabled || loggingEnabled) ? Stopwatch.GetTimestamp() : 0;

            // Scope may be relevant for a different level of logging, so we always create it
            // see: https://github.com/aspnet/Hosting/pull/944
            var scope = _logger.RequestScope(httpContext);

            if (loggingEnabled)
            {
                // Non-inline
                LogRequestStarting(httpContext);
            }
            if (diagnoticsEnabled)
            {
#if NETSTANDARD2_0
                context.Activity = StartActivity(httpContext);
#endif
                // Non-inline
                RecordBeginRequestDiagnostics(httpContext, startTimestamp);
            }

            // Fill and return the request Context
            context.HttpContext = httpContext;
            context.Scope = scope;
            context.StartTimestamp = startTimestamp;
            return context;
        }

#if NETSTANDARD2_0
        private Activity StartActivity(HttpContext httpContext)
        {
            var activity = new Activity(ActivityName);
            StringValues requestId;
            if (httpContext.Request.Headers.TryGetValue(_activityTrackingOptions.RequestIdHeaderName, out requestId))
            {
                try
                {
                    activity.SetParentId(requestId);
                }
                catch (ArgumentException ex)
                {
                    _logger.LogWarning(0, ex, "Request ID is invalid '{RequestId}", requestId);
                }

                // We expect baggage to be empty by default
                // Only very advanced users will be using it in near future, we encouradge them to keep baggage small (few items)
                string[] baggage = httpContext.Request.Headers.GetCommaSeparatedValues(_activityTrackingOptions.BaggageHeaderName);
                if (baggage != StringValues.Empty)
                {
                    foreach (var item in baggage)
                    {
                        NameValueHeaderValue baggageItem;
                        if (NameValueHeaderValue.TryParse(item, out baggageItem))
                        {
                            try
                            {
                                activity.AddBaggage(baggageItem.Name, baggageItem.Value);
                            }
                            catch (ArgumentException ex)
                            {
                                _logger.LogWarning(0, ex, "Invalid baggage item '{ItemName}' with value '{ItemValue}'", baggageItem.Name, baggageItem.Value);
                            }
                        }
                    }
                }
            }

            activity.Start();
            return activity;
        }
#endif

        // Execute the request
        public Task ProcessRequestAsync(Context context)
        {
            return _application(context.HttpContext);
        }

        // Clean up the request
        public void DisposeContext(Context context, Exception exception)
        {
            // Local cache items resolved multiple items, in order of use so they are primed in cpu pipeline when used
            var hostingEventLog = HostingEventSource.Log;
            var startTimestamp = context.StartTimestamp;
            var httpContext = context.HttpContext;
            var eventLogEnabled = hostingEventLog.IsEnabled();

            // If startTimestamp is 0, don't call GetTimestamp, likely don't need the value
            var currentTimestamp = (startTimestamp != 0) ? Stopwatch.GetTimestamp() : 0;

#if NETSTANDARD2_0
            // TODO: Ability to set duration directly or set end time from timestamp
            context.Activity?
                .SetEndTime(DateTime.UtcNow)
                .Stop();
#endif

            // To keep the hot path short we defer logging to non-inlines
            if (exception == null)
            {
                // No exception was thrown, request was sucessful
                if (_diagnosticSource.IsEnabled(DiagnosticsEndRequestKey))
                {
                    // Diagnostics is enabled for EndRequest, but it may not be for BeginRequest
                    // so call GetTimestamp if currentTimestamp is zero (from above)
                    RecordEndRequestDiagnostics(
                        httpContext, 
                        (currentTimestamp != 0) ? currentTimestamp : Stopwatch.GetTimestamp());
                }
            }
            else
            {
                // Exception was thrown from request
                if (_diagnosticSource.IsEnabled(DiagnosticsUnhandledExceptionKey))
                {
                    // Diagnostics is enabled for UnhandledException, but it may not be for BeginRequest 
                    // so call GetTimestamp if currentTimestamp is zero (from above)
                    RecordUnhandledExceptionDiagnostics(
                        httpContext, 
                        (currentTimestamp != 0) ? currentTimestamp : Stopwatch.GetTimestamp(), 
                        exception);
                }

                if (eventLogEnabled)
                {
                    // Non-inline
                    hostingEventLog.UnhandledException();
                }
            }

            // If startTimestamp was 0, then Information logging wasn't enabled at for this request (and calcuated time will be wildly wrong)
            // Is used as proxy to reduce calls to virtual: _logger.IsEnabled(LogLevel.Information)
            if (startTimestamp != 0)
            {
                // Non-inline
                LogRequestFinished(httpContext, startTimestamp, currentTimestamp);
            }

            // Logging Scope and context are finshed with
            context.Scope?.Dispose();
            _httpContextFactory.Dispose(httpContext);

            if (eventLogEnabled)
            {
                // Non-inline
                hostingEventLog.RequestStop();
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void LogRequestStarting(HttpContext httpContext)
        {
            // IsEnabled is checked in the caller, so if we are here just log
            _logger.Log(
                logLevel: LogLevel.Information,
                eventId: LoggerEventIds.RequestStarting,
                state: new HostingRequestStartingLog(httpContext),
                exception: null,
                formatter: HostingRequestStartingLog.Callback);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void LogRequestFinished(HttpContext httpContext, long startTimestamp, long currentTimestamp)
        {
            // IsEnabled isn't checked in the caller, startTimestamp > 0 is used as a fast proxy check
            // but that may be because diagnostics are enabled, which also uses startTimestamp, so check here
            if (_logger.IsEnabled(LogLevel.Information))
            {
                var elapsed = new TimeSpan((long)(TimestampToTicks * (currentTimestamp - startTimestamp)));

                _logger.Log(
                    logLevel: LogLevel.Information,
                    eventId: LoggerEventIds.RequestFinished,
                    state: new HostingRequestFinishedLog(httpContext, elapsed),
                    exception: null,
                    formatter: HostingRequestFinishedLog.Callback);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void RecordBeginRequestDiagnostics(HttpContext httpContext, long startTimestamp)
        {
            _diagnosticSource.Write(
                DiagnosticsBeginRequestKey,
                new
                {
                    httpContext = httpContext,
                    timestamp = startTimestamp
                });
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void RecordEndRequestDiagnostics(HttpContext httpContext, long currentTimestamp)
        {
            _diagnosticSource.Write(
                DiagnosticsEndRequestKey,
                new
                {
                    httpContext = httpContext,
                    timestamp = currentTimestamp
                });
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void RecordUnhandledExceptionDiagnostics(HttpContext httpContext, long currentTimestamp, Exception exception)
        {
            _diagnosticSource.Write(
                DiagnosticsUnhandledExceptionKey,
                new
                {
                    httpContext = httpContext,
                    timestamp = currentTimestamp,
                    exception = exception
                });
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void RecordRequestStartEventLog(HttpContext httpContext)
        {
            HostingEventSource.Log.RequestStart(httpContext.Request.Method, httpContext.Request.Path);
        }

        public struct Context
        {
            public HttpContext HttpContext { get; set; }
            public IDisposable Scope { get; set; }
            public long StartTimestamp { get; set; }

#if NETSTANDARD2_0
            public Activity Activity { get; set; }
#endif

        }
    }
}
