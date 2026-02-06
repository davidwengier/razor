// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel.Composition;
using Microsoft.CodeAnalysis.Razor.Logging;
using Microsoft.VisualStudio.Razor.LanguageClient.Cohost;

namespace Microsoft.VisualStudio.LanguageServices.Razor;

/// <summary>
/// Visual Studio MEF v1 export of the diagnostics cache service.
/// </summary>
[Export(typeof(IDiagnosticsCacheService))]
[method: ImportingConstructor]
internal sealed class VisualStudioDiagnosticsCacheService(ILoggerFactory loggerFactory)
    : DiagnosticsCacheService(loggerFactory)
{
}
