﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license. See License.txt in the project root for license information.

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Razor.LanguageServer.Extensions;
using Microsoft.AspNetCore.Razor.LanguageServer.InlayHints;
using Microsoft.AspNetCore.Razor.Test.Common.Workspaces;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.AspNetCore.Razor.LanguageServer.Test.InlayHints;

public class InlayHintEndpointTest(ITestOutputHelper testOutput) : SingleServerDelegatingEndpointTestBase(testOutput)
{
    [Fact]
    public Task InlayHints()
        => VerifyInlayHintsAsync(
            """

            <div></div>

            @functions {
                private void M(string thisIsMyString)
                {
                    var {|int:x|} = 5;

                    var {|string:y|} = "Hello";

                    M({|thisIsMyString:"Hello"|});
                }
            }
            
            """);

    private async Task VerifyInlayHintsAsync(string input)
    {
        TestFileMarkupParser.GetSpans(input, out input, out ImmutableDictionary<string, ImmutableArray<TextSpan>> spansDict);
        var codeDocument = CreateCodeDocument(input);
        var razorFilePath = "C:/path/to/file.razor";

        var languageServer = await CreateLanguageServerAsync(codeDocument, razorFilePath);

        var endpoint = new InlayHintEndpoint(TestLanguageServerFeatureOptions.Instance, DocumentMappingService, languageServer);

        var request = new InlayHintParams()
        {
            TextDocument = new VSTextDocumentIdentifier
            {
                Uri = new Uri(razorFilePath)
            },
            Range = new()
            {
                Start = new(0, 0),
                End = new(codeDocument.Source.Text.Lines.Count, 0)
            }
        };
        var documentContext = DocumentContextFactory.TryCreateForOpenDocument(request.TextDocument);
        var requestContext = CreateRazorRequestContext(documentContext);

        // Act
        var hints = await endpoint.HandleRequestAsync(request, requestContext, DisposalToken);

        // Assert
        Assert.NotNull(hints);
        Assert.Equal(spansDict.Values.Count(), hints.Length);

        var sourceText = SourceText.From(input);
        foreach (var hint in hints)
        {
            // Because our test input data can't have colons in the input, but parameter info returned from Roslyn does, we have to strip them off.
            var label = hint.Label.First.TrimEnd(':');
            Assert.True(spansDict.TryGetValue(label, out var spans), $"Expected {label} to be in test provided markers");

            var span = Assert.Single(spans);
            var expectedRange = span.ToRange(sourceText);
            // Inlay hints only have a position, so we ignore the end of the range that comes from the test input
            Assert.Equal(expectedRange.Start, hint.Position);
        }
    }
}
