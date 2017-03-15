// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting.Internal;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.AspNetCore.Hosting.Tests
{
    public class HostingApplicationTests
    {
        [Fact]
        public void DisposeContextDoesNotThrowWhenContextScopeIsNull()
        {
            // Arrange
            var hostingApplication = CreateApplication(out var features);
            var context = hostingApplication.CreateContext(features);

            // Act/Assert
            hostingApplication.DisposeContext(context, null);
        }

#if NETCOREAPP2_0
        [Fact]
        public void ActivityIsAvailibleDuringBeginRequestCall()
        {
            var diagnosticSource = new DiagnosticListener("DummySource");
            var hostingApplication = CreateApplication(out var features, diagnosticSource: diagnosticSource);

            bool startCalled = false;
            diagnosticSource.Subscribe(new CallbackDiagnosticListener(pair =>
            {
                if (pair.Key == "Microsoft.AspNetCore.Hosting.BeginRequest")
                {
                    startCalled = true;
                    Assert.NotNull(Activity.Current);
                    Assert.Equal("Microsoft.AspNetCore.Hosting.Activity", Activity.Current.OperationName);
                }
            }));

            hostingApplication.CreateContext(features);
            Assert.True(startCalled);
        }

        [Fact]
        public void ActivityIsAvailibleDuringRequest()
        {
            var hostingApplication = CreateApplication(out var features);

            hostingApplication.CreateContext(features);

            Assert.NotNull(Activity.Current);
            Assert.Equal("Microsoft.AspNetCore.Hosting.Activity", Activity.Current.OperationName);
        }

        [Fact]
        public void ActivityParentIdAndBaggeReadFromHeaders()
        {
            var hostingApplication = CreateApplication(out var features);

            features.Set<IHttpRequestFeature>(new HttpRequestFeature()
            {
                Headers = new HeaderDictionary()
                {
                    { "Request-Id", "ParentId1" },
                    { "Correlation-Context", "Key1=value1, Key2=value2" }
                }
            });
            hostingApplication.CreateContext(features);
            Assert.Equal("Microsoft.AspNetCore.Hosting.Activity", Activity.Current.OperationName);
            Assert.Equal("ParentId1", Activity.Current.ParentId);
            Assert.Contains(Activity.Current.Baggage, pair => pair.Key == "Key1" && pair.Value == "value1");
            Assert.Contains(Activity.Current.Baggage, pair => pair.Key == "Key2" && pair.Value == "value2");
        }

        [Fact]
        public void ActivityParentIdAndBaggeReadFromHeadersWithCustomizedName()
        {
            var hostingApplication = CreateApplication(out var features, new ActivityTrackingOptions() { BaggageHeaderName = "BGG", RequestIdHeaderName = "RID"});

            features.Set<IHttpRequestFeature>(new HttpRequestFeature()
            {
                Headers = new HeaderDictionary
                {
                    { "RID", "ParentId1" },
                    { "BGG", "Key1=value1, Key2=value2" }
                }
            });
            hostingApplication.CreateContext(features);
            Assert.Equal("Microsoft.AspNetCore.Hosting.Activity", Activity.Current.OperationName);
            Assert.Equal("ParentId1", Activity.Current.ParentId);
            foreach (var c in Activity.Current.Baggage)
            {
                Console.WriteLine(c);
            }
            Assert.Contains(Activity.Current.Baggage, pair => pair.Key == "Key1" && pair.Value == "value1");
            Assert.Contains(Activity.Current.Baggage, pair => pair.Key == "Key2" && pair.Value == "value2");
        }
#endif

        private static HostingApplication CreateApplication(out FeatureCollection features,
            ActivityTrackingOptions trackingOptions = null,
            DiagnosticSource diagnosticSource = null)
        {
            var httpContextFactory = new HttpContextFactory(
                new DefaultObjectPoolProvider(),
                Options.Create(new FormOptions()),
                new HttpContextAccessor());

            var hostingApplication = new HostingApplication(
                ctx => Task.FromResult(0),
                new NullScopeLogger(),
                diagnosticSource ?? new NoopDiagnosticSource(),
                httpContextFactory,
                Options.Create(trackingOptions ?? new ActivityTrackingOptions()));

            features = new FeatureCollection();
            features.Set<IHttpRequestFeature>(new HttpRequestFeature());

            return hostingApplication;
        }

        private class NullScopeLogger : ILogger
        {
            public IDisposable BeginScope<TState>(TState state) => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            {
            }
        }

        private class NoopDiagnosticSource : DiagnosticSource
        {
            public override bool IsEnabled(string name) => true;

            public override void Write(string name, object value)
            {
            }
        }

        private class CallbackDiagnosticListener : IObserver<KeyValuePair<string, object>>
        {
            private readonly Action<KeyValuePair<string, object>> _callback;

            public CallbackDiagnosticListener(Action<KeyValuePair<string, object>> callback)
            {
                _callback = callback;
            }

            public void OnNext(KeyValuePair<string, object> value)
            {
                _callback(value);
            }

            public void OnError(Exception error)
            {
            }

            public void OnCompleted()
            {
            }
        }
    }
}
