// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel.Composition;
using Microsoft.VisualStudio.LanguageServer.ContainedLanguage;
using Microsoft.VisualStudio.Utilities;

namespace Microsoft.VisualStudio.Razor.LanguageClient.Cohost;

/// <summary>
/// Listens for document close events and invalidates the diagnostics cache for the closed document.
/// This ensures we don't hold onto stale diagnostics for documents that are no longer open.
/// </summary>
[ContentType(RazorConstants.RazorLSPContentTypeName)]
[Export(typeof(LSPDocumentChangeListener))]
[method: ImportingConstructor]
internal sealed partial class DiagnosticsCacheDocumentRemoveListener(
    IDiagnosticsCacheService diagnosticsCacheService)
    : LSPDocumentChangeListener
{
    private readonly IDiagnosticsCacheService _diagnosticsCacheService = diagnosticsCacheService;

    public override void Changed(LSPDocumentSnapshot? old, LSPDocumentSnapshot? @new, VirtualDocumentSnapshot? virtualOld, VirtualDocumentSnapshot? virtualNew, LSPDocumentChangeKind kind)
    {
        if (kind == LSPDocumentChangeKind.Removed && old is not null)
        {
            _diagnosticsCacheService.DocumentRemoved(old.Uri);
        }
    }
}
