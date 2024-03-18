// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Razor.LanguageServer.Test;
using Microsoft.AspNetCore.Razor.Test.Common;
using Microsoft.AspNetCore.Razor.Test.Common.Workspaces;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.ExternalAccess.Razor;
using Microsoft.CodeAnalysis.Razor.Remote;
using Microsoft.CodeAnalysis.Remote.Razor;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Editor.Razor.Settings;
using Microsoft.VisualStudio.Editor.Razor.Test.Shared;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using Microsoft.VisualStudio.LanguageServerClient.Razor.Cohost;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.VisualStudio.LanguageServerClient.Razor.Test.Cohost;

public class CohostSemanticTokensRangeEndpointTest(ITestOutputHelper testOutputHelper) : WorkspaceTestBase(testOutputHelper)
{
    [Fact]
    public async Task Test()
    {
        var clientCapabilities = new VSInternalClientCapabilities
        {
            SupportsVisualStudioExtensions = true
        };
        var legend = new CohostSemanticTokensLegendService(new TestClientCapabilitiesService(clientCapabilities));

        var clientSettingsManager = new ClientSettingsManager([]);
        var remoteClientProvider = new TestRemoteClientProvider();
        var service = new OutOfProcSemanticTokensService(
            remoteClientProvider,
            clientSettingsManager,
            LoggerFactory);

        var endpoint = new CohostSemanticTokensRangeEndpoint(
             service,
            legend,
            new TestTelemetryReporter(LoggerFactory),
            LoggerFactory);

        var razorFile = new Uri(TestProjectData.SomeProjectComponentFile1.FilePath);

        var request = new SemanticTokensRangeParams()
        {
            Range = new Range
            {
                Start = new Position(0, 0),
                End = new Position(0, 0)
            },
            TextDocument = new TextDocumentIdentifier()
            {
                Uri = razorFile
            }
        };
        var document = InitializeDocument("""

            <div></div>

            @code
            {
                private string _value;
            }

            """);
        await endpoint.GetTestAccessor().HandleRequestAsync(request, document, DisposalToken);
    }

    private Document InitializeDocument(string input)
    {
        var hostProject = TestProjectData.SomeProject;
        var hostDocument = TestProjectData.SomeProjectComponentFile1;

        var sourceText = SourceText.From(input);

        var solution = Workspace.CurrentSolution.AddProject(ProjectInfo.Create(
            ProjectId.CreateNewId(Path.GetFileNameWithoutExtension(hostProject.FilePath)),
            VersionStamp.Create(),
            Path.GetFileNameWithoutExtension(hostDocument.FilePath),
            Path.GetFileNameWithoutExtension(hostDocument.FilePath),
            LanguageNames.CSharp,
            hostDocument.FilePath));

        var textAndVersion = TextAndVersion.Create(sourceText, VersionStamp.Default, hostDocument.FilePath);

        solution = solution.AddDocument(
            DocumentId.CreateNewId(solution.ProjectIds.Single(), hostDocument.FilePath),
            hostDocument.FilePath,
            TextLoader.From(textAndVersion));

        var document = solution.Projects.Single().Documents.Single();

        return document;
    }

    private class TestRemoteClientProvider : IRemoteClientProvider
    {
        public Task<RazorRemoteClientWrapper?> TryGetClientAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<RazorRemoteClientWrapper?>(TestRazorRemoteClientWrapper.Instance);
        }
    }

    private class TestRazorRemoteClientWrapper()
        : RazorRemoteClientWrapper(remoteClient: null!) // deliberately using null here so tests will fail if we haven't overridden a method
    {
        public static TestRazorRemoteClientWrapper Instance = new();

        // no solution, no callback:

        public override ValueTask<bool> TryInvokeAsync<TService>(Func<TService, CancellationToken, ValueTask> invocation, CancellationToken cancellationToken) where TService : class
            => throw new NotImplementedException("Not supported (yet? Be the change you want to see in the world!)");

        public override ValueTask<Optional<TResult>> TryInvokeAsync<TService, TResult>(Func<TService, CancellationToken, ValueTask<TResult>> invocation, CancellationToken cancellationToken) where TService : class
            => throw new NotImplementedException("Not supported (yet? Be the change you want to see in the world!)");

        // no solution, callback:

        public override ValueTask<bool> TryInvokeAsync<TService>(Func<TService, RazorRemoteServiceCallbackIdWrapper, CancellationToken, ValueTask> invocation, object callbackTarget, CancellationToken cancellationToken) where TService : class
            => throw new NotImplementedException("Not supported (yet? Be the change you want to see in the world!)");

        public override ValueTask<Optional<TResult>> TryInvokeAsync<TService, TResult>(Func<TService, RazorRemoteServiceCallbackIdWrapper, CancellationToken, ValueTask<TResult>> invocation, object callbackTarget, CancellationToken cancellationToken) where TService : class
            => throw new NotImplementedException("Not supported (yet? Be the change you want to see in the world!)");

        // solution, no callback:

        public override ValueTask<bool> TryInvokeAsync<TService>(Solution solution, Func<TService, RazorPinnedSolutionInfoWrapper, CancellationToken, ValueTask> invocation, CancellationToken cancellationToken) where TService : class
            => throw new NotImplementedException("Not supported (yet? Be the change you want to see in the world!)");

        public override ValueTask<Optional<TResult>> TryInvokeAsync<TService, TResult>(Solution solution, Func<TService, RazorPinnedSolutionInfoWrapper, CancellationToken, ValueTask<TResult>> invocation, CancellationToken cancellationToken) where TService : class
            => TryInvokeCore(solution, invocation);

        // solution, callback:

        public override ValueTask<bool> TryInvokeAsync<TService>(Solution solution, Func<TService, RazorPinnedSolutionInfoWrapper, RazorRemoteServiceCallbackIdWrapper, CancellationToken, ValueTask> invocation, object callbackTarget, CancellationToken cancellationToken) where TService : class
            => throw new NotImplementedException("Not supported (yet? Be the change you want to see in the world!)");

        public override ValueTask<Optional<TResult>> TryInvokeAsync<TService, TResult>(Solution solution, Func<TService, RazorPinnedSolutionInfoWrapper, RazorRemoteServiceCallbackIdWrapper, CancellationToken, ValueTask<TResult>> invocation, object callbackTarget, CancellationToken cancellationToken) where TService : class
            => throw new NotImplementedException("Not supported (yet? Be the change you want to see in the world!)");

        private ValueTask<Optional<TResult>> TryInvokeCore<TService, TResult>(Solution solution, Func<TService, RazorPinnedSolutionInfoWrapper, CancellationToken, ValueTask<TResult>> invocation) where TService : class
        {
            return default;
        }
    }
}
