// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Razor.Protocol;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using StreamJsonRpc;
using static Microsoft.VisualStudio.VSConstants;

namespace Microsoft.VisualStudio.Razor.LanguageClient.Endpoints;

internal partial class RazorCustomMessageTarget
{
    [JsonRpcMethod(CustomMessageNames.RazorRenameEndpointName, UseSingleObjectParameterDeserialization = true)]
    public async Task<WorkspaceEdit?> RenameAsync(DelegatedRenameParams request, CancellationToken cancellationToken)
    {
        var delegationDetails = await GetProjectedRequestDetailsAsync(request, cancellationToken).ConfigureAwait(false);
        if (delegationDetails is null)
        {
            return null;
        }

        var renameParams = new RenameParams()
        {
            TextDocument = request.Identifier.TextDocumentIdentifier.WithUri(delegationDetails.Value.ProjectedUri),
            Position = request.ProjectedPosition,
            NewName = request.NewName,
        };

        var textBuffer = delegationDetails.Value.TextBuffer;
        var response = await _requestInvoker.ReinvokeRequestOnServerAsync<RenameParams, WorkspaceEdit?>(
            textBuffer,
            Methods.TextDocumentRenameName,
            delegationDetails.Value.LanguageServerName,
            renameParams,
            cancellationToken).ConfigureAwait(false);
        return response?.Response;
    }

    [JsonRpcMethod("razor/initiateRename", UseSingleObjectParameterDeserialization = true)]
    public async Task InitiateRenameAsync(object[] data, CancellationToken cancellationToken)
    {
        var commandGuid = typeof(VSStd2KCmdID).GUID;
        var commandId = VSStd2KCmdID.RENAME;

        await _joinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        var edit = data[0] as WorkspaceEdit;

        var serviceProvider = (IAsyncServiceProvider?)await AsyncServiceProvider.GlobalProvider.GetServiceAsync(typeof(SAsyncServiceProvider)).WithCancellation(cancellationToken);
        var dispatcher = (IOleCommandTarget?)await serviceProvider!.GetServiceAsync(typeof(SUIHostCommandDispatcher)).WithCancellation(cancellationToken);
        ErrorHandler.ThrowOnFailure(dispatcher!.Exec(commandGuid, (uint)commandId, (uint)OLECMDEXECOPT.OLECMDEXECOPT_DODEFAULT, IntPtr.Zero, IntPtr.Zero));
    }
}
