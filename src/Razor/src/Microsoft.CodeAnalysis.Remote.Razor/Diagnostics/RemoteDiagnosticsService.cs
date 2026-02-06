// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Razor.PooledObjects;
using Microsoft.CodeAnalysis.ExternalAccess.Razor;
using Microsoft.CodeAnalysis.Razor.Diagnostics;
using Microsoft.CodeAnalysis.Razor.Protocol;
using Microsoft.CodeAnalysis.Razor.Remote;
using Microsoft.CodeAnalysis.Remote.Razor.ProjectSystem;

namespace Microsoft.CodeAnalysis.Remote.Razor;

internal sealed class RemoteDiagnosticsService(in ServiceArgs args) : RazorDocumentServiceBase(in args), IRemoteDiagnosticsService
{
    internal sealed class Factory : FactoryBase<IRemoteDiagnosticsService>
    {
        protected override IRemoteDiagnosticsService CreateService(in ServiceArgs args)
            => new RemoteDiagnosticsService(in args);
    }

    private readonly RazorTranslateDiagnosticsService _translateDiagnosticsService = args.ExportProvider.GetExportedValue<RazorTranslateDiagnosticsService>();

    public ValueTask<DiagnosticsResult> GetDiagnosticsAsync(
        JsonSerializableRazorPinnedSolutionInfoWrapper solutionInfo,
        JsonSerializableDocumentId documentId,
        LspDiagnostic[] csharpDiagnostics,
        LspDiagnostic[] htmlDiagnostics,
        string? previousRazorChecksum,
        CancellationToken cancellationToken)
        => RunServiceAsync(
            solutionInfo,
            documentId,
            context => GetDiagnosticsAsync(context, csharpDiagnostics, htmlDiagnostics, previousRazorChecksum, cancellationToken),
            cancellationToken);

    private async ValueTask<DiagnosticsResult> GetDiagnosticsAsync(
        RemoteDocumentContext context,
        LspDiagnostic[] csharpDiagnostics,
        LspDiagnostic[] htmlDiagnostics,
        string? previousRazorChecksum,
        CancellationToken cancellationToken)
    {
        var codeDocument = await context.GetCodeDocumentAsync(cancellationToken).ConfigureAwait(false);

        // Use document content checksum for cache validation, consistent with HtmlDocumentSynchronizer
        var checksum = await context.TextDocument.GetChecksumAsync(cancellationToken).ConfigureAwait(false);
        var currentRazorChecksum = checksum.ToString();

        // Only compute Razor diagnostics if the document has changed
        ImmutableArray<LspDiagnostic> translatedRazorDiagnostics;
        if (previousRazorChecksum == currentRazorChecksum)
        {
            // Document unchanged - caller should use cached value
            translatedRazorDiagnostics = [];
        }
        else
        {
            translatedRazorDiagnostics = [.. RazorDiagnosticHelper.Convert(codeDocument.GetRequiredCSharpDocument().Diagnostics, codeDocument.Source.Text, context.Snapshot)];
        }

        // Translate C# and HTML diagnostics (always needed unless caller passes empty arrays)
        ImmutableArray<LspDiagnostic> translatedCSharpDiagnostics = csharpDiagnostics.Length > 0
            ? [.. await _translateDiagnosticsService.TranslateAsync(RazorLanguageKind.CSharp, csharpDiagnostics, context.Snapshot, cancellationToken).ConfigureAwait(false)]
            : [];

        ImmutableArray<LspDiagnostic> translatedHtmlDiagnostics = htmlDiagnostics.Length > 0
            ? [.. await _translateDiagnosticsService.TranslateAsync(RazorLanguageKind.Html, htmlDiagnostics, context.Snapshot, cancellationToken).ConfigureAwait(false)]
            : [];

        return new DiagnosticsResult
        {
            CSharpDiagnostics = translatedCSharpDiagnostics,
            HtmlDiagnostics = translatedHtmlDiagnostics,
            RazorDiagnostics = translatedRazorDiagnostics,
            RazorDiagnosticsChecksum = currentRazorChecksum
        };
    }

    public ValueTask<ImmutableArray<LspDiagnostic>> GetTaskListDiagnosticsAsync(
        JsonSerializableRazorPinnedSolutionInfoWrapper solutionInfo,
        JsonSerializableDocumentId documentId,
        ImmutableArray<string> taskListDescriptors,
        LspDiagnostic[] csharpTaskItems,
        CancellationToken cancellationToken)
        => RunServiceAsync(
            solutionInfo,
            documentId,
            context => GetTaskListDiagnosticsAsync(context, taskListDescriptors, csharpTaskItems, cancellationToken),
            cancellationToken);

    private async ValueTask<ImmutableArray<LspDiagnostic>> GetTaskListDiagnosticsAsync(
        RemoteDocumentContext context,
        ImmutableArray<string> taskListDescriptors,
        LspDiagnostic[] csharpTaskItems,
        CancellationToken cancellationToken)
    {
        var codeDocument = await context.GetCodeDocumentAsync(cancellationToken).ConfigureAwait(false);

        using var diagnostics = new PooledArrayBuilder<LspDiagnostic>();
        diagnostics.AddRange(TaskListDiagnosticProvider.GetTaskListDiagnostics(codeDocument, taskListDescriptors));
        diagnostics.AddRange(_translateDiagnosticsService.MapDiagnostics(RazorLanguageKind.CSharp, csharpTaskItems, context.Snapshot, codeDocument));
        return diagnostics.ToImmutableAndClear();
    }
}
