// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Razor.LanguageServer.EndpointContracts;
using Microsoft.AspNetCore.Razor.LanguageServer.Hosting;
using Microsoft.AspNetCore.Razor.Threading;
using Microsoft.CodeAnalysis.Razor.DocumentMapping;
using Microsoft.CodeAnalysis.Razor.Logging;
using Microsoft.CodeAnalysis.Razor.Workspaces;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using Microsoft.VisualStudio.Threading;

namespace Microsoft.AspNetCore.Razor.LanguageServer.Refactoring;

[RazorLanguageServerEndpoint(Methods.WorkspaceExecuteCommandName)]
internal sealed class CommandEndpoint(
    IClientConnection clientConnection)
    : IRazorDocumentlessRequestHandler<ExecuteCommandParams, object?>, ICapabilitiesProvider
{
    private readonly IClientConnection _clientConnection = clientConnection;

    public bool MutatesSolutionState => false;

    public void ApplyCapabilities(VSInternalServerCapabilities serverCapabilities, VSInternalClientCapabilities clientCapabilities)
    {
        serverCapabilities.ExecuteCommandProvider = new ExecuteCommandOptions
        {
            Commands = ["razor/initiateRename"]
        };
    }

    public Task<object?> HandleRequestAsync(ExecuteCommandParams request, RazorRequestContext context, CancellationToken cancellationToken)
    {
        _clientConnection.SendNotificationAsync("razor/initiateRename", request.Arguments, cancellationToken).Forget();

        return SpecializedTasks.Null<object>();
    }
}
