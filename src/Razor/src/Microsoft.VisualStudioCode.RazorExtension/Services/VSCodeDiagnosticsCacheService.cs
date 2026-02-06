// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Composition;
using Microsoft.CodeAnalysis.Razor.Logging;
using Microsoft.VisualStudio.Razor.LanguageClient.Cohost;

namespace Microsoft.VisualStudioCode.RazorExtension.Services;

/// <summary>
/// VS Code MEF v2 export of the diagnostics cache service.
/// </summary>
[Shared]
[Export(typeof(IDiagnosticsCacheService))]
[method: ImportingConstructor]
internal sealed class VSCodeDiagnosticsCacheService(ILoggerFactory loggerFactory)
    : DiagnosticsCacheService(loggerFactory)
{
}
