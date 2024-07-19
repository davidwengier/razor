﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license. See License.txt in the project root for license information.

#nullable disable

using System;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Razor.ProjectSystem;
using Microsoft.CodeAnalysis.Razor.Workspaces;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.AspNetCore.Razor.LanguageServer.SignatureHelp;

public class SignatureHelpEndpointTest(ITestOutputHelper testOutput) : SingleServerDelegatingEndpointTestBase(testOutput)
{
    [Fact]
    public async Task Handle_SingleServer_CSharpSignature()
    {
        var input = """
                <div></div>

                @{
                    string M1(int i) => throw new NotImplementedException();

                    void Act()
                    {
                        M1($$);
                    }
                }
                """;

        await VerifySignatureHelpAsync(input, "string M1(int i)");
    }

    [Fact]
    public async Task Handle_SingleServerWithAutoListParamsDisabled_ReturnNull()
    {
        var input = """
                <div></div>

                @{
                    string M1(int i) => throw new NotImplementedException();

                    void Act()
                    {
                        M1($$);
                    }
                }
                """;

        var context = new SignatureHelpContext() { TriggerKind = SignatureHelpTriggerKind.TriggerCharacter };
        var optionsMonitor = GetOptionsMonitor(autoListParams: false);

        await VerifySignatureHelpWithContextAndOptionsAsync(input, optionsMonitor, context);
    }

    [Fact]
    public async Task Handle_SingleServer_CSharpSignature_Razor()
    {
        var input = """
                <div>@GetDiv($$)</div>

                @{
                    string GetDiv() => "";
                }
                """;

        await VerifySignatureHelpAsync(input, "string GetDiv()");
    }

    [Fact]
    public async Task Handle_SingleServer_ReturnNull()
    {
        var input = """
                <div>@GetDiv($$)</div>

                @{
                }
                """;

        await VerifySignatureHelpAsync(input);
    }

    private Task VerifySignatureHelpAsync(string input, params string[] signatures)
    {
        return VerifySignatureHelpWithContextAndOptionsAsync(input, optionsMonitor: null, signatureHelpContext: null, signatures);
    }

    private async Task VerifySignatureHelpWithContextAndOptionsAsync(string input, RazorLSPOptionsMonitor optionsMonitor = null, SignatureHelpContext signatureHelpContext = null, params string[] signatures)
    {
        // Arrange
        TestFileMarkupParser.GetPositionAndSpans(input, out var output, out int cursorPosition, out ImmutableArray<TextSpan> _);
        var codeDocument = CreateCodeDocument(output);
        var razorFilePath = "C:/path/to/file.razor";

        var languageServer = await CreateLanguageServerAsync(codeDocument, razorFilePath);

        optionsMonitor ??= GetOptionsMonitor();
        var endpoint = new SignatureHelpEndpoint(
            LanguageServerFeatureOptions, DocumentMappingService, languageServer, optionsMonitor, LoggerFactory);

        var (line, offset) = codeDocument.GetSourceText().GetLineAndOffset(cursorPosition);
        var request = new SignatureHelpParams
        {
            TextDocument = new TextDocumentIdentifier
            {
                Uri = new Uri(razorFilePath)
            },
            Position = new Position(line, offset),
            Context = signatureHelpContext
        };

        Assert.True(DocumentContextFactory.TryCreateForOpenDocument(request.TextDocument, out var documentContext));

        var requestContext = CreateRazorRequestContext(documentContext);

        // Act
        var result = await endpoint.HandleRequestAsync(request, requestContext, DisposalToken);

        // Assert
        if (signatures.Length == 0)
        {
            Assert.Null(result);
            return;
        }

        Assert.Equal(signatures.Length, result.Signatures.Length);
        for (var i = 0; i < signatures.Length; i++)
        {
            var expected = signatures[i];
            var actual = result.Signatures[i];

            Assert.Equal(expected, actual.Label);
        }
    }
}
