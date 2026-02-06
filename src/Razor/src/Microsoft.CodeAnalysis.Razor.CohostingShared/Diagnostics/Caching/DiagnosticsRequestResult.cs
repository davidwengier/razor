// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.VisualStudio.Razor.LanguageClient.Cohost;

/// <summary>
/// Represents the result of a diagnostics request, which can be either changed (with new diagnostics)
/// or unchanged (indicating the client can reuse its cached diagnostics).
/// </summary>
internal readonly record struct DiagnosticsRequestResult(
    string ResultId,
    System.Collections.Immutable.ImmutableArray<LspDiagnostic>? Diagnostics)
{
    /// <summary>
    /// True if the diagnostics have not changed since the previous request.
    /// </summary>
    public bool IsUnchanged => Diagnostics is null;

    /// <summary>
    /// Creates a result indicating diagnostics have changed.
    /// </summary>
    public static DiagnosticsRequestResult Changed(string resultId, System.Collections.Immutable.ImmutableArray<LspDiagnostic> diagnostics)
        => new(resultId, diagnostics);

    /// <summary>
    /// Creates a result indicating diagnostics are unchanged.
    /// </summary>
    public static DiagnosticsRequestResult Unchanged(string resultId)
        => new(resultId, null);
}
