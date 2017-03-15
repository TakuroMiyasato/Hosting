// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Microsoft.AspNetCore.Hosting.Internal
{
    public class ActivityTrackingOptions
    {
        public string RequestIdHeaderName { get; set; } = "Request-Id";

        public string BaggageHeaderName { get; set; } = "Correlation-Context";
    }
}