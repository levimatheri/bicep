// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Bicep.Core;
using Bicep.Core.Parsing;
using Bicep.Core.Syntax;
using Bicep.Core.TypeSystem;
using Bicep.Core.TypeSystem.Types;

namespace Bicep.LanguageServer.Refactor;

//asdfg discriminated object type
//asdfg TypeKind.Never
//asdfg experimental resource types

public static class TypeStringifier
{
    private const string UnknownTypeName = "object? /* unknown */";
    private const string AnyTypeName = "object? /* any */";
    private const string RecursiveTypeName = "object /* recursive */";
    private const string ErrorTypeName = "object /* error */";
    //asdfg private const string NeverTypeName = "object /* never */";

    public enum Strictness
    {
        /// <summary>
        /// Create syntax representing the exact type, e.g. `{ p1: 123, p2: 'abc' | 'def' }`
        /// </summary>
        Strict,

        /// <summary>
        /// Widen literal types when not part of a union, e.g. => `{ p1: int, p2: 'abc' | 'def' }`, empty arrays/objects, tuples, etc, hopefully more in line with user needs
        /// </summary>
        Medium,

        /// <summary>
        /// Widen everything to basic types only, e.g. => `object`
        /// </summary>
        Loose,
    }

    // Note: This is "best effort" code for now. Ideally we should handle this exactly, but Bicep doesn't support expressing all the types it actually supports
    // Note: Returns type as a single line
    //asdfg consider better solution than ignoreTopLevelNullability, like removing the nullability before passing it in
    public static string Stringify(TypeSymbol? type, TypeProperty? typeProperty, Strictness strictness, bool removeTopLevelNullability = false)
    {
        return StringifyHelper(type, typeProperty, strictness, [], removeTopLevelNullability);
    }

