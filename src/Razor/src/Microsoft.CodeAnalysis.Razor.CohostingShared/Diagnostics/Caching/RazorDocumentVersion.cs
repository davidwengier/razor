// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.ExternalAccess.Razor;

namespace Microsoft.VisualStudio.Razor.LanguageClient.Cohost;

/// <summary>
/// Tracks the version of a Razor document using both workspace version and content checksum.
/// This allows accurate detection of document changes even when the same previousResultId is sent
/// after document edits.
/// </summary>
/// <remarks>
/// This type is similar to HtmlDocumentSynchronizer.RazorDocumentVersion but is standalone
/// for use by the diagnostics caching infrastructure.
/// </remarks>
internal readonly struct DiagnosticsCacheDocumentVersion(int workspaceVersion, ChecksumWrapper checksum, Uri? documentUri)
{
    internal int WorkspaceVersion => workspaceVersion;
    internal ChecksumWrapper Checksum => checksum;
    internal Uri? DocumentUri => documentUri;

    public override string ToString()
        => $"Checksum {checksum} from workspace version {workspaceVersion}";

    internal static async Task<DiagnosticsCacheDocumentVersion> CreateAsync(TextDocument razorDocument, CancellationToken cancellationToken)
    {
        var workspaceVersion = razorDocument.Project.Solution.GetWorkspaceVersion();
        var checksum = await razorDocument.GetChecksumAsync(cancellationToken).ConfigureAwait(false);

        // Get the document URI for the URI index
        Uri? documentUri = null;
        if (razorDocument.FilePath is { } filePath && Uri.TryCreate(filePath, UriKind.Absolute, out var uri))
        {
            documentUri = uri;
        }

        return new DiagnosticsCacheDocumentVersion(workspaceVersion, checksum, documentUri);
    }

    /// <summary>
    /// Returns true if the document content has changed (checksums differ).
    /// </summary>
    internal bool HasContentChanged(DiagnosticsCacheDocumentVersion other)
        => !Checksum.Equals(other.Checksum);
}
