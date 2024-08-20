using System;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace AddressableDumper.ValueDumper.Serialization
{
    public class DumpedValueFormatter : IFormatProvider, ICustomFormatter
    {
        public IFormatProvider BackingFormatProvider { get; set; } = CultureInfo.InvariantCulture;

        public object GetFormat(Type formatType)
        {
            if (formatType == typeof(ICustomFormatter))
                return this;

            return null;
        }

        public string Format(string format, object arg, IFormatProvider formatProvider)
        {
            string formatSubArg(object subArg)
            {
                return Format(format, subArg, formatProvider);
            }

            switch (arg)
            {
                case null:
                    return "null";
                case string str:
                    return str;
                case IFormattable formattable:
                    return formattable.ToString(format, BackingFormatProvider);
                case IConvertible convertible:
                    return convertible.ToString(BackingFormatProvider);
                case Vector2 vector2:
                    return $"vec2f({formatSubArg(vector2.x)}, {formatSubArg(vector2.y)})";
                case Vector2Int vector2Int:
                    return $"vec2i({formatSubArg(vector2Int.x)}, {formatSubArg(vector2Int.y)})";
                case Vector3 vector3:
                    return $"vec3f({formatSubArg(vector3.x)}, {formatSubArg(vector3.y)}, {formatSubArg(vector3.z)})";
                case Vector3Int vector3Int:
                    return $"vec3i({formatSubArg(vector3Int.x)}, {formatSubArg(vector3Int.y)}, {formatSubArg(vector3Int.z)})";
                case Vector4 vector4:
                    return $"vec4f({formatSubArg(vector4.x)}, {formatSubArg(vector4.y)}, {formatSubArg(vector4.z)}, {formatSubArg(vector4.w)})";
                case Color color:
                    return $"RGBA({formatSubArg(color.r)}, {formatSubArg(color.g)}, {formatSubArg(color.b)}, {formatSubArg(color.a)})";
                case Color32 color32:
                    return $"RGBA32({formatSubArg(color32.r)}, {formatSubArg(color32.g)}, {formatSubArg(color32.b)}, {formatSubArg(color32.a)})";
                case Rect rect:
                    return $"rect(pos={formatSubArg(rect.position)}, size={formatSubArg(rect.size)})";
                case RectInt rectInt:
                    return $"rect_int(pos={formatSubArg(rectInt.position)}, size={formatSubArg(rectInt.size)})";
                case Bounds bounds:
                    return $"bounds(center={formatSubArg(bounds.center)}, size={formatSubArg(bounds.size)})";
                case BoundsInt boundsInt:
                    return $"bounds_int(center={formatSubArg(boundsInt.center)}, size={formatSubArg(boundsInt.size)})";
                case Quaternion quaternion:
                    Vector3 euler = quaternion.eulerAngles;
                    return $"euler({formatSubArg(euler.x)}, {formatSubArg(euler.y)}, {formatSubArg(euler.z)})";
                case Type type:
                    return $"type({type.AssemblyQualifiedName})";
                case LayerMask layerMask:
                    StringBuilder layerNameBuilder = HG.StringBuilderPool.RentStringBuilder();

                    int mask = layerMask.value;
                    for (int layer = 0; layer < sizeof(int) * 8; layer++)
                    {
                        if ((mask & (1 << layer)) != 0)
                        {
                            if (layerNameBuilder.Length > 0)
                            {
                                layerNameBuilder.Append(" | ");
                            }

                            layerNameBuilder.Append(LayerMask.LayerToName(layer));
                        }
                    }

                    string layerNames = layerNameBuilder.ToString();
                    layerNameBuilder = HG.StringBuilderPool.ReturnStringBuilder(layerNameBuilder);

                    return $"[{layerNames}] ({mask})";
                case PropertyName propertyName:
                    return propertyName.ToString();
                case NetworkHash128 networkHash128:
                    return $"NetworkHash128({networkHash128})";
                case NetworkSceneId networkSceneId:
                    return $"NetworkSceneId({formatSubArg(networkSceneId.Value)})";
                default:
                    throw new NotImplementedException($"Unhandled argument type '{arg.GetType().Name}'");
            }
        }
    }
}
