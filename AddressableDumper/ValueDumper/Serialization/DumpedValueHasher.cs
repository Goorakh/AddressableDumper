using AddressableDumper.Utils;
using RoR2.Navigation;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace AddressableDumper.ValueDumper.Serialization
{
    public class DumpedValueHasher : IHashProvider
    {
        static readonly HashAlgorithm _sharedHasher;
        static readonly MemoryStream _sharedMemoryStream;

        static readonly Dictionary<Type, MethodInfo> _writeMethodCache = [];

        static DumpedValueHasher()
        {
            _sharedHasher = MD5.Create();
            _sharedMemoryStream = new MemoryStream();
        }

        public byte[] ComputeHash(object value)
        {
            if (value is null)
                return [];

            _sharedMemoryStream.Position = 0;
            using (BinaryWriter writer = new BinaryWriter(_sharedMemoryStream, Encoding.UTF8, true))
            {
                writeValue(writer, value);
            }

            int writtenBytesCount = (int)_sharedMemoryStream.Position;

            if (writtenBytesCount == 0)
                return [];

            byte[] bytes = _sharedMemoryStream.ToArray();

            return _sharedHasher.ComputeHash(bytes, 0, writtenBytesCount);
        }

        static void writeValue(BinaryWriter writer, object value)
        {
            if (value is null)
            {
                writer.Write(0);
                return;
            }

            Type type = value.GetType();

            switch (value)
            {
                case Vector2 vector2:
                    writer.Write(vector2.x);
                    writer.Write(vector2.y);

                    return;
                case Vector3 vector3:
                    writer.Write(vector3.x);
                    writer.Write(vector3.y);
                    writer.Write(vector3.z);

                    return;
                case Vector4 vector4:
                    writer.Write(vector4.x);
                    writer.Write(vector4.y);
                    writer.Write(vector4.z);
                    writer.Write(vector4.w);

                    return;
                case Quaternion quaternion:
                    writer.Write(quaternion.x);
                    writer.Write(quaternion.y);
                    writer.Write(quaternion.z);
                    writer.Write(quaternion.w);

                    return;
                case Matrix4x4 matrix4x4:
                    for (int i = 0; i < 4; i++)
                    {
                        writeValue(writer, matrix4x4.GetRow(i));
                    }

                    return;
                case Color color:
                    writer.Write(color.r);
                    writer.Write(color.g);
                    writer.Write(color.b);
                    writer.Write(color.a);

                    return;
                case Color32 color:
                    writer.Write(color.r);
                    writer.Write(color.g);
                    writer.Write(color.b);
                    writer.Write(color.a);

                    return;
                case BoneWeight boneWeight:
                    writer.Write(boneWeight.weight0);
                    writer.Write(boneWeight.weight1);
                    writer.Write(boneWeight.weight2);
                    writer.Write(boneWeight.weight3);
                    writer.Write(boneWeight.boneIndex0);
                    writer.Write(boneWeight.boneIndex1);
                    writer.Write(boneWeight.boneIndex2);
                    writer.Write(boneWeight.boneIndex3);
                    return;
                case ClothSkinningCoefficient clothSkinningCoefficient:
                    writer.Write(clothSkinningCoefficient.maxDistance);
                    writer.Write(clothSkinningCoefficient.collisionSphereDistance);

                    return;
            }

            if (value is IEnumerable enumerable)
            {
                foreach (object item in enumerable)
                {
                    writeValue(writer, item);
                }

                return;
            }

            Type writeMethodArgumentType = type;
            if (writeMethodArgumentType.IsEnum)
                writeMethodArgumentType = writeMethodArgumentType.GetEnumUnderlyingType();

            if (!_writeMethodCache.TryGetValue(writeMethodArgumentType, out MethodInfo writeMethod))
            {
                writeMethod = typeof(BinaryWriter).GetMethod("Write", [writeMethodArgumentType]);
                _writeMethodCache.Add(writeMethodArgumentType, writeMethod);
            }

            if (writeMethod != null)
            {
                writeMethod.Invoke(writer, [value]);
                return;
            }

            if (type.IsSerializable)
            {
                foreach (MemberInfo member in ReflectionUtils.GetSerializableMembers(type, MemberTypes.Field, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (member is not FieldInfo field)
                        throw new NotImplementedException($"{member} is not implemented");

                    writeValue(writer, field.GetValue(value));
                }

                return;
            }

            Log.Warning($"Object of type {type.FullName} ({value}) is not implemented");
        }
    }
}