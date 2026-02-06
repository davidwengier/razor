// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Razor.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.ExternalAccess.Razor;
using Microsoft.CodeAnalysis.Razor.Cohost;
using Microsoft.CodeAnalysis.Razor.Logging;
using Microsoft.CodeAnalysis.Razor.Protocol;
using Microsoft.CodeAnalysis.Razor.Remote;
using Microsoft.CodeAnalysis.Razor.Telemetry;
using ExternalHandlers = Microsoft.CodeAnalysis.ExternalAccess.Razor.Cohost.Handlers;

namespace Microsoft.VisualStudio.Razor.LanguageClient.Cohost;

internal abstract class CohostDocumentPullDiagnosticsEndpointBase<TRequest, TResponse>(
    IIncompatibleProjectService incompatibleProjectService,
    IRemoteServiceInvoker remoteServiceInvoker,
    IHtmlRequestInvoker requestInvoker,
    IClientCapabilitiesService clientCapabilitiesService,
    IDiagnosticsCacheService diagnosticsCacheService,
    ITelemetryReporter telemetryReporter,
    ILogger logger)
    : AbstractCohostDocumentEndpoint<TRequest, TResponse>(incompatibleProjectService)
    where TRequest : notnull
{
    private readonly IRemoteServiceInvoker _remoteServiceInvoker = remoteServiceInvoker;
    private readonly IHtmlRequestInvoker _requestInvoker = requestInvoker;
    private readonly IClientCapabilitiesService _clientCapabilitiesService = clientCapabilitiesService;
    private readonly IDiagnosticsCacheService _diagnosticsCacheService = diagnosticsCacheService;
    private readonly ITelemetryReporter _telemetryReporter = telemetryReporter;
    private readonly ILogger _logger = logger;

    protected override bool MutatesSolutionState => false;

    protected override bool RequiresLSPSolution => true;

    protected abstract string LspMethodName { get; }
    protected abstract bool SupportsHtmlDiagnostics { get; }

    /// <summary>
    /// Extracts diagnostics from the HTML response.
    /// </summary>
    protected virtual LspDiagnostic[] ExtractHtmlDiagnostics(TResponse result)
    {
        throw new NotSupportedException("If SupportsHtmlDiagnostics is true, you must implement ExtractHtmlDiagnostics");
    }

    /// <summary>
    /// Extracts the result ID from the HTML response for caching.
    /// Returns null if the response doesn't contain a result ID.
    /// </summary>
    protected virtual string? ExtractHtmlResultId(TResponse result)
    {
        // Default implementation returns null - derived classes should override if HTML server supports result IDs
        return null;
    }

    /// <summary>
    /// Determines if the HTML response indicates "unchanged" (same result ID, no diagnostics need to be sent).
    /// </summary>
    protected virtual bool IsHtmlResponseUnchanged(TResponse result, string? previousResultId)
    {
        // Default: always consider changed. Derived classes can override for servers that support caching.
        return false;
    }

    protected virtual TRequest CreateHtmlParams(Uri uri, string? previousResultId)
    {
        throw new NotSupportedException("If SupportsHtmlDiagnostics is true, you must implement CreateHtmlParams");
    }

    /// <summary>
    /// Gets diagnostics with caching support. Returns a result that indicates whether diagnostics
    /// have changed or can be reused from the client's cache.
    /// </summary>
    /// <param name="razorDocument">The Razor document to get diagnostics for.</param>
    /// <param name="previousResultId">The resultId from the client's previous request, if any.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result containing diagnostics and a resultId, or an unchanged indicator.</returns>
    protected async Task<DiagnosticsRequestResult?> GetDiagnosticsAsync(
        TextDocument razorDocument,
        string? previousResultId,
        CancellationToken cancellationToken)
    {
        var correlationId = Guid.NewGuid();
        using var _ = _telemetryReporter.TrackLspRequest(LspMethodName, LanguageServerConstants.RazorLanguageServerName, TelemetryThresholds.DiagnosticsRazorTelemetryThreshold, correlationId);

        _logger.LogDebug($"Getting diagnostics for {razorDocument.FilePath} with previousResultId: {previousResultId ?? "(none)"}");

        // Check cache first
        var cachedEntry = await _diagnosticsCacheService.TryGetCachedEntryAsync(razorDocument, previousResultId, cancellationToken).ConfigureAwait(false);

        // Get C# diagnostics from Roslyn
        // TODO: Once Roslyn supports previousResultId, pass cachedEntry?.CSharpResultId
        var csharpDiagnosticsTask = GetCSharpDiagnosticsAsync(razorDocument, correlationId, cancellationToken);

        // Get HTML diagnostics from WebTools
        var htmlResultTask = SupportsHtmlDiagnostics
            ? GetHtmlDiagnosticsWithResultIdAsync(razorDocument, cachedEntry?.HtmlResultId, correlationId, cancellationToken)
            : Task.FromResult<HtmlDiagnosticsResult?>(null);

        try
        {
            await Task.WhenAll(htmlResultTask, csharpDiagnosticsTask).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _logger.LogError(e, $"Exception thrown in PullDiagnostic delegation");
            throw;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        var csharpDiagnostics = csharpDiagnosticsTask.VerifyCompleted();
        var htmlResult = htmlResultTask.VerifyCompleted();

        // Determine HTML diagnostics - use cached if unchanged
        LspDiagnostic[] htmlDiagnosticsForOop;
        ImmutableArray<LspDiagnostic>? cachedHtmlDiagnostics = null;
        string? newHtmlResultId = null;

        if (htmlResult is not null)
        {
            newHtmlResultId = htmlResult.ResultId;

            if (htmlResult.IsUnchanged && cachedEntry is not null)
            {
                // HTML server says unchanged - use our cached (already translated) diagnostics
                _logger.LogDebug($"HTML diagnostics unchanged (resultId match), using cached diagnostics");
                htmlDiagnosticsForOop = []; // Don't send to OOP - we'll use cached
                cachedHtmlDiagnostics = cachedEntry.CachedHtmlDiagnostics;
            }
            else
            {
                // HTML changed - need to translate via OOP
                htmlDiagnosticsForOop = htmlResult.Diagnostics;
            }
        }
        else
        {
            htmlDiagnosticsForOop = [];
        }

        // Call OOP with caching support - pass the cached Razor checksum so OOP can skip
        // Razor diagnostic computation if unchanged
        var previousRazorChecksum = cachedEntry?.RazorDiagnosticsChecksum;

        _logger.LogDebug($"Calling OOP with {csharpDiagnostics.Length} C# and {htmlDiagnosticsForOop.Length} Html diagnostics, previousRazorChecksum: {previousRazorChecksum ?? "(none)"}");
        var diagnosticsResult = await _remoteServiceInvoker.TryInvokeAsync<IRemoteDiagnosticsService, DiagnosticsResult>(
            razorDocument.Project.Solution,
            (service, solutionInfo, cancellationToken) => service.GetDiagnosticsAsync(
                solutionInfo,
                razorDocument.Id,
                csharpDiagnostics,
                htmlDiagnosticsForOop,
                previousRazorChecksum,
                cancellationToken),
            cancellationToken).ConfigureAwait(false);

        if (cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        // Determine which diagnostics to use (new or cached)
        var finalCSharpDiagnostics = diagnosticsResult.CSharpDiagnostics;

        // For HTML diagnostics, use cached if we determined HTML was unchanged earlier
        var finalHtmlDiagnostics = cachedHtmlDiagnostics ?? diagnosticsResult.HtmlDiagnostics;

        // For Razor diagnostics, if OOP returned empty array AND checksum matches, use cached
        ImmutableArray<LspDiagnostic> finalRazorDiagnostics;
        if (diagnosticsResult.RazorDiagnostics.IsEmpty &&
            diagnosticsResult.RazorDiagnosticsChecksum == previousRazorChecksum &&
            cachedEntry is not null)
        {
            _logger.LogDebug($"Razor diagnostics unchanged (checksum match), using cached diagnostics");
            finalRazorDiagnostics = cachedEntry.CachedRazorDiagnostics;
        }
        else
        {
            finalRazorDiagnostics = diagnosticsResult.RazorDiagnostics;
        }

        var allDiagnostics = ImmutableArray.CreateBuilder<LspDiagnostic>(
            finalCSharpDiagnostics.Length + finalHtmlDiagnostics.Length + finalRazorDiagnostics.Length);
        allDiagnostics.AddRange(finalCSharpDiagnostics);
        allDiagnostics.AddRange(finalHtmlDiagnostics);
        allDiagnostics.AddRange(finalRazorDiagnostics);
        var diagnostics = allDiagnostics.MoveToImmutable();

        // Check if all diagnostics have changed compared to cache
        if (cachedEntry is not null && DiagnosticsAreEqual(cachedEntry.GetAllDiagnostics(), diagnostics))
        {
            _logger.LogDebug($"All diagnostics unchanged for {razorDocument.FilePath}, returning cached resultId");
            return DiagnosticsRequestResult.Unchanged(cachedEntry.ResultId);
        }

        // Create new cache entry with per-source diagnostics
        var documentVersion = await DiagnosticsCacheDocumentVersion.CreateAsync(razorDocument, cancellationToken).ConfigureAwait(false);
        var newResultId = Guid.NewGuid().ToString();
        var newEntry = new DiagnosticsCacheEntry
        {
            ResultId = newResultId,
            DocumentVersion = documentVersion,
            CachedCSharpDiagnostics = finalCSharpDiagnostics,
            CachedHtmlDiagnostics = finalHtmlDiagnostics,
            CachedRazorDiagnostics = finalRazorDiagnostics,
            HtmlResultId = newHtmlResultId ?? cachedEntry?.HtmlResultId,
            RazorDiagnosticsChecksum = diagnosticsResult.RazorDiagnosticsChecksum
        };

        _diagnosticsCacheService.UpdateCache(razorDocument.Id, newEntry);

        _logger.LogDebug($"Reporting {diagnostics.Length} diagnostics back to the client with resultId {newResultId}");
        return DiagnosticsRequestResult.Changed(newResultId, diagnostics);
    }

    /// <summary>
    /// Result from getting HTML diagnostics, including the result ID for caching.
    /// </summary>
    private sealed record HtmlDiagnosticsResult(LspDiagnostic[] Diagnostics, string? ResultId, bool IsUnchanged);

    /// <summary>
    /// Compares two diagnostic arrays for equality.
    /// </summary>
    private static bool DiagnosticsAreEqual(ImmutableArray<LspDiagnostic> a, ImmutableArray<LspDiagnostic> b)
    {
        if (a.Length != b.Length)
        {
            return false;
        }

        // Compare diagnostics by their key properties
        // We use a simple ordered comparison since diagnostics should be in consistent order
        for (var i = 0; i < a.Length; i++)
        {
            var diagA = a[i];
            var diagB = b[i];

            if (diagA.Code != diagB.Code ||
                diagA.Message != diagB.Message ||
                !RangesAreEqual(diagA.Range, diagB.Range) ||
                diagA.Severity != diagB.Severity)
            {
                return false;
            }
        }

        return true;
    }

    private static bool RangesAreEqual(Roslyn.LanguageServer.Protocol.Range? a, Roslyn.LanguageServer.Protocol.Range? b)
    {
        if (a is null && b is null)
        {
            return true;
        }

        if (a is null || b is null)
        {
            return false;
        }

        return a.Start.Line == b.Start.Line &&
               a.Start.Character == b.Start.Character &&
               a.End.Line == b.End.Line &&
               a.End.Character == b.End.Character;
    }

    protected static Task<SourceGeneratedDocument?> TryGetGeneratedDocumentAsync(TextDocument razorDocument, CancellationToken cancellationToken)
    {
        return razorDocument.Project.TryGetSourceGeneratedDocumentForRazorDocumentAsync(razorDocument, cancellationToken);
    }

    private async Task<LspDiagnostic[]> GetCSharpDiagnosticsAsync(TextDocument razorDocument, Guid correletionId, CancellationToken cancellationToken)
    {
        var generatedDocument = await TryGetGeneratedDocumentAsync(razorDocument, cancellationToken).ConfigureAwait(false);
        if (generatedDocument is null)
        {
            return [];
        }

        _logger.LogDebug($"Getting C# diagnostics for {generatedDocument.FilePath}");

        using var _ = _telemetryReporter.TrackLspRequest(LspMethodName, "Razor.ExternalAccess", TelemetryThresholds.DiagnosticsSubLSPTelemetryThreshold, correletionId);
        var supportsVisualStudioExtensions = _clientCapabilitiesService.ClientCapabilities.SupportsVisualStudioExtensions;
        var diagnostics = await ExternalHandlers.Diagnostics.GetDocumentDiagnosticsAsync(generatedDocument, supportsVisualStudioExtensions, cancellationToken).ConfigureAwait(false);
        return [.. diagnostics];
    }

    private async Task<HtmlDiagnosticsResult?> GetHtmlDiagnosticsWithResultIdAsync(TextDocument razorDocument, string? previousHtmlResultId, Guid correletionId, CancellationToken cancellationToken)
    {
        var diagnosticsParams = CreateHtmlParams(razorDocument.CreateUri(), previousHtmlResultId);

        var result = await _requestInvoker.MakeHtmlLspRequestAsync<TRequest, TResponse>(
            razorDocument,
            LspMethodName,
            diagnosticsParams,
            TelemetryThresholds.DiagnosticsSubLSPTelemetryThreshold,
            correletionId,
            cancellationToken).ConfigureAwait(false);

        if (result is null)
        {
            return new HtmlDiagnosticsResult([], null, false);
        }

        var diagnostics = ExtractHtmlDiagnostics(result);
        var resultId = ExtractHtmlResultId(result);
        var isUnchanged = IsHtmlResponseUnchanged(result, previousHtmlResultId);

        return new HtmlDiagnosticsResult(diagnostics, resultId, isUnchanged);
    }
}
