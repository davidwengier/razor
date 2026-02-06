// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.VisualStudio.Razor.LanguageClient.Cohost;

/// <summary>
/// Represents a cached diagnostics entry for a single document.
/// Stores post-translation, post-filtering diagnostics that are ready to send to the client.
/// </summary>
/// <remarks>
/// The cache stores final diagnostics (already translated to Razor document coordinates and filtered).
/// When reusing cached diagnostics, no OOP round-trip is needed - we can directly combine and return them.
/// </remarks>
internal sealed record DiagnosticsCacheEntry
{
    /// <summary>
    /// Our composite resultId sent to the client (e.g., "razor-abc123").
    /// This is the key clients use to reference this cache entry.
    /// </summary>
    public required string ResultId { get; init; }

    /// <summary>
    /// Document version at the time this cache entry was created.
    /// Used to invalidate the cache when the document content changes.
    /// </summary>
    public required DiagnosticsCacheDocumentVersion DocumentVersion { get; init; }

    /// <summary>
    /// Roslyn's resultId from the last C# diagnostics call.
    /// Passed back to Roslyn on subsequent requests to enable their caching.
    /// </summary>
    public string? CSharpResultId { get; init; }

    /// <summary>
    /// Translated and filtered C# diagnostics, ready to send to client.
    /// </summary>
    public System.Collections.Immutable.ImmutableArray<LspDiagnostic> CachedCSharpDiagnostics { get; init; } = [];

    /// <summary>
    /// WebTools' resultId from the last HTML diagnostics call.
    /// Passed back to WebTools on subsequent requests to enable their caching.
    /// </summary>
    public string? HtmlResultId { get; init; }

    /// <summary>
    /// Translated and filtered HTML diagnostics, ready to send to client.
    /// </summary>
    public System.Collections.Immutable.ImmutableArray<LspDiagnostic> CachedHtmlDiagnostics { get; init; } = [];

    /// <summary>
    /// Checksum of the Razor diagnostics source (code document).
    /// Used to detect when Razor-specific diagnostics have changed.
    /// </summary>
    public string? RazorDiagnosticsChecksum { get; init; }

    /// <summary>
    /// Translated Razor diagnostics, ready to send to client.
    /// </summary>
    public System.Collections.Immutable.ImmutableArray<LspDiagnostic> CachedRazorDiagnostics { get; init; } = [];

    /// <summary>
    /// Combines all cached diagnostics into a single array for response construction.
    /// </summary>
    public System.Collections.Immutable.ImmutableArray<LspDiagnostic> GetAllDiagnostics()
        => [.. CachedCSharpDiagnostics, .. CachedHtmlDiagnostics, .. CachedRazorDiagnostics];
}
