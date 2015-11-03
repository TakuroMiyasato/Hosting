﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.AspNet.Http;
using Microsoft.AspNet.Http.Features;

namespace Microsoft.AspNet.Hosting.Server
{
    /// <summary>
    /// Represents an HttpApplication.
    /// </summary>
    public interface IHttpApplication
    {
        /// <summary>
        /// Create an HttpContext given a collection of HTTP features.
        /// </summary>
        /// <param name="contextFeatures">A collection of HTTP features to be used for creating the HttpContext.</param>
        /// <returns>The created HttpContext.</returns>
        HttpContext CreateHttpContext(IFeatureCollection contextFeatures);

        /// <summary>
        /// Create the application's asynchronous operation that processes an HttpContext.
        /// </summary>
        /// <param name="httpContext">The HttpContext that the operation will process.</param>
        /// <returns>The asynchronous operation.</returns>
        Task InvokeAsync(HttpContext httpContext);

        /// <summary>
        /// Dispose the given HttpContext.
        /// </summary>
        /// <param name="httpContext">The HttpContext to be disposed.</param>
        void DisposeHttpContext(HttpContext httpContext);
    }
}