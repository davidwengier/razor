// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.ExternalAccess.Razor.Cohost;
using Microsoft.CodeAnalysis.ExternalAccess.Razor.Features;
using Microsoft.CodeAnalysis.Razor.Cohost;
using Microsoft.CodeAnalysis.Razor.Logging;
using Microsoft.CodeAnalysis.Razor.Protocol;
using Microsoft.CodeAnalysis.Razor.Remote;
using Microsoft.CodeAnalysis.Razor.Telemetry;
using Roslyn.LanguageServer.Protocol;

namespace Microsoft.VisualStudio.Razor.LanguageClient.Cohost;

#pragma warning disable RS0030 // Do not use banned APIs
[Shared]
[CohostEndpoint(Methods.TextDocumentDiagnosticName)]
[Export(typeof(IDynamicRegistrationProvider))]
[ExportRazorStatelessLspService(typeof(DocumentPullDiagnosticsEndpoint))]
[method: ImportingConstructor]
#pragma warning restore RS0030 // Do not use banned APIs
internal sealed class DocumentPullDiagnosticsEndpoint(
    IIncompatibleProjectService incompatibleProjectService,
    IRemoteServiceInvoker remoteServiceInvoker,
    IHtmlRequestInvoker requestInvoker,
    IClientCapabilitiesService clientCapabilitiesService,
    IDiagnosticsCacheService diagnosticsCacheService,
    ITelemetryReporter telemetryReporter,
    ILoggerFactory loggerFactory)
    : CohostDocumentPullDiagnosticsEndpointBase<DocumentDiagnosticParams, SumType<FullDocumentDiagnosticReport, UnchangedDocumentDiagnosticReport>?>(
        incompatibleProjectService,
        remoteServiceInvoker,
        requestInvoker,
        clientCapabilitiesService,
        diagnosticsCacheService,
        telemetryReporter,
        loggerFactory.GetOrCreateLogger<DocumentPullDiagnosticsEndpoint>()), IDynamicRegistrationProvider
{
    protected override string LspMethodName => Methods.TextDocumentDiagnosticName;
    protected override bool SupportsHtmlDiagnostics => false;

    public ImmutableArray<Registration> GetRegistrations(VSInternalClientCapabilities clientCapabilities, RazorCohostRequestContext requestContext)
    {
        if (clientCapabilities.TextDocument?.Diagnostic?.DynamicRegistration is true)
        {
            return [new Registration()
            {
                Method = Methods.TextDocumentDiagnosticName,
                RegisterOptions = new DiagnosticRegistrationOptions()
                {
                    WorkspaceDiagnostics = false
                }
            }];
        }

        return [];
    }

    protected override RazorTextDocumentIdentifier? GetRazorTextDocumentIdentifier(DocumentDiagnosticParams request)
        => request.TextDocument?.ToRazorTextDocumentIdentifier();

    protected async override Task<SumType<FullDocumentDiagnosticReport, UnchangedDocumentDiagnosticReport>?> HandleRequestAsync(DocumentDiagnosticParams request, TextDocument razorDocument, CancellationToken cancellationToken)
    {
        var result = await GetDiagnosticsAsync(razorDocument, request.PreviousResultId, cancellationToken).ConfigureAwait(false);

        if (result is null)
        {
            return null;
        }

        if (result.Value.IsUnchanged)
        {
            return new UnchangedDocumentDiagnosticReport
            {
                ResultId = result.Value.ResultId
            };
        }

        return new FullDocumentDiagnosticReport
        {
            Items = result.Value.Diagnostics is { } diagnostics ? [.. diagnostics] : [],
            ResultId = result.Value.ResultId
        };
    }

    internal TestAccessor GetTestAccessor() => new(this);

    internal readonly struct TestAccessor(DocumentPullDiagnosticsEndpoint instance)
    {
        public async Task<LspDiagnostic[]?> HandleRequestAsync(TextDocument razorDocument, CancellationToken cancellationToken)
        {
            var result = await instance.GetDiagnosticsAsync(razorDocument, previousResultId: null, cancellationToken).ConfigureAwait(false);
            return result?.Diagnostics is { } diagnostics ? [.. diagnostics] : null;
        }
    }
}
