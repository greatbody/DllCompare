using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace DllCompare;

public sealed record AssemblyComparisonResult(
    AssemblyMetadataDocument LeftDocument,
    AssemblyMetadataDocument RightDocument,
    IReadOnlyList<string> AddedNamespaces,
    IReadOnlyList<string> RemovedNamespaces,
    IReadOnlyList<string> AddedTypes,
    IReadOnlyList<string> RemovedTypes,
    IReadOnlyList<string> AddedMethods,
    IReadOnlyList<string> RemovedMethods)
{
    public string LeftName => LeftDocument.Name;
    public string RightName => RightDocument.Name;

    public bool HasDifferences =>
        AddedNamespaces.Count > 0 ||
        RemovedNamespaces.Count > 0 ||
        AddedTypes.Count > 0 ||
        RemovedTypes.Count > 0 ||
        AddedMethods.Count > 0 ||
        RemovedMethods.Count > 0;
}

public sealed record AssemblyMetadataDocument(string Name, IReadOnlyList<NamespaceMetadataNode> Namespaces)
{
    public IReadOnlySet<string> NamespaceKeys { get; } = Namespaces.Select(node => node.Key).ToHashSet(StringComparer.Ordinal);
    public IReadOnlySet<string> TypeKeys { get; } = Namespaces.SelectMany(node => node.Types).Select(node => node.Key).ToHashSet(StringComparer.Ordinal);
    public IReadOnlySet<string> MethodKeys { get; } = Namespaces.SelectMany(ns => ns.Types).SelectMany(type => type.Methods).Select(node => node.Key).ToHashSet(StringComparer.Ordinal);
}

public sealed record NamespaceMetadataNode(string Key, string DisplayName, IReadOnlyList<TypeMetadataNode> Types);

public sealed record TypeMetadataNode(string Key, string FullName, string DisplayName, IReadOnlyList<MethodMetadataNode> Methods);

public sealed record MethodMetadataNode(string Key, string DisplayName);

public static class AssemblyMetadataComparer
{
    public static AssemblyComparisonResult Compare(string leftPath, string rightPath)
    {
        AssemblyMetadataDocument left = AssemblyShapeReader.Read(leftPath);
        AssemblyMetadataDocument right = AssemblyShapeReader.Read(rightPath);

        return new AssemblyComparisonResult(
            left,
            right,
            AddedNamespaces: ExceptSorted(right.NamespaceKeys, left.NamespaceKeys),
            RemovedNamespaces: ExceptSorted(left.NamespaceKeys, right.NamespaceKeys),
            AddedTypes: ExceptSorted(right.TypeKeys, left.TypeKeys),
            RemovedTypes: ExceptSorted(left.TypeKeys, right.TypeKeys),
            AddedMethods: ExceptSorted(right.MethodKeys, left.MethodKeys),
            RemovedMethods: ExceptSorted(left.MethodKeys, right.MethodKeys));
    }

    public static AssemblyMetadataDocument Read(string path) => AssemblyShapeReader.Read(path);

