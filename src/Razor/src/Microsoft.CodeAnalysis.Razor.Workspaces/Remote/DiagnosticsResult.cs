// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using System.Runtime.Serialization;

namespace Microsoft.CodeAnalysis.Razor.Remote;

/// <summary>
/// Result from the OOP diagnostics service that includes per-source diagnostics with checksums
/// to enable partial caching on the devenv side.
/// </summary>
[DataContract]
internal readonly record struct DiagnosticsResult
{
    /// <summary>
    /// Translated C# diagnostics in Razor document coordinates.
    /// </summary>
    [DataMember(Order = 0)]
    public ImmutableArray<LspDiagnostic> CSharpDiagnostics { get; init; }

    /// <summary>
    /// Translated HTML diagnostics in Razor document coordinates.
    /// </summary>
    [DataMember(Order = 1)]
    public ImmutableArray<LspDiagnostic> HtmlDiagnostics { get; init; }

    /// <summary>
    /// Translated Razor diagnostics.
    /// </summary>
    [DataMember(Order = 2)]
    public ImmutableArray<LspDiagnostic> RazorDiagnostics { get; init; }

    /// <summary>
    /// Content checksum used to detect when Razor diagnostics have changed.
    /// </summary>
    [DataMember(Order = 3)]
    public string? RazorDiagnosticsChecksum { get; init; }

    /// <summary>
    /// Combines all diagnostics into a single array.
    /// </summary>
    public ImmutableArray<LspDiagnostic> GetAllDiagnostics()
        => [.. CSharpDiagnostics, .. HtmlDiagnostics, .. RazorDiagnostics];
}
