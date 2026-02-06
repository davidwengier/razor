// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;

namespace Microsoft.VisualStudio.Razor.LanguageClient.Cohost;

/// <summary>
/// Service that caches post-translation, post-filtering diagnostics for Razor documents.
/// The cache lives in devenv (not OOP) to avoid re-translation when reusing cached diagnostics.
/// </summary>
internal interface IDiagnosticsCacheService
{
    /// <summary>
    /// Tries to get a cached entry for the given previousResultId and validates the document version.
    /// Returns null if no entry exists or if the document content has changed since the cache was created.
    /// </summary>
    /// <param name="razorDocument">The current razor document to validate version against.</param>
    /// <param name="previousResultId">The resultId from the client's previous request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The cache entry if valid, or null if not found or document has changed.</returns>
    Task<DiagnosticsCacheEntry?> TryGetCachedEntryAsync(
        TextDocument razorDocument,
        string? previousResultId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Updates the cache with a new entry for the given document.
    /// </summary>
    /// <param name="documentId">The document ID to cache the entry for.</param>
    /// <param name="entry">The cache entry containing diagnostics and version info.</param>
    void UpdateCache(DocumentId documentId, DiagnosticsCacheEntry entry);

    /// <summary>
    /// Invalidates and removes any cached entry for the given document.
    /// Should be called when a document is closed or removed.
    /// </summary>
    /// <param name="documentId">The document ID to invalidate.</param>
    void InvalidateDocument(DocumentId documentId);

    /// <summary>
    /// Invalidates and removes any cached entry for the document at the given URI.
    /// Used by VS Code where DocumentId may not be readily available.
    /// </summary>
    /// <param name="uri">The URI of the document to invalidate.</param>
    void DocumentRemoved(Uri uri);

    /// <summary>
    /// Clears all cached diagnostics. Should be called when the solution is closed.
    /// </summary>
    void ClearAll();
}
