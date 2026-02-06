// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.ExternalAccess.Razor;

namespace Microsoft.CodeAnalysis.Razor.Remote;

internal interface IRemoteDiagnosticsService : IRemoteJsonService
{
    /// <summary>
    /// Gets diagnostics with per-source results to enable partial caching.
    /// When a source (C#, HTML, or Razor) hasn't changed, devenv can reuse cached translated diagnostics.
    /// </summary>
    /// <param name="solutionInfo">Solution info for the request.</param>
    /// <param name="documentId">The Razor document ID.</param>
    /// <param name="csharpDiagnostics">Raw C# diagnostics to translate (empty if using cached).</param>
    /// <param name="htmlDiagnostics">Raw HTML diagnostics to translate (empty if using cached).</param>
    /// <param name="previousRazorChecksum">Content checksum from previous call; if matches current, Razor diagnostics are unchanged.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Per-source translated diagnostics with content checksum for future caching.</returns>
    ValueTask<DiagnosticsResult> GetDiagnosticsAsync(
        JsonSerializableRazorPinnedSolutionInfoWrapper solutionInfo,
        JsonSerializableDocumentId documentId,
        LspDiagnostic[] csharpDiagnostics,
        LspDiagnostic[] htmlDiagnostics,
        string? previousRazorChecksum,
        CancellationToken cancellationToken);

    ValueTask<ImmutableArray<LspDiagnostic>> GetTaskListDiagnosticsAsync(
        JsonSerializableRazorPinnedSolutionInfoWrapper solutionInfo,
        JsonSerializableDocumentId documentId,
        ImmutableArray<string> taskListDescriptors,
        LspDiagnostic[] csharpTaskItems,
        CancellationToken cancellationToken);
}
