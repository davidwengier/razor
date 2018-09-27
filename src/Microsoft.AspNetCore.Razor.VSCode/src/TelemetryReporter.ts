/* --------------------------------------------------------------------------------------------
 * Copyright (c) Microsoft Corporation. All rights reserved.
 * Licensed under the MIT License. See License.txt in the project root for license information.
 * ------------------------------------------------------------------------------------------ */

import { Trace } from 'vscode-jsonrpc';
import { HostEventStream, TelemetryEvent } from './HostEventStream';

export class TelemetryReporter {
    private readonly razorDocuments: { [hostDocumentPath: string]: boolean } = {};
    private readonly documentOpenedEvent = new TelemetryEvent('VSCode.Razor.DocumentOpened');
    private readonly documentClosedEvent = new TelemetryEvent('VSCode.Razor.DocumentClosed');
    private readonly documentEditedAfterOpenEvent = new TelemetryEvent('VSCode.Razor.DocumentEditedAfterOpen');
    private readonly razorExtensionActivated = new TelemetryEvent('VSCode.Razor.RazorExtensionActivated');
    private readonly debugLanguageServerEvent = new TelemetryEvent('VSCode.Razor.DebugLanguageServer');
    private readonly workspaceContainsRazorEvent = new TelemetryEvent('VSCode.Razor.WorkspaceContainsRazor');
    private reportedWorkspaceContainsRazor = false;

    constructor(
        private readonly eventStream: HostEventStream) {
        // If this telemetry reporter is created it means the rest of the Razor extension world was created.
        this.eventStream.post(this.razorExtensionActivated);
    }

    public reportTraceLevel(trace: Trace) {
        const traceLevelEvent = new TelemetryEvent(
            'VSCode.Razor.TraceLevel',
            {
                trace: Trace[trace],
            });
        this.eventStream.post(traceLevelEvent);
    }

    public reportErrorOnServerStart(error: Error) {
        this.reportError('VSCode.Razor.ErrorOnServerStart', error);
    }

    public reportErrorOnActivation(error: Error) {
        this.reportError('VSCode.Razor.ErrorOnActivation', error);
    }

    public reportDebugLanguageServer() {
        this.eventStream.post(this.debugLanguageServerEvent);
    }

    public reportWorkspaceContainsRazor() {
        if (this.reportedWorkspaceContainsRazor) {
            return;
        }

        this.reportedWorkspaceContainsRazor = true;
        this.eventStream.post(this.workspaceContainsRazorEvent);
    }

    public reportDocumentOpened(path: string) {
        this.eventStream.post(this.documentOpenedEvent);
    }

    public reportDocumentClosed(path: string) {
        delete this.razorDocuments[path];
        this.eventStream.post(this.documentClosedEvent);
    }

    public reportDocumentEdited(path: string) {
        if (this.razorDocuments[path] === undefined) {
            this.razorDocuments[path] = true;

            // Only report the first edit to a document when its opened.
            this.eventStream.post(this.documentEditedAfterOpenEvent);
        }
    }

    private reportError(eventName: string, error: Error) {
        const errorOnActivationEvent = new TelemetryEvent(
            eventName,
            {
                error: JSON.stringify(error),
            });

        this.eventStream.post(errorOnActivationEvent);
    }
}
