// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.AspNetCore.Builder;
using Microsoft.Health.Fhir.Api.Features.Events;

namespace Microsoft.Health.Fhir.Api.Features.ApiNotifications
{
    public static class EventMiddlewareExtensions
    {
        public static IApplicationBuilder UseEventsMiddleware(
            this IApplicationBuilder builder)
        {
            EnsureArg.IsNotNull(builder, nameof(builder));

            return builder.UseMiddleware<EventMiddleware>();
        }
    }
}
