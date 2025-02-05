﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace Microsoft.CodeAnalysis.Razor.MapCode;

internal interface IMapCodeService
{
    Task<WorkspaceEdit?> HandleMappingsAsync(VSInternalMapCodeMapping[] mappings, Guid mapCodeCorrelationId, CancellationToken cancellationToken);
}
