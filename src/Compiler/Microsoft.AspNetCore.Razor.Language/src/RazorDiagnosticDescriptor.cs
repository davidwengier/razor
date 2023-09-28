﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;

namespace Microsoft.AspNetCore.Razor.Language;

[DebuggerDisplay($"{{{nameof(DebuggerToString)}(),nq}}")]
public record RazorDiagnosticDescriptor
{
    public string Id { get; }
    public string MessageFormat { get; }
    public RazorDiagnosticSeverity Severity { get; }

    public RazorDiagnosticDescriptor(string id, string messageFormat, RazorDiagnosticSeverity severity)
    {
        if (string.IsNullOrEmpty(id))
        {
            throw new ArgumentNullException(nameof(id), Resources.ArgumentCannotBeNullOrEmpty);
        }

        Id = id;
        MessageFormat = messageFormat.IsNullOrEmpty()
            ? Resources.FormatRazorDiagnosticDescriptor_DefaultError(Id)
            : messageFormat;
        Severity = severity;
    }

    private string DebuggerToString() => $"""
        Error "{Id}": "{MessageFormat}"
        """;
}
