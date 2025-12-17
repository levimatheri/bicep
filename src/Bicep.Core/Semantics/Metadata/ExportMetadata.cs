// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System.Collections.Immutable;
using System.Web.Services.Description;
using Bicep.Core.SourceGraph;
using Bicep.Core.TypeSystem;
using Bicep.Core.TypeSystem.Types;

namespace Bicep.Core.Semantics.Metadata;

public enum ExportMetadataKind
{
    Error = 0,
    Type,
    Variable,
    Function,
}

public abstract record ExportMetadata(ExportMetadataKind Kind, string Name, ITypeReference TypeReference, string? Description, ImmutableHashSet<BicepSourceFileKind>? ImportableFileTypes);

public record ExportedTypeMetadata(string Name, ITypeReference TypeReference, string? Description, ImmutableHashSet<BicepSourceFileKind>? ImportableFileTypes)
    : ExportMetadata(ExportMetadataKind.Type, Name, TypeReference, Description, ImportableFileTypes);

public record ExportedVariableMetadata(string Name, ITypeReference TypeReference, string? Description, ITypeReference? DeclaredType, ImmutableHashSet<BicepSourceFileKind>? ImportableFileTypes)
    : ExportMetadata(ExportMetadataKind.Variable, Name, TypeReference, Description, ImportableFileTypes);

public record ExportedFunctionParameterMetadata(string Name, ITypeReference TypeReference, string? Description);

public record ExportedFunctionReturnMetadata(ITypeReference TypeReference, string? Description);

public record ExportedFunctionMetadata(string Name, ImmutableArray<ExportedFunctionParameterMetadata> Parameters, ExportedFunctionReturnMetadata Return, string? Description, ImmutableHashSet<BicepSourceFileKind>? ImportableFileTypes)
    : ExportMetadata(ExportMetadataKind.Function, Name, new LambdaType([.. Parameters.Select(md => md.TypeReference)], [], Return.TypeReference), Description, ImportableFileTypes);

public record DuplicatedExportMetadata(string Name, ImmutableArray<string> ExportKindsWithSameName)
    : ExportMetadata(ExportMetadataKind.Error, Name, ErrorType.Empty(), $"The name \"{Name}\" is ambiguous because it refers to exports of the following kinds: {string.Join(", ", ExportKindsWithSameName)}.", []);
