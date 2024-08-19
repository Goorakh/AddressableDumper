using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace AddressableDumper.Utils
{
    public static class ReflectionUtils
    {
        public static List<Type> GetHierarchyTypes(Type type)
        {
            List<Type> baseTypes = [];

            do
            {
                baseTypes.Add(type);
                type = type.BaseType;
            } while (type != null);

            return baseTypes;
        }

        public static IEnumerable<MemberInfo> GetSerializableMembers(Type declaringType, MemberTypes memberTypes, BindingFlags bindingFlags)
        {
            if (declaringType is null)
                throw new ArgumentNullException(nameof(declaringType));

            return declaringType.GetMembers(bindingFlags).Where(member =>
            {
                if ((member.MemberType & memberTypes) == 0)
                    return false;

                if (member is FieldInfo field)
                {
                    if (field.GetCustomAttribute(typeof(NonSerializedAttribute)) != null)
                        return false;

                    if (!field.IsPublic && field.GetCustomAttribute(typeof(SerializeField)) == null)
                        return false;
                }
                else if (member is PropertyInfo property)
                {
                    if (property.GetIndexParameters().Length > 0)
                        return false;

                    MethodInfo getMethod = property.GetMethod;
                    if (getMethod == null || !getMethod.IsPublic)
                        return false;
                }

                return true;
            });
        }
    }
}