    private static IReadOnlyList<string> ExceptSorted(IReadOnlySet<string> first, IReadOnlySet<string> second) =>
        first.Except(second, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
}

internal static class AssemblyShapeReader
{
    public static AssemblyMetadataDocument Read(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using PEReader peReader = new(stream);

        if (!peReader.HasMetadata)
        {
            throw new InvalidOperationException($"'{Path.GetFileName(path)}' does not contain .NET metadata.");
        }

        MetadataReader reader = peReader.GetMetadataReader();
        string assemblyName = reader.IsAssembly
            ? reader.GetString(reader.GetAssemblyDefinition().Name)
            : Path.GetFileNameWithoutExtension(path);

        Dictionary<string, List<TypeMetadataNode>> namespaces = new(StringComparer.Ordinal);
        SignatureTypeNameProvider typeNameProvider = new(reader);

        foreach (TypeDefinitionHandle typeHandle in reader.TypeDefinitions)
        {
            TypeDefinition type = reader.GetTypeDefinition(typeHandle);
            string fullTypeName = GetTypeName(reader, typeHandle);

            if (fullTypeName == "<Module>")
            {
                continue;
            }

            string ns = reader.GetString(type.Namespace);
            string namespaceKey = string.IsNullOrWhiteSpace(ns) ? "<global>" : ns;
            string typeAttributes = FormatTypeAttributes(type.Attributes);
            string typeKey = $"{typeAttributes} {fullTypeName}";
            string typeDisplayName = $"{typeAttributes} {GetDisplayTypeName(fullTypeName, namespaceKey)}";
            List<MethodMetadataNode> methods = new();

            foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
            {
                MethodDefinition method = reader.GetMethodDefinition(methodHandle);
                string methodName = reader.GetString(method.Name);
                MethodSignature<string> signature = method.DecodeSignature(typeNameProvider, genericContext: null);
                string genericArity = method.GetGenericParameters().Count == 0
                    ? string.Empty
                    : $"`{method.GetGenericParameters().Count}";
                Dictionary<int, Parameter> parameterMetadata = method.GetParameters()
                    .Select(reader.GetParameter)
                    .Where(parameter => parameter.SequenceNumber > 0)
                    .ToDictionary(parameter => parameter.SequenceNumber);
                string parameters = string.Join(", ", signature.ParameterTypes.Select((type, index) =>
                    FormatParameterType(type, parameterMetadata.GetValueOrDefault(index + 1))));
                string methodDisplayName = $"{FormatMethodAttributes(method.Attributes)} {methodName}{genericArity}({parameters}) : {signature.ReturnType}";

                methods.Add(new MethodMetadataNode($"{fullTypeName}.{methodDisplayName}", methodDisplayName));
            }

            if (!namespaces.TryGetValue(namespaceKey, out List<TypeMetadataNode>? types))
            {
                types = new List<TypeMetadataNode>();
                namespaces.Add(namespaceKey, types);
            }

            types.Add(new TypeMetadataNode(
                typeKey,
                fullTypeName,
                typeDisplayName,
                methods.OrderBy(method => method.DisplayName, StringComparer.Ordinal).ToArray()));
        }

        NamespaceMetadataNode[] namespaceNodes = namespaces
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new NamespaceMetadataNode(
                pair.Key,
                pair.Key,
                pair.Value.OrderBy(type => type.FullName, StringComparer.Ordinal).ToArray()))
            .ToArray();

        return new AssemblyMetadataDocument(assemblyName, namespaceNodes);
    }

    private static string GetDisplayTypeName(string fullTypeName, string namespaceKey)
    {
        if (namespaceKey == "<global>" || !fullTypeName.StartsWith(namespaceKey + ".", StringComparison.Ordinal))
        {
            return fullTypeName;
        }

        return fullTypeName[(namespaceKey.Length + 1)..];
    }

    private static string FormatParameterType(string type, Parameter parameter)
    {
        if (!type.StartsWith("ref ", StringComparison.Ordinal))
        {
            return type;
        }

        string elementType = type[4..];
        return parameter.Attributes switch
        {
            var attributes when attributes.HasFlag(ParameterAttributes.Out) && !attributes.HasFlag(ParameterAttributes.In) => $"out {elementType}",
            var attributes when attributes.HasFlag(ParameterAttributes.In) && !attributes.HasFlag(ParameterAttributes.Out) => $"in {elementType}",
            _ => type
        };
    }

    private static string FormatTypeAttributes(TypeAttributes attributes)
    {
        List<string> parts = new() { FormatTypeVisibility(attributes) };

        if (attributes.HasFlag(TypeAttributes.Interface))
        {
            parts.Add("interface");
        }
        else if (attributes.HasFlag(TypeAttributes.Abstract) && attributes.HasFlag(TypeAttributes.Sealed))
        {
            parts.Add("static class");
        }
        else if (attributes.HasFlag(TypeAttributes.Abstract))
        {
            parts.Add("abstract class");
        }
        else if (attributes.HasFlag(TypeAttributes.Sealed))
        {
            parts.Add("sealed class");
        }
        else
        {
            parts.Add("class");
        }

        return string.Join(" ", parts.Where(part => !string.IsNullOrEmpty(part)));
    }

    private static string FormatTypeVisibility(TypeAttributes attributes) => (attributes & TypeAttributes.VisibilityMask) switch
    {
        TypeAttributes.Public => "public",
        TypeAttributes.NestedPublic => "public",
        TypeAttributes.NestedPrivate => "private",
        TypeAttributes.NestedFamily => "protected",
        TypeAttributes.NestedAssembly => "internal",
        TypeAttributes.NestedFamORAssem => "protected internal",
        TypeAttributes.NestedFamANDAssem => "private protected",
        _ => "internal"
    };

