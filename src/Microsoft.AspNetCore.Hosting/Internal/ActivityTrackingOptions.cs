// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Microsoft.AspNetCore.Hosting.Internal
{
    public class ActivityTrackingOptions
    {
        /// <summary>
        /// Gets or sets request header name to use as Parent ID for activity.
        /// </summary>
        public string RequestIdHeaderName { get; set; } = "Request-Id";

        /// <summary>
        /// Gets or sets request header name that contains comma separated list of baggage key-value pairs.
        /// </summary>
        public string BaggageHeaderName { get; set; } = "Correlation-Context";
    }
}