    private static string StringifyHelper(TypeSymbol? type, TypeProperty? typeProperty, Strictness strictness, TypeSymbol[] visitedTypes, bool removeTopLevelNullability = false)
    {
        //asdfg why are we never calling StringifyHelper with a non-null typeProperty during recursion?.  Should we?

        // asdfg also check stack depth
        if (type == null)
        {
            return UnknownTypeName; //asdfg test
        }

        if (visitedTypes.Contains(type))
        {
            return RecursiveTypeName;
        }

        TypeSymbol[] previousVisitedTypes = visitedTypes;
        visitedTypes = [..previousVisitedTypes, type];

        type = Widen(type, strictness);

        // If from an object property that is implicitly allowed to be null (like for many resource properties)
        if (!removeTopLevelNullability && typeProperty?.Flags.HasFlag(TypePropertyFlags.AllowImplicitNull) == true)
        {
            // Won't recursive forever because now typeProperty = null
            // Note though that because this is by nature recursive with the same type, we must pass in previousVisitedTypes
            return StringifyHelper(TypeHelper.MakeNullable(type), null, strictness, previousVisitedTypes);
        }

        // Displayable nullable types (always represented as a union type containing "null" as a member")
        //   as "type?" rather than "type|null"
        if (TypeHelper.TryRemoveNullability(type) is TypeSymbol nonNullableType)
        {
            if (removeTopLevelNullability)
            {
                return StringifyHelper(nonNullableType, null, strictness, visitedTypes);//asdfg testpoint
            }
            else
            {
                return Nullableify(nonNullableType, strictness, visitedTypes);//asdfg testpoint
            }
        }

        switch (type)
        {
            // Literal types - keep as is if strict asdfg??
            case StringLiteralType
               or IntegerLiteralType
               or BooleanLiteralType
               when strictness == Strictness.Strict:
                return type.Name;
            // ... otherwise widen to simple type
            case StringLiteralType:
                return LanguageConstants.String.Name;
            case IntegerLiteralType:
                return LanguageConstants.Int.Name;
            case BooleanLiteralType:
                return LanguageConstants.Bool.Name;

            // Tuple types
            case TupleType tupleType:
                if (strictness == Strictness.Loose)
                {
                    return LanguageConstants.Array.Name;
                }
                else if (strictness == Strictness.Medium)
                {
                    var widenedTypes = tupleType.Items.Select(t => Widen(t.Type, strictness)).ToArray();
                    var firstItemType = widenedTypes.FirstOrDefault()?.Type;
                    if (firstItemType == null)
                    {
                        // Empty tuple - use "array" to allow items
                        return LanguageConstants.Array.Name;
                    }
                    else if (widenedTypes.All(t => t.Type.Name == firstItemType.Name))
                    {
                        // Bicep infers a tuple type from literals such as "[1, 2]", turn these
                        // into the more likely intended int[] if all the members are of the same type
                        return Arrayify(widenedTypes[0], strictness, visitedTypes);
                    }
                }

                return $"[{string.Join(", ", tupleType.Items.Select(tt => StringifyHelper(tt.Type, null, strictness, visitedTypes)))}]";

            // e.g. "int[]"
            case TypedArrayType when strictness == Strictness.Loose:
                return LanguageConstants.Array.Name;
            case TypedArrayType typedArrayType:
                return Arrayify(typedArrayType.Item.Type, strictness, visitedTypes);

            // plain old "array"
            case ArrayType:
                return LanguageConstants.Array.Name;

            //asdfg // Nullable types are union types with one of the members being the null type  TypeHelper.IsNullable asdfg
            //UnionType unionType when strictness != Strictness.Loose && TryRemoveNullFromTwoMemberUnion(unionType) is TypeSymbol nullableUnionMember =>
            //    $"{GetSyntaxStringForType(null, nullableUnionMember, strictness)}?",

            case UnionType unionType when strictness == Strictness.Loose:
                // Widen to the first non-null member type (which are all supposed to be literal types of the same type) asdfg?
                var itemType = Widen(FirstNonNullUnionMember(unionType) ?? unionType.Members.FirstOrDefault()?.Type ?? LanguageConstants.Null, strictness);
                if (TypeHelper.IsNullable(unionType))
                {
                    itemType = TypeHelper.MakeNullable(itemType); //asdfg???
                }
                return StringifyHelper(itemType, null, Strictness.Loose, visitedTypes);

            case UnionType:
                return type.Name;

            case BooleanType:
                return LanguageConstants.Bool.Name;
            case IntegerType:
                return LanguageConstants.Int.Name;
            case StringType:
                return LanguageConstants.String.Name;
            case NullType:
                return LanguageConstants.Null.Name;

            case ObjectType objectType:
                if (strictness == Strictness.Loose)
                {
                    return LanguageConstants.Object.Name;
                }
                // strict: {} with additional properties allowed should be "object" not "{}"
                // medium: Bicep infers {} with no allowable members from the literal "{}", the user more likely wants to allow members
                else if (objectType.Properties.Count == 0 &&
                    (strictness == Strictness.Medium || !IsObjectLiteral(objectType)))
                {
                    return "object";
                }

                return $"{{ {
                    string.Join(", ", objectType.Properties
                        .Where(p => !p.Value.Flags.HasFlag(TypePropertyFlags.ReadOnly))
                        .Select(p => GetFormattedTypeProperty(p.Value, strictness, visitedTypes)))
                    } }}";

            case AnyType:
                return AnyTypeName;
            case ErrorType:
                return ErrorTypeName; //asdfg test

            // Anything else we don't know about
            //asdfg _ => type.Name, //asdfg?
            default:
                return $"object? /* {type.Name} */"; //asdfg?
        };
    }

    private static string Arrayify(TypeSymbol type, Strictness strictness, TypeSymbol[] visitedTypes)
    {
        string stringifiedType = StringifyHelper(type, null, strictness, visitedTypes);
        bool needsParentheses = NeedsParentheses(type, strictness);

        return needsParentheses ? $"({stringifiedType})[]" : $"{stringifiedType}[]";
    }

    private static string Nullableify(TypeSymbol type, Strictness strictness, TypeSymbol[] visitedTypes)
    {
        string stringifiedType = StringifyHelper(type, null, strictness, visitedTypes);
        bool needsParentheses = NeedsParentheses(type, strictness);

        return needsParentheses ? $"({stringifiedType})?" : $"{stringifiedType}?";
    }

    private static bool NeedsParentheses(TypeSymbol type, Strictness strictness)
    {
        // If the type is '1|2', with loose/medium, we need to check whether 'int' needs parentheses, not '1|2'
        // Therefore, widen first
        bool needsParentheses = Widen(type, strictness) switch
        {
            UnionType { Members.Length: > 1 } => true, // also works for nullable types
            _ => false
        };
        return needsParentheses;
    }

    private static TypeSymbol Widen(TypeSymbol type, Strictness strictness)
    {
        if (strictness == Strictness.Strict)
        {
            return type;
        }

        if (type is UnionType unionType && strictness == Strictness.Loose)
        {
            // Widen non-null members to a single type (which are all supposed to be literal types of the same type) asdfg
            var widenedType = Widen(
                FirstNonNullUnionMember(unionType) ?? unionType.Members.FirstOrDefault()?.Type ?? LanguageConstants.Null,
                strictness);
            if (TypeHelper.IsNullable(unionType))
            {
                // If it had "|null" before, add it back
                widenedType = TypeHelper.MakeNullable(widenedType); //asdfg??? testpoint
            }
            return widenedType;
        }

        // ... otherwise widen to simple types
        return type switch
        {
            StringLiteralType => LanguageConstants.String,
            IntegerLiteralType => LanguageConstants.Int,
            BooleanLiteralType => LanguageConstants.Bool,
            _ => type,
        };
    }

    private static TypeSymbol? FirstNonNullUnionMember(UnionType unionType) =>
        unionType.Members.FirstOrDefault(m => m.Type is not NullType)?.Type;

    // True if "{}" (which allows no additional properties) instead of "object"
    private static bool IsObjectLiteral(ObjectType objectType)
    {
        return objectType.Properties.Count == 0 && !objectType.HasExplicitAdditionalPropertiesType;
    }

    // asdfg??
    //type.AdditionalPropertiesFlags
    // type.AdditionalPropertiesType
    // type.UnwrapArrayType

    //private static bool IsTypeStringNullable(string typeString) //asdfg
    //{
    //    var commentsRemoved = new Regex("/\\*[^*/]*\\*/\\s*$").Replace(typeString, "");
    //    return commentsRemoved.TrimEnd().EndsWith('?');
    //}

    private static string GetFormattedTypeProperty(TypeProperty property, Strictness strictness, TypeSymbol[] visitedTypes)
    {
        return
            $"{StringUtils.EscapeBicepPropertyName(property.Name)}: {StringifyHelper(property.TypeReference.Type, property, strictness, visitedTypes)}";
    }
}
