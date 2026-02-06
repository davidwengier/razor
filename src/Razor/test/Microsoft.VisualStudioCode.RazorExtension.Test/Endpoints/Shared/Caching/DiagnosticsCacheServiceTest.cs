// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Razor.Test.Common;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.VisualStudio.Razor.LanguageClient.Cohost;

public class DiagnosticsCacheServiceTest(ITestOutputHelper testOutput) : CohostEndpointTestBase(testOutput)
{
    [Fact]
    public async Task TryGetCachedEntryAsync_NoPreviousResultId_ReturnNull()
    {
        // Arrange
        var cacheService = new DiagnosticsCacheService(LoggerFactory);
        var document = CreateProjectAndRazorDocument("<div></div>");

        // Act
        var result = await cacheService.TryGetCachedEntryAsync(document, previousResultId: null, DisposalToken);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task TryGetCachedEntryAsync_UnknownResultId_ReturnsNull()
    {
        // Arrange
        var cacheService = new DiagnosticsCacheService(LoggerFactory);
        var document = CreateProjectAndRazorDocument("<div></div>");

        // Act
        var result = await cacheService.TryGetCachedEntryAsync(document, previousResultId: "unknown-result-id", DisposalToken);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateCache_ThenTryGetCachedEntryAsync_ReturnsEntry()
    {
        // Arrange
        var cacheService = new DiagnosticsCacheService(LoggerFactory);
        var document = CreateProjectAndRazorDocument("<div></div>");

        var documentVersion = await DiagnosticsCacheDocumentVersion.CreateAsync(document, DisposalToken);
        var entry = new DiagnosticsCacheEntry
        {
            ResultId = "test-result-id",
            DocumentVersion = documentVersion,
            CachedCSharpDiagnostics = [],
            CachedHtmlDiagnostics = [],
            CachedRazorDiagnostics = []
        };

        // Act
        cacheService.UpdateCache(document.Id, entry);
        var result = await cacheService.TryGetCachedEntryAsync(document, previousResultId: "test-result-id", DisposalToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("test-result-id", result.ResultId);
    }

    [Fact]
    public async Task UpdateCache_WithNewResultId_RemovesOldResultIdFromIndex()
    {
        // Arrange
        var cacheService = new DiagnosticsCacheService(LoggerFactory);
        var document = CreateProjectAndRazorDocument("<div></div>");

        var documentVersion = await DiagnosticsCacheDocumentVersion.CreateAsync(document, DisposalToken);
        var entry1 = new DiagnosticsCacheEntry
        {
            ResultId = "first-result-id",
            DocumentVersion = documentVersion,
            CachedCSharpDiagnostics = [],
            CachedHtmlDiagnostics = [],
            CachedRazorDiagnostics = []
        };
        var entry2 = new DiagnosticsCacheEntry
        {
            ResultId = "second-result-id",
            DocumentVersion = documentVersion,
            CachedCSharpDiagnostics = [],
            CachedHtmlDiagnostics = [],
            CachedRazorDiagnostics = []
        };

        // Act
        cacheService.UpdateCache(document.Id, entry1);
        cacheService.UpdateCache(document.Id, entry2);

        var resultForOldId = await cacheService.TryGetCachedEntryAsync(document, previousResultId: "first-result-id", DisposalToken);
        var resultForNewId = await cacheService.TryGetCachedEntryAsync(document, previousResultId: "second-result-id", DisposalToken);

        // Assert
        Assert.Null(resultForOldId); // Old result ID should be removed from index
        Assert.NotNull(resultForNewId);
        Assert.Equal("second-result-id", resultForNewId.ResultId);
    }

    [Fact]
    public async Task InvalidateDocument_RemovesEntry()
    {
        // Arrange
        var cacheService = new DiagnosticsCacheService(LoggerFactory);
        var document = CreateProjectAndRazorDocument("<div></div>");

        var documentVersion = await DiagnosticsCacheDocumentVersion.CreateAsync(document, DisposalToken);
        var entry = new DiagnosticsCacheEntry
        {
            ResultId = "test-result-id",
            DocumentVersion = documentVersion,
            CachedCSharpDiagnostics = [],
            CachedHtmlDiagnostics = [],
            CachedRazorDiagnostics = []
        };

        cacheService.UpdateCache(document.Id, entry);

        // Act
        cacheService.InvalidateDocument(document.Id);
        var result = await cacheService.TryGetCachedEntryAsync(document, previousResultId: "test-result-id", DisposalToken);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task DocumentRemoved_ByUri_RemovesEntry()
    {
        // Arrange
        var cacheService = new DiagnosticsCacheService(LoggerFactory);
        var document = CreateProjectAndRazorDocument("<div></div>");

        var documentVersion = await DiagnosticsCacheDocumentVersion.CreateAsync(document, DisposalToken);
        var entry = new DiagnosticsCacheEntry
        {
            ResultId = "test-result-id",
            DocumentVersion = documentVersion,
            CachedCSharpDiagnostics = [],
            CachedHtmlDiagnostics = [],
            CachedRazorDiagnostics = []
        };

        cacheService.UpdateCache(document.Id, entry);

        // Act - Remove by URI
        var uri = new Uri(document.FilePath!);
        cacheService.DocumentRemoved(uri);
        var result = await cacheService.TryGetCachedEntryAsync(document, previousResultId: "test-result-id", DisposalToken);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ClearAll_RemovesAllEntries()
    {
        // Arrange
        var cacheService = new DiagnosticsCacheService(LoggerFactory);
        // Note: Each call creates a document in a separate project with a different ID
        var document1 = CreateProjectAndRazorDocument("<div>1</div>");
        var document2 = CreateProjectAndRazorDocument("<div>2</div>");

        var documentVersion1 = await DiagnosticsCacheDocumentVersion.CreateAsync(document1, DisposalToken);
        var documentVersion2 = await DiagnosticsCacheDocumentVersion.CreateAsync(document2, DisposalToken);

        var entry1 = new DiagnosticsCacheEntry
        {
            ResultId = "result-id-1",
            DocumentVersion = documentVersion1,
            CachedCSharpDiagnostics = [],
            CachedHtmlDiagnostics = [],
            CachedRazorDiagnostics = []
        };
        var entry2 = new DiagnosticsCacheEntry
        {
            ResultId = "result-id-2",
            DocumentVersion = documentVersion2,
            CachedCSharpDiagnostics = [],
            CachedHtmlDiagnostics = [],
            CachedRazorDiagnostics = []
        };

        cacheService.UpdateCache(document1.Id, entry1);
        cacheService.UpdateCache(document2.Id, entry2);

        // Act
        cacheService.ClearAll();
        var result1 = await cacheService.TryGetCachedEntryAsync(document1, previousResultId: "result-id-1", DisposalToken);
        var result2 = await cacheService.TryGetCachedEntryAsync(document2, previousResultId: "result-id-2", DisposalToken);

        // Assert
        Assert.Null(result1);
        Assert.Null(result2);
    }

    [Fact]
    public async Task TryGetCachedEntryAsync_WithDifferentDocumentId_ReturnsNull()
    {
        // Arrange
        var cacheService = new DiagnosticsCacheService(LoggerFactory);
        // Note: Each call creates a document in a separate project with a different ID
        var document1 = CreateProjectAndRazorDocument("<div>1</div>");
        var document2 = CreateProjectAndRazorDocument("<div>2</div>");

        var documentVersion = await DiagnosticsCacheDocumentVersion.CreateAsync(document1, DisposalToken);
        var entry = new DiagnosticsCacheEntry
        {
            ResultId = "test-result-id",
            DocumentVersion = documentVersion,
            CachedCSharpDiagnostics = [],
            CachedHtmlDiagnostics = [],
            CachedRazorDiagnostics = []
        };

        cacheService.UpdateCache(document1.Id, entry);

        // Act - Try to get the entry using document2 but with document1's resultId
        var result = await cacheService.TryGetCachedEntryAsync(document2, previousResultId: "test-result-id", DisposalToken);

        // Assert - Should fail because document IDs don't match
        Assert.Null(result);
    }

    [Fact]
    public void GetAllDiagnostics_CombinesAllSources()
    {
        // Arrange
        var csharpDiag = CreateDiagnostic("CS0001", 0, 5);
        var htmlDiag = CreateDiagnostic("HTML001", 10, 15);
        var razorDiag = CreateDiagnostic("RZ001", 20, 25);

        var entry = new DiagnosticsCacheEntry
        {
            ResultId = "test",
            DocumentVersion = default,
            CachedCSharpDiagnostics = [csharpDiag],
            CachedHtmlDiagnostics = [htmlDiag],
            CachedRazorDiagnostics = [razorDiag]
        };

        // Act
        var allDiagnostics = entry.GetAllDiagnostics();

        // Assert
        Assert.Equal(3, allDiagnostics.Length);
        Assert.Contains(allDiagnostics, d => d.Code == "CS0001");
        Assert.Contains(allDiagnostics, d => d.Code == "HTML001");
        Assert.Contains(allDiagnostics, d => d.Code == "RZ001");
    }

    private static LspDiagnostic CreateDiagnostic(string code, int startLine, int endLine)
    {
        return new LspDiagnostic
        {
            Code = code,
            Message = $"Test diagnostic {code}",
            Range = new Roslyn.LanguageServer.Protocol.Range
            {
                Start = new Roslyn.LanguageServer.Protocol.Position(startLine, 0),
                End = new Roslyn.LanguageServer.Protocol.Position(endLine, 0)
            },
            Severity = LspDiagnosticSeverity.Error
        };
    }
}
