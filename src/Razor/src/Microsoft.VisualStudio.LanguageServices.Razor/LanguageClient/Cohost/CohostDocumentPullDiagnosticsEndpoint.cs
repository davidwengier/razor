// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Razor;
using Microsoft.AspNetCore.Razor.PooledObjects;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.ExternalAccess.Razor.Cohost;
using Microsoft.CodeAnalysis.Razor.Cohost;
using Microsoft.CodeAnalysis.Razor.Logging;
using Microsoft.CodeAnalysis.Razor.Protocol;
using Microsoft.CodeAnalysis.Razor.Remote;
using Microsoft.CodeAnalysis.Razor.Telemetry;
using Microsoft.CodeAnalysis.Razor.Workspaces.Settings;
using ExternalHandlers = Microsoft.CodeAnalysis.ExternalAccess.Razor.Cohost.Handlers;

namespace Microsoft.VisualStudio.Razor.LanguageClient.Cohost;

#pragma warning disable RS0030 // Do not use banned APIs
[Shared]
[CohostEndpoint(VSInternalMethods.DocumentPullDiagnosticName)]
[Export(typeof(IDynamicRegistrationProvider))]
[ExportCohostStatelessLspService(typeof(CohostDocumentPullDiagnosticsEndpoint))]
[method: ImportingConstructor]
#pragma warning restore RS0030 // Do not use banned APIs
internal sealed class CohostDocumentPullDiagnosticsEndpoint(
    IIncompatibleProjectService incompatibleProjectService,
    IRemoteServiceInvoker remoteServiceInvoker,
    IHtmlRequestInvoker requestInvoker,
    IClientSettingsManager clientSettingsManager,
    IClientCapabilitiesService clientCapabilitiesService,
    IDiagnosticsCacheService diagnosticsCacheService,
    ITelemetryReporter telemetryReporter,
    ILoggerFactory loggerFactory)
    : CohostDocumentPullDiagnosticsEndpointBase<VSInternalDocumentDiagnosticsParams, VSInternalDiagnosticReport[]>(
        incompatibleProjectService,
        remoteServiceInvoker,
        requestInvoker,
        clientCapabilitiesService,
        diagnosticsCacheService,
        telemetryReporter,
        loggerFactory.GetOrCreateLogger<CohostDocumentPullDiagnosticsEndpoint>()), IDynamicRegistrationProvider
{
    private readonly IRemoteServiceInvoker _remoteServiceInvoker = remoteServiceInvoker;
    private readonly IClientSettingsManager _clientSettingsManager = clientSettingsManager;
    private readonly IClientCapabilitiesService _clientCapabilitiesService = clientCapabilitiesService;

    protected override string LspMethodName => VSInternalMethods.DocumentPullDiagnosticName;
    protected override bool SupportsHtmlDiagnostics => true;

    public ImmutableArray<Registration> GetRegistrations(VSInternalClientCapabilities clientCapabilities, RazorCohostRequestContext requestContext)
    {
        if (clientCapabilities.TextDocument?.Diagnostic?.DynamicRegistration is true)
        {
            return [new Registration()
            {
                Method = VSInternalMethods.DocumentPullDiagnosticName,
                RegisterOptions = new VSInternalDiagnosticRegistrationOptions()
                {
                    DiagnosticKinds = [VSInternalDiagnosticKind.Syntax, VSInternalDiagnosticKind.Task]
                }
            }];
        }

        return [];
    }

    protected override RazorTextDocumentIdentifier? GetRazorTextDocumentIdentifier(VSInternalDocumentDiagnosticsParams request)
        => request.TextDocument?.ToRazorTextDocumentIdentifier();

    protected async override Task<VSInternalDiagnosticReport[]?> HandleRequestAsync(VSInternalDocumentDiagnosticsParams request, TextDocument razorDocument, CancellationToken cancellationToken)
    {
        if (request.QueryingDiagnosticKind?.Value == VSInternalDiagnosticKind.Task.Value)
        {
            return await HandleTaskListItemRequestAsync(
                razorDocument,
                _clientSettingsManager.GetClientSettings().AdvancedSettings.TaskListDescriptors,
                cancellationToken).ConfigureAwait(false);
        }

        var previousResultId = request.PreviousResultId;

        var diagnosticsResult = await GetVSDiagnosticsAsync(razorDocument, previousResultId, cancellationToken).ConfigureAwait(false);
        if (diagnosticsResult is null)
        {
            return null;
        }

        return [new()
        {
            Diagnostics = diagnosticsResult.Value.Diagnostics,
            ResultId = diagnosticsResult.Value.ResultId
        }];
    }

    private async Task<(LspDiagnostic[]? Diagnostics, string ResultId)?> GetVSDiagnosticsAsync(TextDocument razorDocument, string? previousResultId, CancellationToken cancellationToken)
    {
        var result = await GetDiagnosticsAsync(razorDocument, previousResultId, cancellationToken).ConfigureAwait(false);
        if (result is null)
        {
            return null;
        }

        // If unchanged, return with same resultId but still need to provide empty array for VS
        if (result.Value.IsUnchanged)
        {
            return ([], result.Value.ResultId);
        }

        var diagnostics = result.Value.Diagnostics!.Value;

        // We always use Roslyn's project understanding, and in VS the project Id is not necessarily the Id that is reported by Roslyn
        // for diagnostics. Rather than try to replicate any of this behaviour directly, we just take Roslyn as the source of truth,
        // and force the project information to match what it would produce, regardless of where it comes from or how we might have
        // filtered or converted it.
        var projectInfo = new[] { ExternalHandlers.Diagnostics.GetProjectInformation(razorDocument.Project) };

        var results = new VSDiagnostic[diagnostics.Length];
        for (var i = 0; i < diagnostics.Length; i++)
        {
            var vsDiagnostic = JsonHelpers.Convert<LspDiagnostic, VSDiagnostic>(diagnostics[i]).AssumeNotNull();
            vsDiagnostic.Projects = projectInfo;

            // Setting a unique identifier ensures that VS will show project info in the error list, and things like the "Current Project"
            // filter will work. Putting the Razor file path in the identifier ensures that files in multiple projects get their diagnostics
            // de-duped.
            vsDiagnostic.Identifier = (vsDiagnostic.Code, razorDocument.FilePath, vsDiagnostic.Range, vsDiagnostic.Message).GetHashCode().ToString();

            results[i] = vsDiagnostic;
        }

        return (results, result.Value.ResultId);
    }

    protected override VSInternalDocumentDiagnosticsParams CreateHtmlParams(Uri uri, string? previousResultId)
    {
        return new VSInternalDocumentDiagnosticsParams
        {
            TextDocument = new TextDocumentIdentifier { DocumentUri = new(uri) },
            PreviousResultId = previousResultId
        };
    }

    protected override LspDiagnostic[] ExtractHtmlDiagnostics(VSInternalDiagnosticReport[] result)
    {
        using var allDiagnostics = new PooledArrayBuilder<LspDiagnostic>();
        foreach (var report in result)
        {
            if (report.Diagnostics is not null)
            {
                allDiagnostics.AddRange(report.Diagnostics);
            }
        }

        return allDiagnostics.ToArray();
    }

    protected override string? ExtractHtmlResultId(VSInternalDiagnosticReport[] result)
    {
        // VS Internal diagnostics returns an array of reports, each with its own ResultId
        // We use the first non-null ResultId as the representative for caching
        foreach (var report in result)
        {
            if (report.ResultId is not null)
            {
                return report.ResultId;
            }
        }

        return null;
    }

    protected override bool IsHtmlResponseUnchanged(VSInternalDiagnosticReport[] result, string? previousResultId)
    {
        // If we have a previous result ID and the response contains no diagnostics
        // with a matching result ID, it indicates unchanged
        if (previousResultId is null)
        {
            return false;
        }

        foreach (var report in result)
        {
            // If we get an empty diagnostics array with the same result ID, it's unchanged
            if (report.ResultId == previousResultId && (report.Diagnostics is null || report.Diagnostics.Length == 0))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<VSInternalDiagnosticReport[]> HandleTaskListItemRequestAsync(TextDocument razorDocument, ImmutableArray<string> taskListDescriptors, CancellationToken cancellationToken)
    {
        var csharpTaskItems = await GetCSharpTaskListItemsAsync(razorDocument, cancellationToken).ConfigureAwait(false);

        var diagnostics = await _remoteServiceInvoker.TryInvokeAsync<IRemoteDiagnosticsService, ImmutableArray<LspDiagnostic>>(
            razorDocument.Project.Solution,
            (service, solutionInfo, cancellationToken) => service.GetTaskListDiagnosticsAsync(solutionInfo, razorDocument.Id, taskListDescriptors, csharpTaskItems, cancellationToken),
            cancellationToken).ConfigureAwait(false);

        if (diagnostics.IsDefaultOrEmpty)
        {
            return [];
        }

        return
        [
            new()
            {
                Diagnostics = [.. diagnostics],
                ResultId = Guid.NewGuid().ToString()
            }
        ];
    }

    private async Task<LspDiagnostic[]> GetCSharpTaskListItemsAsync(TextDocument razorDocument, CancellationToken cancellationToken)
    {
        var generatedDocument = await TryGetGeneratedDocumentAsync(razorDocument, cancellationToken).ConfigureAwait(false);
        if (generatedDocument is null)
        {
            return [];
        }

        var supportsVisualStudioExtensions = _clientCapabilitiesService.ClientCapabilities.SupportsVisualStudioExtensions;
        var csharpTaskItems = await ExternalHandlers.Diagnostics.GetTaskListAsync(generatedDocument, supportsVisualStudioExtensions, cancellationToken).ConfigureAwait(false);
        return [.. csharpTaskItems];
    }

    internal TestAccessor GetTestAccessor() => new(this);

    internal readonly struct TestAccessor(CohostDocumentPullDiagnosticsEndpoint instance)
    {
        public async Task<LspDiagnostic[]?> HandleRequestAsync(TextDocument razorDocument, CancellationToken cancellationToken)
        {
            var result = await instance.GetVSDiagnosticsAsync(razorDocument, previousResultId: null, cancellationToken).ConfigureAwait(false);
            return result?.Diagnostics;
        }

        public async Task<(LspDiagnostic[]? Diagnostics, string? ResultId)> HandleRequestWithResultIdAsync(TextDocument razorDocument, string? previousResultId, CancellationToken cancellationToken)
        {
            var result = await instance.GetVSDiagnosticsAsync(razorDocument, previousResultId, cancellationToken).ConfigureAwait(false);
            return (result?.Diagnostics, result?.ResultId);
        }

        public Task<VSInternalDiagnosticReport[]> HandleTaskListItemRequestAsync(TextDocument razorDocument, ImmutableArray<string> taskListDescriptors, CancellationToken cancellationToken)
            => instance.HandleTaskListItemRequestAsync(razorDocument, taskListDescriptors, cancellationToken);
    }
}

