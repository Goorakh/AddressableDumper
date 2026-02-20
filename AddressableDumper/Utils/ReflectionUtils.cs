using AddressableDumper.Utils.Extensions;
using HG;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace AddressableDumper.Utils
{
    public static class ReflectionUtils
    {
        static readonly Dictionary<Type, MemberInfo[]> _cachedSerializableMembers = [];
        static readonly Dictionary<Type, FieldInfo[]> _cachedSerializableFields = [];
        static readonly Dictionary<Type, PropertyInfo[]> _cachedSerializableProperties = [];

        public static void GetHierarchyTypes(Type type, List<Type> hierarchyTypes)
        {
            do
            {
                hierarchyTypes.Add(type);
                type = type.BaseType;
            } while (type != null);
        }

        public static MemberInfo[] GetSerializableMembers(Type declaringType, MemberTypes memberTypes)
        {
            return memberTypes switch
            {
                MemberTypes.Field => GetSerializableFields(declaringType),
                MemberTypes.Property => GetSerializableProperties(declaringType),
                MemberTypes.Field | MemberTypes.Property => GetSerializableMembers(declaringType),
                _ => throw new NotImplementedException($"Member types {memberTypes} is not implemented"),
            };
        }

        public static MemberInfo[] GetSerializableMembers(Type declaringType)
        {
            if (declaringType is null)
                throw new ArgumentNullException(nameof(declaringType));

            if (!_cachedSerializableMembers.TryGetValue(declaringType, out MemberInfo[] serializableMembers))
            {
                MemberInfo[] allMembers = declaringType.GetMembers(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (allMembers.Length > 0)
                {
                    using var _ = ListPool<MemberInfo>.RentCollection(out List<MemberInfo> serializableMembersList);
                    serializableMembersList.EnsureCapacity(allMembers.Length);

                    foreach (MemberInfo member in allMembers)
                    {
                        if (member is FieldInfo field)
                        {
                            if (field.GetCustomAttribute(typeof(NonSerializedAttribute)) != null)
                                continue;

                            if (!field.IsPublic && field.GetCustomAttribute(typeof(SerializeField)) == null)
                                continue;
                        }
                        else if (member is PropertyInfo property)
                        {
                            if (property.GetIndexParameters().Length > 0)
                                continue;

                            MethodInfo getMethod = property.GetMethod;
                            if (getMethod == null || !getMethod.IsPublic)
                                continue;
                        }
                        else
                        {
                            continue;
                        }

                        serializableMembersList.Add(member);
                    }

                    serializableMembers = [.. serializableMembersList];
                }
                else
                {
                    serializableMembers = [];
                }

                _cachedSerializableMembers.Add(declaringType, serializableMembers);
            }

            return serializableMembers;
        }

        public static FieldInfo[] GetSerializableFields(Type declaringType)
        {
            if (declaringType is null)
                throw new ArgumentNullException(nameof(declaringType));

            if (!_cachedSerializableFields.TryGetValue(declaringType, out FieldInfo[] serializableFields))
            {
                MemberInfo[] serializableMembers = GetSerializableMembers(declaringType);
                if (serializableMembers.Length > 0)
                {
                    using var _ = ListPool<FieldInfo>.RentCollection(out List<FieldInfo> serializableFieldsList);
                    serializableFieldsList.EnsureCapacity(serializableMembers.Length);

                    foreach (MemberInfo member in serializableMembers)
                    {
                        if (member is FieldInfo field)
                        {
                            serializableFieldsList.Add(field);
                        }
                    }

                    serializableFields = [.. serializableFieldsList];
                }
                else
                {
                    serializableFields = [];
                }

                _cachedSerializableFields.Add(declaringType, serializableFields);
            }

            return serializableFields;
        }

        public static PropertyInfo[] GetSerializableProperties(Type declaringType)
        {
            if (declaringType is null)
                throw new ArgumentNullException(nameof(declaringType));

            if (!_cachedSerializableProperties.TryGetValue(declaringType, out PropertyInfo[] serializableProperties))
            {
                MemberInfo[] serializableMembers = GetSerializableMembers(declaringType);
                if (serializableMembers.Length > 0)
                {
                    using var _ = ListPool<PropertyInfo>.RentCollection(out List<PropertyInfo> serializablePropertiesList);
                    serializablePropertiesList.EnsureCapacity(serializableMembers.Length);

                    foreach (MemberInfo member in serializableMembers)
                    {
                        if (member is PropertyInfo field)
                        {
                            serializablePropertiesList.Add(field);
                        }
                    }

                    serializableProperties = [.. serializablePropertiesList];
                }
                else
                {
                    serializableProperties = [];
                }

                _cachedSerializableProperties.Add(declaringType, serializableProperties);
            }

            return serializableProperties;
        }
    }
}
