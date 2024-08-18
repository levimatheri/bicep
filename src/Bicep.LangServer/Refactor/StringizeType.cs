// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Bicep.Core;
using Bicep.Core.Parsing;
using Bicep.Core.Syntax;
using Bicep.Core.TypeSystem;
using Bicep.Core.TypeSystem.Types;

namespace Bicep.LanguageServer.Refactor
{
    //asdfg discriminated object type
    //asdfg TypeKind.Never
    //asdfg experimental resource types

    public static class StringizeType
    {
        private const string UnknownTypeName = "object? /* unknown */"; //asdfg
        private const string AnyTypeName = "object? /* any */"; //asdfg
        private const string RecursiveTypeName = "object /* recursive */";
        private const string ErrorTypeName = "object /* error */";
        //asdfg private const string NeverTypeName = "object /* never */";

        public enum Strictness
        {
            Strict, // Create syntax representing the exact type, e.g. `{ p1: 123, p2: 'abc' | 'def' }`
            Medium, // Widen literal types, e.g. => `{ p1: int, p2: 'abc' | 'def' }`, empty arrays/objects, tuples, etc, hopefully more in line with user needs
            Loose,  // Widen everything to basic types only, e.g. => `object`
        }

        //asdfg should this be creating syntax nodes?  Probably...
        //asdfg recursive types
        // Note: This is "best effort" code for now. Ideally we should handle this exactly, but Bicep doesn't support expressing all the types it actually supports
        // Note: Returns type as a single line
        public static string Stringize(TypeSymbol? type, TypeProperty? typeProperty, Strictness strictness)
        {
            return StringizeHelper(type, typeProperty, strictness, []);
        }

        private static string StringizeHelper(TypeSymbol? type, TypeProperty? typeProperty, Strictness strictness, TypeSymbol[] visitedTypes)
        {
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

            // If from an object property that is implicitly allowed to be null (like for many resource properties)
            if (typeProperty?.Flags.HasFlag(TypePropertyFlags.AllowImplicitNull) == true)
            {
                // Won't recursive forever because now typeProperty = null
                // Note though that because this is by nature recursive with the same type, we must pass in previousVisitedTypes
                return StringizeHelper(TypeHelper.MakeNullable(type), null, strictness, previousVisitedTypes);
            }

            // Convert "( unionMember1 | null )" => "unionMember1?"
            if (type is UnionType nullableUnionType && TypeHelper.TryRemoveNullability(type) is TypeSymbol nonNullableType)
            {
                // Type is nullable (i.e., a union which includes a null member).  If there is only a single
                //   member in the original type besides the null, then display as "member?" instead of "member | null".
                // All other cases, display as the original union (i.e., we want "false|true|null" instead of "(false|true)?"
                if (nullableUnionType.Members.Length == 2)
                {
                    return $"{StringizeHelper(nonNullableType, null, strictness, visitedTypes)}?"; //asdfg testpoint
                }
            }

            switch (type)
            {
                // Literal types - keep as is if strict
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
                        var firstItemType = tupleType.Items.FirstOrDefault()?.Type;
                        if (firstItemType == null)
                        {
                            // Empty tuple - use "array" to allow items
                            return LanguageConstants.Array.Name;
                        }
                        else if (tupleType.Items.All(t => t.Type.Name == firstItemType.Name))
                        {
                            // Bicep infers a tuple type from literals such as "[1, 2]", turn these
                            // into the more likely intended int[] if all the members are of the same type
                            return Arrayize(tupleType.Item.Type, strictness, visitedTypes);
                        }
                    }

                    return $"[{string.Join(", ", tupleType.Items.Select(tt => StringizeHelper(tt.Type, null, strictness, visitedTypes)))}]";

                // e.g. "int[]"
                case TypedArrayType when strictness == Strictness.Loose:
                    return LanguageConstants.Array.Name;
                case TypedArrayType typedArrayType:
                    return Arrayize(typedArrayType.Item.Type, strictness, visitedTypes);

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
                    return StringizeHelper(itemType, null, Strictness.Loose, visitedTypes);

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

                    return $"{{ {string.Join(", ", objectType.Properties.Select(p => GetFormattedTypeProperty(p.Value, strictness, visitedTypes)))} }}";

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

        private static string Arrayize(TypeSymbol type, Strictness strictness, TypeSymbol[] visitedTypes)
        {
            string stringizedType = StringizeHelper(type, null, strictness, visitedTypes);
            bool needsParentheses = type switch
            {
                UnionType unionType => true, // also works for nullable types
                _ => false
            };

            return needsParentheses ? $"({stringizedType})[]" : $"{stringizedType}[]";
        }

        private static TypeSymbol Widen(TypeSymbol type, Strictness strictness)
        {
            if (strictness == Strictness.Strict)
            {
                return type;
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
                $"{StringUtils.EscapeBicepPropertyName(property.Name)}: {StringizeHelper(property.TypeReference.Type, property, strictness, visitedTypes)}";
        }
    }
}
