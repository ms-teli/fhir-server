// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Api.Features.Events
{
    /// <summary>
    /// Event to emit.
    /// </summary>
    public class Event
    {
        /// <summary>
        /// The FHIR operation being performed.
        /// </summary>
        public string Property { get; set; }
    }
}
