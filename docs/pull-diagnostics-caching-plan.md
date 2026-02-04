# Implementation Plan: Pull Diagnostics Caching for Cohosting

## Problem Statement

GitHub Issue: [dotnet/razor#8454](https://github.com/dotnet/razor/issues/8454)

The current Razor cohosting diagnostics implementation (PR #8453) uses pull diagnostics but in a "naive" fashion - it generates a new GUID for every response's `ResultId`, which means the client can never re-use cached diagnostics. This defeats the purpose of the pull diagnostics protocol's caching mechanism.

### How Pull Diagnostics Caching Works (LSP 3.17)

1. **Client Request**: Sends `textDocument/diagnostic` with `previousResultId` (the last `ResultId` received)
2. **Server Response Options**:
   - **Changed**: `FullDocumentDiagnosticReport` with new `resultId` and diagnostics
   - **Unchanged**: `UnchangedDocumentDiagnosticReport` with `kind: "unchanged"` and same `resultId` (no diagnostics array)

3. **Challenge for Razor**: We delegate to multiple downstream servers (Roslyn for C#, WebTools/HTML for HTML/CSS/JS). Each has its own `resultId`. If one server reports "unchanged" and another reports "changed", we must:
   - Use cached diagnostics for the "unchanged" server
   - Use fresh diagnostics for the "changed" server
   - Merge them and respond to the client with our own unified `resultId`

### Reference Implementation

WebTools' `DocumentPullDiagnosticsMergedReport.cs` (in `D:\Code\WebTools`) demonstrates this pattern:
- Caches diagnostics per language client
- Maps our `resultId` to downstream servers' `resultId`s
- Only generates a new `resultId` when any diagnostics actually change
- Returns `null` diagnostics with same `resultId` when nothing changed

## Proposed Approach

### Architecture Overview

Create a **per-document diagnostics cache in devenv** that:
1. Stores the last **translated & filtered** response from each source (C#, HTML, Razor)
2. Tracks a composite `resultId` that represents the combined state
3. On request with `previousResultId`:
   - Pass each downstream source's cached `resultId` in delegated requests
   - Collect responses (some may be "unchanged", some may have new diagnostics)
   - For "unchanged" sources: use devenv-cached diagnostics (no OOP call needed)
   - For "changed" sources: translate/filter via OOP, then cache result
   - Only generate a new composite `resultId` if any source changed
   - Return "unchanged" if all sources report no changes

### Key Components

```
┌─────────────────────────────────────────────────────────────────┐
│                    Client (VS / VS Code)                        │
│              previousResultId = "razor-abc123"                  │
└────────────────────────────┬────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│         CohostDocumentPullDiagnosticsEndpointBase (devenv)      │
│                                                                 │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │    DiagnosticsCacheService (devenv, post-translation)    │   │
│  │                                                          │   │
│  │  Cache per document (keyed by our resultId):             │   │
│  │    - csharpResultId + cachedCSharpDiags (translated)     │   │
│  │    - htmlResultId + cachedHtmlDiags (translated)         │   │
│  │    - razorChecksum + cachedRazorDiags (translated)       │   │
│  │    - documentVersion (for invalidation)                  │   │
│  └─────────────────────────────────────────────────────────┘   │
│                                                                 │
│  Request flow:                                                  │
│   1. Validate document version (invalidate if changed)          │
│   2. Lookup cache by previousResultId                           │
│   3. Call C# with cached csharpResultId                         │
│   4. Call HTML with cached htmlResultId                         │
│   5. For each source:                                           │
│      - "unchanged" → use devenv-cached diagnostics (no OOP!)   │
│      - "changed" → call OOP to translate, cache result          │
│   6. Combine all diagnostics, respond to client                 │
└─────────────────────────────────────────────────────────────────┘
                    │
        ┌───────────┼───────────┐
        ▼           ▼           ▼
   ┌─────────┐ ┌─────────┐ ┌─────────────┐
   │ Roslyn  │ │WebTools │ │ OOP Service │
   │(C# diag)│ │(HTML)   │ │(translate)  │
   └─────────┘ └─────────┘ └─────────────┘
```

**Key optimization**: When a source returns "unchanged", we skip the OOP translation call entirely and use our devenv-cached (already translated) diagnostics.

## Scope Constraints

Per user requirements:
- **Limited to cohosting system only** - NO changes in `Microsoft.AspNetCore.Razor.LanguageServer` except absolute minimum for compilation
- Focus on `CohostDocumentPullDiagnosticsEndpointBase`, `RemoteDiagnosticsService`, and related helpers
- Must work for both VS and VS Code
- Must include task list diagnostics caching
- Must evict cache on document close

## Work Plan

### Phase 1: Create Diagnostics Cache Infrastructure (in devenv)

The cache lives in **devenv** (not OOP) because:
1. We cache **post-translation, post-filtering** diagnostics (exactly what we'd send to client)
2. When reusing cached diagnostics, no OOP round-trip needed
3. No re-translation or re-filtering of cached diagnostics
4. Simpler architecture - cache is close to where responses are constructed

- [ ] **1.1** Create `IDiagnosticsCacheService` interface in `Microsoft.CodeAnalysis.Razor.CohostingShared`
  - Define cache entry structure (per-document: our resultId, per-source resultIds, **per-source cached diagnostics**)
  - Define methods: `TryGetCachedEntry`, `UpdateCache`, `InvalidateDocument`
  - Location: `src/Razor/src/Microsoft.CodeAnalysis.Razor.CohostingShared/Diagnostics/`

- [ ] **1.2** Create cache entry types
  - Reuse `RazorDocumentVersion` pattern from `HtmlDocumentSynchronizer.RazorDocumentVersion.cs`
  - `DiagnosticsCacheEntry` class/record with:
    - **ResultId** - our composite resultId sent to client (e.g., "razor-abc123")
    - **RazorDocumentVersion** (WorkspaceVersion + Checksum, same as HtmlDocumentSynchronizer)
    - **CSharpResultId** - Roslyn's resultId from last call
    - **CachedCSharpDiagnostics** - **translated & filtered** diagnostics from Roslyn
    - **HtmlResultId** - WebTools' resultId from last call
    - **CachedHtmlDiagnostics** - **translated & filtered** diagnostics from WebTools
    - **RazorDiagnosticsChecksum** - checksum of Razor diagnostics
    - **CachedRazorDiagnostics** - **translated** Razor diagnostics
  - Use `ConcurrentDictionary<DocumentId, CacheEntry>` for thread safety
  - Location: Same folder as HtmlDocumentSynchronizer for consistency

- [ ] **1.3** Implement `DiagnosticsCacheService` 
  - Singleton service that caches per-document diagnostic state
  - **Must cache actual diagnostics, not just resultIds** (critical for partial updates)
  - **Caches post-OOP diagnostics** - already translated to Razor document coordinates
  - Use weak references or explicit cleanup on document close
  - Wire up to document close events for cache eviction

### Phase 2: Modify Base Endpoint to Support Caching

- [ ] **2.1** Add `previousResultId` parameter handling in `CohostDocumentPullDiagnosticsEndpointBase`
  - Extract `previousResultId` from request in derived classes
  - Pass to `GetDiagnosticsAsync` method

- [ ] **2.2** Modify `GetDiagnosticsAsync` to use cache
  - Check cache for existing entry matching `previousResultId`
  - **Validate document version matches cached version**
  - If version mismatch → treat as cache miss (document changed)
  - If found and version matches, use cached resultIds for downstream requests
  - Track which sources returned "unchanged" vs new diagnostics

- [ ] **2.3** Add "unchanged" response support
  - Create abstract method `CreateUnchangedResponse` in base class
  - Implement in derived classes to return proper `UnchangedDocumentDiagnosticReport`

- [ ] **2.4** Modify response construction
  - Only generate new `resultId` when any source changed
  - Return "unchanged" response when all sources report no changes
  - Update cache with new state

### Phase 3: Update Derived Endpoint Classes

- [ ] **3.1** Update VS Code `DocumentPullDiagnosticsEndpoint`
  - Extract `previousResultId` from `DocumentDiagnosticParams`
  - Implement `CreateUnchangedResponse`
  - Pass `previousResultId` to base class

- [ ] **3.2** Update VS `CohostDocumentPullDiagnosticsEndpoint`  
  - Extract `previousResultId` from `VSInternalDocumentDiagnosticsParams`
  - Implement `CreateUnchangedResponse`
  - Handle both regular diagnostics and task list diagnostics
  - Pass `previousResultId` to base class

### Phase 4: Handle Downstream Server Result IDs

- [ ] **4.1** Modify C# diagnostics fetching - **REQUIRES ROSLYN CHANGES**
  - Current API: `ExternalHandlers.Diagnostics.GetDocumentDiagnosticsAsync(Document, bool, CancellationToken)`
  - This bypasses Roslyn's LSP handler caching entirely (uses `IDiagnosticAnalyzerService.GetDiagnosticsForSpanAsync` directly)
  - **Need new API that leverages Roslyn's existing `DiagnosticsPullCache`**
  - See "Roslyn API Changes" section below for detailed proposal

- [ ] **4.2** Modify HTML diagnostics fetching  
  - Currently uses `IHtmlRequestInvoker.MakeHtmlLspRequestAsync`
  - For VS: WebTools supports pull diagnostics with previousResultId
  - For VS Code: HTML language server may support this
  - Update request creation to include previousResultId

- [ ] **4.3** Handle Razor diagnostics caching
  - Razor diagnostics come from `codeDocument.GetRequiredCSharpDocument().Diagnostics`
  - Use document version or code document checksum for change detection
  - Cache translated diagnostics in OOP service

### Phase 5: Remote Service Updates

- [ ] **5.1** Update `IRemoteDiagnosticsService` interface
  - Add optional previousResultId parameter
  - Add method to check if diagnostics changed without full computation
  - Consider returning structured result with changed/unchanged indicator

- [ ] **5.2** Update `RemoteDiagnosticsService` implementation
  - Implement caching logic for Razor-specific diagnostics
  - Return "unchanged" indicator when appropriate
  - Handle diagnostic translation caching

### Phase 6: Cache Eviction and Document Version Tracking

- [ ] **6.1** Wire up document close events
  - Subscribe to document close events in VS and VS Code
  - Call `IDiagnosticsCacheService.InvalidateDocument` on close
  - Ensure proper cleanup to avoid memory leaks

- [ ] **6.2** Document version validation strategy
  - **Reuse `RazorDocumentVersion` from HtmlDocumentSynchronizer** (or share it)
  - Use `razorDocument.GetChecksumAsync()` for content equality (primary check)
  - Use `razorDocument.Project.Solution.GetWorkspaceVersion()` for ordering
  - On each request, compare current checksum with cached checksum
  - If checksums differ → invalidate cache entry, compute fresh diagnostics
  - Store new version with updated cache entry

- [ ] **6.3** Handle generated document version changes
  - C# diagnostics depend on generated document, not just Razor source
  - Track both Razor source version AND generated document version
  - Invalidate C# cache if either changes

### Phase 7: Task List Diagnostics

- [ ] **7.1** Extend caching to task list diagnostics (VS only)
  - Add separate cache entries for task list diagnostics
  - Handle task list descriptor changes (configuration change)
  - Implement similar caching pattern as regular diagnostics

### Phase 8: Testing

- [ ] **8.1** Add unit tests for cache service
  - Test cache hit/miss scenarios
  - Test eviction behavior
  - Test concurrent access

- [ ] **8.2** Update existing cohost diagnostics tests
  - Add tests for previousResultId handling
  - Add tests for "unchanged" response
  - Test mixed changed/unchanged scenarios
  - **Test document edit invalidates cache** (same previousResultId, different content)
  - **Test version mismatch returns fresh diagnostics**

- [ ] **8.3** Add integration tests
  - Test end-to-end caching behavior
  - Test cache invalidation on document close
  - Test with VS Code endpoint
  - Test with VS endpoint (including task list)

## Roslyn API Changes Required

### The Core Problem

The caching complexity lives **in Razor**, not in Roslyn. Here's why:

```
Client sends: previousResultId = "razor-abc123"
                    │
                    ▼
┌─────────────────────────────────────────────────────────────────────┐
│  Razor's Cache (keyed by "razor-abc123")                            │
│                                                                     │
│  Stored mapping:                                                    │
│    - csharpResultId: "roslyn-xyz"     → cachedCSharpDiagnostics[]  │
│    - htmlResultId: "webtools-456"     → cachedHtmlDiagnostics[]    │
│    - razorDiagnosticsChecksum: "..."  → cachedRazorDiagnostics[]   │
│    - documentVersion: RazorDocumentVersion                          │
└─────────────────────────────────────────────────────────────────────┘
                    │
        ┌───────────┴───────────┐
        ▼                       ▼
┌───────────────┐       ┌───────────────┐
│    Roslyn     │       │   WebTools    │
│ prev="roslyn- │       │ prev="web-    │
│      xyz"     │       │   tools-456"  │
└───────┬───────┘       └───────┬───────┘
        │                       │
        ▼                       ▼
   "unchanged"            NEW diagnostics
   (no diags returned)    + new resultId
```

**The problem**: Roslyn says "unchanged" but WebTools has new diagnostics. 
We must return **ALL** diagnostics to the client, but Roslyn didn't give us any!

**The solution**: Razor must cache the actual diagnostics from each source, so when one source says "unchanged", we can use our cached copy.

### What We Need from Roslyn

The Roslyn API change is simpler than initially proposed. We need:

1. **A resultId returned** so we can track if Roslyn's diagnostics changed
2. **The "unchanged" indicator** so we know to use our cached copy

```csharp
// Current API - no caching support
public static Task<ImmutableArray<LSP.Diagnostic>> GetDocumentDiagnosticsAsync(
    Document document, 
    bool supportsVisualStudioExtensions, 
    CancellationToken cancellationToken)

// Proposed API - supports caching
public readonly record struct DiagnosticResult(
    ImmutableArray<LSP.Diagnostic>? Diagnostics,  // null if unchanged
    string ResultId,                               // always returned
    bool IsUnchanged);

public static Task<DiagnosticResult> GetDocumentDiagnosticsAsync(
    Document document,
    bool supportsVisualStudioExtensions,
    string? previousResultId,  // Roslyn's resultId from last call
    CancellationToken cancellationToken)
```

**Key insight**: When Roslyn returns `IsUnchanged: true`, we use **Razor's cached copy** of Roslyn's diagnostics, NOT Roslyn's cache. Roslyn's internal cache just helps Roslyn avoid recomputation.

### Implementation in Roslyn

The simplest implementation uses checksum-based comparison:

```csharp
public static async Task<DiagnosticResult> GetDocumentDiagnosticsAsync(
    Document document,
    bool supportsVisualStudioExtensions,
    string? previousResultId,
    CancellationToken cancellationToken)
{
    var solutionServices = document.Project.Solution.Services;
    
    // Compute current version identifier (checksum-based)
    var checksum = await document.Project.GetDiagnosticChecksumAsync(cancellationToken);
    var globalVersion = GetGlobalDiagnosticStateVersion(); // analyzer state, options, etc.
    var currentResultId = $"{globalVersion}:{checksum}";
    
    // If unchanged, early exit - Razor will use its cached copy
    if (previousResultId == currentResultId)
    {
        return new DiagnosticResult(
            Diagnostics: null,      // Don't need to return them
            ResultId: currentResultId,
            IsUnchanged: true);
    }
    
    // Compute diagnostics
    var diagnostics = await ComputeDiagnosticsAsync(document, ...);
    return new DiagnosticResult(
        Diagnostics: diagnostics,
        ResultId: currentResultId,
        IsUnchanged: false);
}
```

Alternatively, Roslyn could expose access to their existing `DiagnosticsPullCache` for better performance (avoids recomputing checksums), but the above is sufficient.

### Razor's Cache Architecture (in devenv)

The cache lives in devenv and stores **post-translation, post-filtering** diagnostics:

```csharp
internal sealed class DiagnosticsCacheEntry
{
    // Our composite resultId sent to client
    public required string ResultId { get; init; }
    
    // Document version for invalidation
    public required RazorDocumentVersion DocumentVersion { get; init; }
    
    // Per-source: resultId + TRANSLATED/FILTERED diagnostics
    // These are ready to send to client - no OOP call needed to reuse
    public required string? CSharpResultId { get; init; }
    public required ImmutableArray<LspDiagnostic> CachedCSharpDiagnostics { get; init; }
    
    public required string? HtmlResultId { get; init; }
    public required ImmutableArray<LspDiagnostic> CachedHtmlDiagnostics { get; init; }
    
    // Razor diagnostics (from code document, already translated)
    public required Checksum RazorDiagnosticsChecksum { get; init; }
    public required ImmutableArray<LspDiagnostic> CachedRazorDiagnostics { get; init; }
}
```

**Why devenv, not OOP?**
- OOP cache would store raw/untranslated diagnostics
- Reusing OOP-cached diagnostics would require: wire transfer → re-translate → re-filter
- devenv cache stores final diagnostics → just combine and return
- Significant perf win when ANY source is unchanged

### Request Flow

```
1. Client request arrives with previousResultId = "razor-abc123"

2. Validate document version (checksum comparison)
   └─ Version changed? → Invalidate cache, goto step 4 with no cache

3. Look up cache entry by "razor-abc123"
   └─ Not found? → goto step 4 with no cache

4. Call downstream servers with THEIR cached resultIds:
   - Roslyn: GetDiagnosticsAsync(doc, prev: "roslyn-xyz")
   - HTML:   MakeRequest(doc, prev: "webtools-456")
   - Razor:  Check code document checksum via OOP

5. Process responses - KEY OPTIMIZATION:
   ┌─────────────┬─────────────────┬──────────────────────────────────────┐
   │   Source    │    Response     │         Action                       │
   ├─────────────┼─────────────────┼──────────────────────────────────────┤
   │   Roslyn    │   unchanged     │ Use devenv cache (NO OOP call!)      │
   │   HTML      │   NEW diags     │ Call OOP to translate, update cache  │
   │   Razor     │   unchanged     │ Use devenv cache (NO OOP call!)      │
   └─────────────┴─────────────────┴──────────────────────────────────────┘

6. Determine response:
   - ALL unchanged? → Return "unchanged" with same resultId to client
                      (zero OOP translation, zero work!)
   - ANY changed?   → Combine: devenv-cached + OOP-translated
                      Generate new resultId, update cache, return to client
```

**Performance characteristics**:
- **Best case** (all unchanged): No OOP calls, no translation, instant response
- **Partial update** (one source changed): Only translate changed diagnostics
- **Full update** (all changed): Same as current behavior (no regression)

### Work Items for Roslyn Changes

- [ ] **R1** Define `DiagnosticResult` record type in ExternalAccess.Razor
- [ ] **R2** Add new `GetDocumentDiagnosticsAsync` overload with `previousResultId` parameter
- [ ] **R3** Implement checksum-based or cache-based change detection
- [ ] **R4** Same changes for `GetTaskListAsync`
- [ ] **R5** (Optional) Expose `GetDiagnosticChecksumAsync` for Razor to use directly
- [ ] **R6** Update Razor to use new API and implement caching layer


### Differences from WebTools Implementation

1. **No TextBuffer access**: Cohosting uses Roslyn's `TextDocument`, not VS text buffers
2. **OOP processing**: Diagnostics are processed in OOP service, not in-proc
3. **Multiple entry points**: Both VS and VS Code have separate derived endpoints
4. **No snapshot translation**: We don't need to translate cached diagnostics to new snapshots (document changes invalidate cache)

### Document Versioning Strategy

**Critical invariant**: We must never return stale diagnostics for a changed document.

**Reference Implementation**: `HtmlDocumentSynchronizer` in `Microsoft.CodeAnalysis.Razor.CohostingShared/HtmlDocumentServices/` already implements this pattern:

```csharp
// From HtmlDocumentSynchronizer.RazorDocumentVersion.cs
internal readonly struct RazorDocumentVersion(int workspaceVersion, ChecksumWrapper checksum)
{
    internal int WorkspaceVersion => workspaceVersion;
    internal ChecksumWrapper Checksum => checksum;

    internal static async Task<RazorDocumentVersion> CreateAsync(TextDocument razorDocument, CancellationToken cancellationToken)
    {
        var workspaceVersion = razorDocument.Project.Solution.GetWorkspaceVersion();
        var checksum = await razorDocument.GetChecksumAsync(cancellationToken).ConfigureAwait(false);
        return new RazorDocumentVersion(workspaceVersion, checksum);
    }
}
```

**Key APIs to use**:
- `razorDocument.Project.Solution.GetWorkspaceVersion()` - workspace-level version
- `razorDocument.GetChecksumAsync()` - document content checksum
- `ChecksumWrapper` - Roslyn's checksum type for comparison

**Version comparison strategy** (from HtmlDocumentSynchronizer):
1. **Same checksum** → same document content (use cached result)
2. **Different checksum, lower workspace version** → older request, ignore
3. **Different checksum, same/higher workspace version** → newer document, invalidate cache

```
Cache Lookup Logic:
┌─────────────────────────────────────────────────────────────────┐
│  Request arrives with previousResultId = "abc123"              │
│                                                                 │
│  1. Look up cache entry by resultId                             │
│     └─ Not found? → Cache miss, compute fresh diagnostics      │
│                                                                 │
│  2. Compare document checksum (primary) and version (ordering)  │
│     └─ currentChecksum != cachedChecksum?                       │
│        → Content changed! Invalidate entry, compute fresh       │
│                                                                 │
│  3. Check downstream sources for changes                        │
│     └─ Any source reports new diagnostics?                      │
│        → Merge cached + fresh, generate new resultId            │
│                                                                 │
│  4. All sources unchanged AND checksum matches                  │
│     → Return "unchanged" response with same resultId            │
└─────────────────────────────────────────────────────────────────┘
```

**Edge case**: Client sends `previousResultId` after rapid edits
- Document checksum will have changed
- Cache lookup succeeds but checksum validation fails
- Result: Fresh diagnostics computed (correct behavior)

### Potential Challenges

1. **Roslyn API limitations**: `ExternalHandlers.Diagnostics.GetDocumentDiagnosticsAsync` may not expose previousResultId
   - Workaround: Use document version/checksum for change detection
   
2. **OOP serialization**: Cache may need to live in devenv, not OOP
   - Result IDs from downstream servers need to cross the OOP boundary
   
3. **HTML request invoker**: May need updates to support previousResultId in requests
   - Check if `IHtmlRequestInvoker` supports this already

### Performance Expectations

- **Best case**: All sources unchanged → immediate "unchanged" response (no computation)
- **Typical case**: One source changed → compute only that source's diagnostics
- **Worst case**: All sources changed → same as current behavior (no regression)

## Files Likely to be Modified

### New Files
- `src/Razor/src/Microsoft.CodeAnalysis.Razor.CohostingShared/Diagnostics/IDiagnosticsCacheService.cs`
- `src/Razor/src/Microsoft.CodeAnalysis.Razor.CohostingShared/Diagnostics/DiagnosticsCacheService.cs`
- `src/Razor/src/Microsoft.CodeAnalysis.Razor.CohostingShared/Diagnostics/DiagnosticsCacheEntry.cs`
- `src/Razor/test/Microsoft.VisualStudioCode.RazorExtension.Test/Diagnostics/DiagnosticsCacheServiceTest.cs`

### Modified Files
- `src/Razor/src/Microsoft.CodeAnalysis.Razor.CohostingShared/Diagnostics/CohostDocumentPullDiagnosticsEndpointBase.cs`
- `src/Razor/src/Microsoft.VisualStudioCode.RazorExtension/Endpoints/DocumentPullDiagnosticsEndpoint.cs`
- `src/Razor/src/Microsoft.VisualStudio.LanguageServices.Razor/LanguageClient/Cohost/CohostDocumentPullDiagnosticsEndpoint.cs`
- `src/Razor/src/Microsoft.CodeAnalysis.Razor.Workspaces/Remote/IRemoteDiagnosticsService.cs`
- `src/Razor/src/Microsoft.CodeAnalysis.Remote.Razor/Diagnostics/RemoteDiagnosticsService.cs`
- `src/Razor/test/Microsoft.VisualStudioCode.RazorExtension.Test/Endpoints/Shared/CohostDocumentPullDiagnosticsTest.cs`
- `src/Razor/test/Microsoft.VisualStudio.LanguageServices.Razor.Test/Cohost/CohostDocumentPullDiagnosticsTest.cs`
