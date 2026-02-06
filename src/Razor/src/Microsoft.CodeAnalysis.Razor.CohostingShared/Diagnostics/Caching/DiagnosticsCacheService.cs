// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Razor.Logging;

namespace Microsoft.VisualStudio.Razor.LanguageClient.Cohost;

/// <summary>
/// Singleton service that caches post-translation, post-filtering diagnostics for Razor documents.
/// Uses a concurrent dictionary keyed by DocumentId, with entries containing the resultId mapping.
/// </summary>
/// <remarks>
/// This class is exported by platform-specific projects (VS uses MEF v1, VS Code uses MEF v2).
/// </remarks>
internal class DiagnosticsCacheService(ILoggerFactory loggerFactory) : IDiagnosticsCacheService
{
    private readonly ILogger _logger = loggerFactory.GetOrCreateLogger<DiagnosticsCacheService>();

    // Primary cache: DocumentId -> current cache entry
    private readonly ConcurrentDictionary<DocumentId, DiagnosticsCacheEntry> _documentCache = new();

    // Secondary index: ResultId -> DocumentId (for fast lookup by previousResultId)
    private readonly ConcurrentDictionary<string, DocumentId> _resultIdIndex = new();

    // URI index: URI -> DocumentId (for VS Code which uses URI-based document close)
    private readonly ConcurrentDictionary<Uri, DocumentId> _uriIndex = new();

    public async Task<DiagnosticsCacheEntry?> TryGetCachedEntryAsync(
        TextDocument razorDocument,
        string? previousResultId,
        CancellationToken cancellationToken)
    {
        if (previousResultId is null)
        {
            _logger.LogDebug($"No previousResultId provided, cache miss for {razorDocument.FilePath}");
            return null;
        }

        // Look up the document ID from the resultId index
        if (!_resultIdIndex.TryGetValue(previousResultId, out var documentId))
        {
            _logger.LogDebug($"ResultId {previousResultId} not found in index, cache miss");
            return null;
        }

        // Verify this is the same document
        if (documentId != razorDocument.Id)
        {
            _logger.LogDebug($"ResultId {previousResultId} maps to different document, cache miss");
            return null;
        }

        // Get the cache entry
        if (!_documentCache.TryGetValue(documentId, out var entry))
        {
            _logger.LogDebug($"No cache entry for document {razorDocument.FilePath}, cache miss");
            return null;
        }

        // Validate the document version hasn't changed
        var currentVersion = await DiagnosticsCacheDocumentVersion.CreateAsync(razorDocument, cancellationToken).ConfigureAwait(false);
        if (entry.DocumentVersion.HasContentChanged(currentVersion))
        {
            _logger.LogDebug($"Document {razorDocument.FilePath} content changed since cache entry, invalidating");
            InvalidateDocument(documentId);
            return null;
        }

        _logger.LogDebug($"Cache hit for {razorDocument.FilePath} with resultId {previousResultId}");
        return entry;
    }

    public void UpdateCache(DocumentId documentId, DiagnosticsCacheEntry entry)
    {
        // Remove old resultId from index if it exists and differs
        if (_documentCache.TryGetValue(documentId, out var oldEntry) &&
            oldEntry.ResultId != entry.ResultId)
        {
            _resultIdIndex.TryRemove(oldEntry.ResultId, out _);
        }

        // Update the primary cache
        _documentCache[documentId] = entry;

        // Update the secondary index
        _resultIdIndex[entry.ResultId] = documentId;

        // Update the URI index if the entry has a URI
        if (entry.DocumentVersion.DocumentUri is { } uri)
        {
            _uriIndex[uri] = documentId;
        }

        _logger.LogDebug($"Updated cache for document {documentId} with resultId {entry.ResultId}");
    }

    public void InvalidateDocument(DocumentId documentId)
    {
        if (_documentCache.TryRemove(documentId, out var entry))
        {
            _resultIdIndex.TryRemove(entry.ResultId, out _);
            if (entry.DocumentVersion.DocumentUri is { } uri)
            {
                _uriIndex.TryRemove(uri, out _);
            }

            _logger.LogDebug($"Invalidated cache for document {documentId}");
        }
    }

    public void DocumentRemoved(Uri uri)
    {
        if (_uriIndex.TryRemove(uri, out var documentId))
        {
            if (_documentCache.TryRemove(documentId, out var entry))
            {
                _resultIdIndex.TryRemove(entry.ResultId, out _);
                _logger.LogDebug($"Removed cache entry for document at {uri}");
            }
        }
        else
        {
            _logger.LogDebug($"No cache entry found for URI {uri}, nothing to remove");
        }
    }

    public void ClearAll()
    {
        var count = _documentCache.Count;
        _documentCache.Clear();
        _resultIdIndex.Clear();
        _uriIndex.Clear();
        _logger.LogDebug($"Cleared all {count} cached diagnostics entries");
    }
}