    private static string FormatMethodAttributes(MethodAttributes attributes)
    {
        List<string> parts = new() { FormatMethodVisibility(attributes) };

        if (attributes.HasFlag(MethodAttributes.Static))
        {
            parts.Add("static");
        }

        if (attributes.HasFlag(MethodAttributes.Abstract))
        {
            parts.Add("abstract");
        }
        else if (attributes.HasFlag(MethodAttributes.Virtual))
        {
            parts.Add(attributes.HasFlag(MethodAttributes.Final) ? "sealed override" : "virtual");
        }

        return string.Join(" ", parts.Where(part => !string.IsNullOrEmpty(part)));
    }

    private static string FormatMethodVisibility(MethodAttributes attributes) => (attributes & MethodAttributes.MemberAccessMask) switch
    {
        MethodAttributes.Public => "public",
        MethodAttributes.Private => "private",
        MethodAttributes.Family => "protected",
        MethodAttributes.Assembly => "internal",
        MethodAttributes.FamORAssem => "protected internal",
        MethodAttributes.FamANDAssem => "private protected",
        _ => "private"
    };

    public static string GetTypeName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        TypeDefinition type = reader.GetTypeDefinition(handle);
        string name = reader.GetString(type.Name);

        if (type.GetDeclaringType().IsNil)
        {
            string ns = reader.GetString(type.Namespace);
            return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
        }

        return $"{GetTypeName(reader, type.GetDeclaringType())}+{name}";
    }
}

internal sealed class SignatureTypeNameProvider : ISignatureTypeProvider<string, object?>
{
    public SignatureTypeNameProvider(MetadataReader reader)
    {
    }

    public string GetArrayType(string elementType, ArrayShape shape) =>
        shape.Rank == 1 ? $"{elementType}[]" : $"{elementType}[{new string(',', shape.Rank - 1)}]";

    public string GetByReferenceType(string elementType) => $"ref {elementType}";

    public string GetFunctionPointerType(MethodSignature<string> signature)
    {
        string parameters = string.Join(", ", signature.ParameterTypes);
        return $"delegate*<{parameters}, {signature.ReturnType}>";
    }

    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) =>
        $"{genericType}<{string.Join(", ", typeArguments)}>";

    public string GetGenericMethodParameter(object? genericContext, int index) => $"!!{index}";

    public string GetGenericTypeParameter(object? genericContext, int index) => $"!{index}";

    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) =>
        isRequired ? $"modreq({modifier}) {unmodifiedType}" : $"modopt({modifier}) {unmodifiedType}";

    public string GetPinnedType(string elementType) => $"pinned {elementType}";

    public string GetPointerType(string elementType) => $"{elementType}*";

    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch
    {
        PrimitiveTypeCode.Boolean => "bool",
        PrimitiveTypeCode.Byte => "byte",
        PrimitiveTypeCode.Char => "char",
        PrimitiveTypeCode.Double => "double",
        PrimitiveTypeCode.Int16 => "short",
        PrimitiveTypeCode.Int32 => "int",
        PrimitiveTypeCode.Int64 => "long",
        PrimitiveTypeCode.IntPtr => "nint",
        PrimitiveTypeCode.Object => "object",
        PrimitiveTypeCode.SByte => "sbyte",
        PrimitiveTypeCode.Single => "float",
        PrimitiveTypeCode.String => "string",
        PrimitiveTypeCode.TypedReference => "typedref",
        PrimitiveTypeCode.UInt16 => "ushort",
        PrimitiveTypeCode.UInt32 => "uint",
        PrimitiveTypeCode.UInt64 => "ulong",
        PrimitiveTypeCode.UIntPtr => "nuint",
        PrimitiveTypeCode.Void => "void",
        _ => typeCode.ToString()
    };

    public string GetSZArrayType(string elementType) => $"{elementType}[]";

    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) =>
        AssemblyShapeReader.GetTypeName(reader, handle);

    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        TypeReference type = reader.GetTypeReference(handle);
        string name = reader.GetString(type.Name);
        string ns = reader.GetString(type.Namespace);
        return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
    }

    public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) =>
        reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
}
