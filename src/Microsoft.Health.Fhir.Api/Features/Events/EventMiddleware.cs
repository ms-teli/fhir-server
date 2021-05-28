// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.EventGrid;
using Microsoft.Azure.EventGrid.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Extensions;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Context;

namespace Microsoft.Health.Fhir.Api.Features.Events
{
    public class EventMiddleware : IMiddleware
    {
        private readonly IFhirRequestContextAccessor _fhirRequestContextAccessor;
        private readonly IMediator _mediator;
        private readonly ILogger<EventMiddleware> _logger;

        private readonly string _topicUrl;
        private readonly string _topicAccessKey;

        public EventMiddleware(
            IFhirRequestContextAccessor fhirRequestContextAccessor,
            IMediator mediator,
            IOptions<OperationsConfiguration> operationsConfig,
            ILogger<EventMiddleware> logger)
        {
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _fhirRequestContextAccessor = fhirRequestContextAccessor;
            _mediator = mediator;
            _logger = logger;

            _topicUrl = operationsConfig.Value.Event.EventTopic;
            _topicAccessKey = operationsConfig.Value.Event.EventTopicAccess;
        }

        public async Task InvokeAsync(HttpContext context, RequestDelegate next)
        {
            EnsureArg.IsNotNull(context, nameof(context));
            EnsureArg.IsNotNull(next, nameof(next));

            await next(context);
            if (context.Request.IsFhirRequest())
            {
                // Don't emit events for internal calls.
                PublishEventsAsync(context);
            }
        }

        private void PublishEventsAsync(HttpContext context)
        {
            IFhirRequestContext fhirRequestContext = _fhirRequestContextAccessor.FhirRequestContext;

            // Can get from RP.
            string topicEndpoint = _topicUrl;
            string topicKey = _topicAccessKey;
            string topicHostname = new Uri(topicEndpoint).Host;

            if (!(context.Response.StatusCode == 200 || context.Response.StatusCode == 201 || context.Response.StatusCode == 204))
            {
                return;
            }

            if (!TryGetEventType(fhirRequestContext.AuditEventType, out string eventType))
            {
                return;
            }

            Console.WriteLine("Firing Event...");

#pragma warning disable CS1701 // Assuming assembly reference matches identity
            TopicCredentials topicCredentials = new TopicCredentials(topicKey);
            EventGridClient client = new EventGridClient(topicCredentials);

            List<EventGridEvent> eventsList = new List<EventGridEvent>
                {
                    new EventGridEvent()
                    {
                        Id = Guid.NewGuid().ToString(),
                        EventType = eventType,
                        EventTime = DateTime.Now,
                        Subject = fhirRequestContext.Uri.AbsoluteUri.ToString(),
                        DataVersion = "1.0",
                        Data = new Event()
                        {
                            Property = "property",
                        },
                    },
                };
            try
            {
                client.PublishEventsAsync(topicHostname, eventsList);
            }
            catch
            {
                Console.WriteLine("Error occurred when publishing the events.");
            }
#pragma warning restore CS1701 // Assuming assembly reference matches identity

            Console.WriteLine("Published events to Event Grid.");
        }

        private static bool TryGetEventType(string resourceOperationType, out string eventType)
        {
            eventType = string.Empty;
#pragma warning disable CA1308 // Normalize strings to uppercase
            switch (resourceOperationType.ToLowerInvariant())
#pragma warning restore CA1308 // Normalize strings to uppercase
            {
                case "create":
                    eventType = "Microsoft.Health.FHIR.ResourceCreated";
                    return true;
                case "update":
                    eventType = "Microsoft.Health.FHIR.ResourceUpdated";
                    return true;
                case "delete":
                    eventType = "Microsoft.Health.FHIR.ResourceDeleted";
                    return true;
                case "read":
                    return false;
                default:
                    return false;
            }
        }
    }
}