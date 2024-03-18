// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license. See License.txt in the project root for license information.

using System.Threading.Tasks;
using System.Threading;
using System;
using Microsoft.CodeAnalysis.ExternalAccess.Razor;

namespace Microsoft.CodeAnalysis.Razor.Remote;

internal class RazorRemoteClientWrapper(RazorRemoteHostClient remoteClient)
{
    // Virtual methods, for testing

    // no solution, no callback:

    public virtual ValueTask<bool> TryInvokeAsync<TService>(Func<TService, CancellationToken, ValueTask> invocation, CancellationToken cancellationToken) where TService : class
        => remoteClient.TryInvokeAsync(invocation, cancellationToken);

    public virtual ValueTask<Optional<TResult>> TryInvokeAsync<TService, TResult>(Func<TService, CancellationToken, ValueTask<TResult>> invocation, CancellationToken cancellationToken) where TService : class
        => remoteClient.TryInvokeAsync(invocation, cancellationToken);

    // no solution, callback:

    public virtual ValueTask<bool> TryInvokeAsync<TService>(Func<TService, RazorRemoteServiceCallbackIdWrapper, CancellationToken, ValueTask> invocation, object callbackTarget, CancellationToken cancellationToken) where TService : class
        => remoteClient.TryInvokeAsync(invocation, callbackTarget, cancellationToken);

    public virtual ValueTask<Optional<TResult>> TryInvokeAsync<TService, TResult>(Func<TService, RazorRemoteServiceCallbackIdWrapper, CancellationToken, ValueTask<TResult>> invocation, object callbackTarget, CancellationToken cancellationToken) where TService : class
        => remoteClient.TryInvokeAsync(invocation, callbackTarget, cancellationToken);

    // solution, no callback:

    public virtual ValueTask<bool> TryInvokeAsync<TService>(Solution solution, Func<TService, RazorPinnedSolutionInfoWrapper, CancellationToken, ValueTask> invocation, CancellationToken cancellationToken) where TService : class
        => remoteClient.TryInvokeAsync(solution, invocation, cancellationToken);

    public virtual ValueTask<Optional<TResult>> TryInvokeAsync<TService, TResult>(Solution solution, Func<TService, RazorPinnedSolutionInfoWrapper, CancellationToken, ValueTask<TResult>> invocation, CancellationToken cancellationToken) where TService : class
        => remoteClient.TryInvokeAsync(solution, invocation, cancellationToken);

    // solution, callback:

    public virtual ValueTask<bool> TryInvokeAsync<TService>(Solution solution, Func<TService, RazorPinnedSolutionInfoWrapper, RazorRemoteServiceCallbackIdWrapper, CancellationToken, ValueTask> invocation, object callbackTarget, CancellationToken cancellationToken) where TService : class
        => remoteClient.TryInvokeAsync(solution, invocation, callbackTarget, cancellationToken);

    public virtual ValueTask<Optional<TResult>> TryInvokeAsync<TService, TResult>(Solution solution, Func<TService, RazorPinnedSolutionInfoWrapper, RazorRemoteServiceCallbackIdWrapper, CancellationToken, ValueTask<TResult>> invocation, object callbackTarget, CancellationToken cancellationToken) where TService : class
        => remoteClient.TryInvokeAsync(solution, invocation, callbackTarget, cancellationToken);
}
