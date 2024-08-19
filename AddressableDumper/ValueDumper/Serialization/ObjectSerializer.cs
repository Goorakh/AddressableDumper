using AddressableDumper.Utils;
using AddressableDumper.Utils.Extensions;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AddressableDumper.ValueDumper.Serialization
{
    public class ObjectSerializer
    {
        static readonly IFormatProvider _formatProvider = new DumpedValueFormatter();

        readonly JsonWriter _writer;
        readonly object _rootValue;

        readonly Stack<object> _serializingObjectStack = [];

        readonly Stack<Transform> _serializingObjectRoots = [];

        bool isSerializingRootValue => _serializingObjectStack.Count <= 1;

        public ObjectSerializer(JsonWriter writer, object value)
        {
            _writer = writer;
            _rootValue = value;
        }

        public void Write()
        {
            writeValue(_rootValue);
        }

        void writeValue(object value)
        {
            WriteOperationBuilder builder = new WriteOperationBuilder(new JsonWriteOperationBuilderWriter(_writer));

            try
            {
                if (!buildWriteOperation(value, builder))
                {
                    Log.Warning($"Failed to determine write operation for value {value} ({value?.GetType()?.Name})");
                    builder.AddNull($"Failed to serialize value of type '{value?.GetType()?.Name ?? "null"}'");
                }
            }
            catch (Exception)
            {
                StringBuilder operationsLogBuilder = HG.StringBuilderPool.RentStringBuilder();
                foreach (WriteOperation operation in builder)
                {
                    operationsLogBuilder.AppendLine(operation.ToString());
                }

                Log.Info($"Builder state:\n{operationsLogBuilder}");
                HG.StringBuilderPool.ReturnStringBuilder(operationsLogBuilder);

                throw;
            }
            finally
            {
                builder.Flush();
            }
        }
        
        bool buildWriteOperation(object value, WriteOperationBuilder builder)
        {
            // TODO: This does not detect recursive references in value types
            if (_serializingObjectStack.Contains(value, EqualityComparer<object>.Default))
            {
                builder.AddNull($"Recursive reference ({value})");
                return true;
            }

            _serializingObjectStack.Push(value);

            bool addedRoot = false;
            if (value is UnityEngine.Object unityObj)
            {
                GameObject gameObject = unityObj.GetGameObject();
                if (gameObject)
                {
                    Transform root = gameObject.transform.root;
                    if (!_serializingObjectRoots.Contains(root))
                    {
                        _serializingObjectRoots.Push(root);
                        addedRoot = true;
                    }
                }
            }

            bool anythingWritten = buildWrite(value, builder);

            if (_serializingObjectStack.Count == 0 || !ReferenceEquals(_serializingObjectStack.Peek(), value))
            {
                throw new Exception($"Invalid state, expected {value} at top of stack, found {(_serializingObjectStack.Count == 0 ? "nothing" : _serializingObjectStack.Peek())}");
            }

            if (addedRoot)
            {
                _serializingObjectRoots.Pop();
            }

            _serializingObjectStack.Pop();
            return anythingWritten;

            bool buildWrite(object value, WriteOperationBuilder builder)
            {
                if (value is null)
                {
                    builder.AddNull();
                    return true;
                }

                if (value is string str)
                {
                    builder.AddValue(str);
                    return true;
                }

                if (buildCustomFormattedWriteOperation(value, builder))
                    return true;
                
                if (buildCollectionWriteOperation(value, builder))
                    return true;

                if (buildUnityObjectWriteOperation(value, builder))
                    return true;

                if (getSerializableTypeWriteOperation(value, builder))
                    return true;

                return false;
            }
        }

        bool buildCollectionWriteOperation(object value, WriteOperationBuilder builder, int maxCount = 50)
        {
            if (value is null)
            {
                builder.AddNull();
                return true;
            }

            if (value is byte[] bytes && bytes.Length > 5)
            {
                builder.AddValueRaw($"b64('{Convert.ToBase64String(bytes)}')", "Converted from byte array");
                return true;
            }

            Type type = value.GetType();
            if (type.IsArray || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)))
            {
                builder.AddStartArray();

                ICollection collection = (ICollection)value;

                int writtenItems = 0;
                foreach (object element in collection)
                {
                    if (writtenItems >= maxCount)
                    {
                        builder.AddComment($"... Remaining {collection.Count - writtenItems} value(s) excluded ...");
                        break;
                    }

                    if (buildWriteOperation(element, builder))
                    {
                        writtenItems++;
                    }
                    else
                    {
                        Log.Warning($"Failed to determine write operation for collection element {element} ({element?.GetType()?.Name})");
                    }
                }

                builder.AddEndArray();

                return true;
            }

            return false;
        }

        bool getSerializableTypeWriteOperation(object value, WriteOperationBuilder builder)
        {
            if (value is null)
            {
                builder.AddNull();
                return true;
            }

            Type type = value.GetType();
            if (!type.IsSerializable)
                return false;

            WriteOperationBuilder serializedObjectBuilder = new WriteOperationBuilder(builder)
            {
                AutoFlushCapacity = builder.AutoFlushCapacity,
                AutoFlush = false,
            };

            serializedObjectBuilder.AddStartObject();

            buildTypeFieldWriteOperation(type, serializedObjectBuilder);

            List<Type> hierarchyTypes = ReflectionUtils.GetHierarchyTypes(type);
            hierarchyTypes.Reverse();

            bool anyFieldWritten = false;
            bool anyFieldWriteAttempted = false;

            foreach (Type baseType in hierarchyTypes)
            {
                const BindingFlags FLAGS = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;

                foreach (MemberInfo member in ReflectionUtils.GetSerializableMembers(baseType, MemberTypes.Field, FLAGS))
                {
                    if (member is not FieldInfo field)
                        throw new NotImplementedException($"Member type {member.MemberType} ({member}) is not implemented");

                    anyFieldWriteAttempted = true;

                    object fieldValue;
                    try
                    {
                        fieldValue = field.GetValue(value);
                    }
                    catch (Exception e)
                    {
                        Log.Error_NoCallerPrefix($"Failed to get field value of {field.DeclaringType.FullName}.{field.Name}: {e}");

                        serializedObjectBuilder.AddPropertyName(field.Name);
                        serializedObjectBuilder.AddNull($"Error retrieving member value: {e.Message}");

                        continue;
                    }

                    if (tryBuildPropertyWithValueWriteOperation(field.Name, fieldValue, serializedObjectBuilder))
                    {
                        anyFieldWritten = true;

                        serializedObjectBuilder.AutoFlush = true;
                        serializedObjectBuilder.Flush();
                    }
                    else
                    {
                        Log.Warning($"Failed to determine write operation for serialized field {field.DeclaringType.FullName}.{field.Name}: {fieldValue} ({field.FieldType.Name})");
                    }
                }
            }

            serializedObjectBuilder.AddEndObject();

            if (anyFieldWritten || !anyFieldWriteAttempted)
            {
                serializedObjectBuilder.Flush();
                return true;
            }
            else
            {
                return false;
            }
        }

        bool tryBuildPropertyWithValueWriteOperation(string propertyName, object value, WriteOperationBuilder builder)
        {
            WriteOperationBuilder propertyValueBuilder = new WriteOperationBuilder(builder)
            {
                AutoFlush = false,
                AutoFlushCapacity = builder.AutoFlushCapacity,
            };

            propertyValueBuilder.AddPropertyName(propertyName);

            propertyValueBuilder.AutoFlush = true;

            if (buildWriteOperation(value, propertyValueBuilder))
            {
                propertyValueBuilder.Flush();
                return true;
            }
            else
            {
                return false;
            }
        }

        void buildPropertyWithValueWriteOperation(string propertyName, object value, WriteOperationBuilder builder)
        {
            builder.AddPropertyName(propertyName);

            if (!buildWriteOperation(value, builder))
            {
                throw new ArgumentException($"Value {value} ({value?.GetType().FullName}) was not serialized for property '{propertyName}'");
            }
        }

        void buildTypeFieldWriteOperation(Type type, WriteOperationBuilder builder)
        {
            buildPropertyWithValueWriteOperation("$type", type.FullName, builder);
        }

        bool buildCustomFormattedWriteOperation(object value, WriteOperationBuilder builder, string formatString = "")
        {
            if (string.IsNullOrWhiteSpace(formatString))
            {
                formatString = "{0}";
            }
            else
            {
                formatString = "{0:" + formatString + "}";
            }

            string formatted = null;
            try
            {
                formatted = string.Format(_formatProvider, formatString, value);
            }
            catch (Exception e) when (e is not FormatException)
            {
            }

            if (formatted == null)
            {
                if (value != null)
                {
                    Type type = value.GetType();
                    if (type.IsPrimitive)
                    {
                        formatted = value.ToString();
                    }
                }
            }

            if (formatted != null)
            {
                builder.AddValueRaw(formatted);
                return true;
            }

            switch (value)
            {
                case Matrix4x4 matrix4x4:
                {
                    builder.AddStartObject();

                    for (int i = 0; i < 4; i++)
                    {
                        buildPropertyWithValueWriteOperation($"row{i}", matrix4x4.GetRow(i), builder);
                    }

                    builder.AddEndObject();

                    return true;
                }
                case ParticleSystem.MinMaxCurve minMaxCurve:
                {
                    builder.AddStartObject();

                    buildTypeFieldWriteOperation(value.GetType(), builder);

                    buildPropertyWithValueWriteOperation("mode", minMaxCurve.mode, builder);

                    switch (minMaxCurve.mode)
                    {
                        case ParticleSystemCurveMode.Constant:
                            buildPropertyWithValueWriteOperation("constant", minMaxCurve.constantMax, builder);
                            break;
                        case ParticleSystemCurveMode.Curve:
                            buildPropertyWithValueWriteOperation("curveMultiplier", minMaxCurve.curveMultiplier, builder);
                            buildPropertyWithValueWriteOperation("curve", minMaxCurve.curveMax, builder);
                            break;
                        case ParticleSystemCurveMode.TwoCurves:
                            buildPropertyWithValueWriteOperation("curveMultiplier", minMaxCurve.curveMultiplier, builder);
                            buildPropertyWithValueWriteOperation("curveMin", minMaxCurve.curveMin, builder);
                            buildPropertyWithValueWriteOperation("curveMax", minMaxCurve.curveMax, builder);
                            break;
                        case ParticleSystemCurveMode.TwoConstants:
                            buildPropertyWithValueWriteOperation("constantMin", minMaxCurve.constantMin, builder);
                            buildPropertyWithValueWriteOperation("constantMax", minMaxCurve.constantMax, builder);
                            break;
                        default:
                            break;
                    }

                    builder.AddEndObject();

                    return true;
                }
                case ParticleSystem.MinMaxGradient minMaxGradient:
                {
                    builder.AddStartObject();

                    buildTypeFieldWriteOperation(value.GetType(), builder);

                    buildPropertyWithValueWriteOperation("mode", minMaxGradient.mode, builder);

                    switch (minMaxGradient.mode)
                    {
                        case ParticleSystemGradientMode.Color:
                            buildPropertyWithValueWriteOperation("color", minMaxGradient.colorMax, builder);
                            break;
                        case ParticleSystemGradientMode.Gradient:
                            buildPropertyWithValueWriteOperation("gradient", minMaxGradient.gradientMax, builder);
                            break;
                        case ParticleSystemGradientMode.TwoColors:
                            buildPropertyWithValueWriteOperation("colorMin", minMaxGradient.colorMin, builder);
                            buildPropertyWithValueWriteOperation("colorMax", minMaxGradient.colorMax, builder);
                            break;
                        case ParticleSystemGradientMode.TwoGradients:
                            buildPropertyWithValueWriteOperation("gradientMin", minMaxGradient.gradientMin, builder);
                            buildPropertyWithValueWriteOperation("gradientMax", minMaxGradient.gradientMax, builder);
                            break;
                        case ParticleSystemGradientMode.RandomColor:
                            buildPropertyWithValueWriteOperation("colorCurve", minMaxGradient.gradientMax, builder);
                            break;
                        default:
                            throw new NotImplementedException($"{minMaxGradient.mode} is not implemented");
                    }

                    builder.AddEndObject();

                    return true;
                }
                case AnimationEvent animationEvent:
                {
                    builder.AddStartObject();

                    buildTypeFieldWriteOperation(value.GetType(), builder);

                    buildPropertyWithValueWriteOperation("time", animationEvent.time, builder);

                    buildPropertyWithValueWriteOperation("functionName", animationEvent.functionName, builder);

                    buildPropertyWithValueWriteOperation("stringParameter", animationEvent.stringParameter, builder);

                    buildPropertyWithValueWriteOperation("floatParameter", animationEvent.floatParameter, builder);

                    buildPropertyWithValueWriteOperation("intParameter", animationEvent.intParameter, builder);

                    buildPropertyWithValueWriteOperation("objectReferenceParameter", animationEvent.objectReferenceParameter, builder);

                    buildPropertyWithValueWriteOperation("isFiredByLegacy", animationEvent.isFiredByLegacy, builder);

                    if (animationEvent.isFiredByLegacy)
                    {
                        buildPropertyWithValueWriteOperation("animationState", animationEvent.animationState, builder);
                    }

                    buildPropertyWithValueWriteOperation("isFiredByAnimator", animationEvent.isFiredByAnimator, builder);

                    if (animationEvent.isFiredByAnimator)
                    {
                        buildPropertyWithValueWriteOperation("animatorStateInfo", animationEvent.animatorStateInfo, builder);

                        buildPropertyWithValueWriteOperation("animatorClipInfo", animationEvent.animatorClipInfo, builder);
                    }

                    builder.AddEndObject();

                    return true;
                }
                case GradientColorKey:
                case GradientAlphaKey:
                case SkeletonBone:
                case LightBakingOutput:
                case JointSpring:
                case TreeInstance:
                case ClothSkinningCoefficient:
                {
                    builder.AddStartObject();

                    buildTypeFieldWriteOperation(value.GetType(), builder);

                    buildSerializedMemberWriteOperations(value, builder, t =>
                    {
                        return new MemberSerializationContext(MemberTypes.Field,
                                                              false,
                                                              false,
                                                              false);
                    });

                    builder.AddEndObject();
                    return true;
                }
                case Scene:
                case AnimationCurve:
                case Keyframe:
                case ParticleSystem.MainModule:
                case ParticleSystem.EmissionModule:
                case ParticleSystem.Burst:
                case ParticleSystem.ShapeModule:
                case ParticleSystem.VelocityOverLifetimeModule:
                case ParticleSystem.LimitVelocityOverLifetimeModule:
                case ParticleSystem.InheritVelocityModule:
                case ParticleSystem.ForceOverLifetimeModule:
                case ParticleSystem.ColorOverLifetimeModule:
                case ParticleSystem.ColorBySpeedModule:
                case ParticleSystem.SizeOverLifetimeModule:
                case ParticleSystem.SizeBySpeedModule:
                case ParticleSystem.RotationOverLifetimeModule:
                case ParticleSystem.RotationBySpeedModule:
                case ParticleSystem.ExternalForcesModule:
                case ParticleSystem.NoiseModule:
                case ParticleSystem.CollisionModule:
                case ParticleSystem.TriggerModule:
                case ParticleSystem.SubEmittersModule:
                case ParticleSystem.TextureSheetAnimationModule:
                case ParticleSystem.LightsModule:
                case ParticleSystem.TrailModule:
                case ParticleSystem.CustomDataModule:
                case Gradient:
                case HumanLimit:
                case SoftJointLimitSpring:
                case SoftJointLimit:
                case JointDrive:
                case RenderTextureDescriptor:
                case WheelFrictionCurve:
                case AnimationState:
                case AnimatorStateInfo:
                case AnimatorClipInfo:
                case TreePrototype:
                case DetailPrototype:
                {
                    builder.AddStartObject();

                    buildTypeFieldWriteOperation(value.GetType(), builder);

                    buildSerializedMemberWriteOperations(value, builder, t =>
                    {
                        return new MemberSerializationContext(MemberTypes.Property,
                                                              false,
                                                              false,
                                                              false);
                    });

                    switch (value)
                    {
                        case ParticleSystem.EmissionModule emissionModule:
                        {
                            ParticleSystem.Burst[] bursts = new ParticleSystem.Burst[emissionModule.burstCount];
                            int burstCount = emissionModule.GetBursts(bursts);
                            Array.Resize(ref bursts, burstCount);

                            buildPropertyWithValueWriteOperation("bursts", bursts, builder);

                            break;
                        }
                        case ParticleSystem.CollisionModule collisionModule:
                        {
                            Transform[] planes = new Transform[collisionModule.maxPlaneCount];

                            for (int i = 0; i < planes.Length; i++)
                            {
                                planes[i] = collisionModule.GetPlane(i);
                            }

                            buildPropertyWithValueWriteOperation("planes", planes, builder);

                            break;
                        }
                        case ParticleSystem.TriggerModule triggerModule:
                        {
                            Component[] colliders = new Component[triggerModule.maxColliderCount];

                            for (int i = 0; i < colliders.Length; i++)
                            {
                                colliders[i] = triggerModule.GetCollider(i);
                            }

                            buildPropertyWithValueWriteOperation("colliders", colliders, builder);

                            break;
                        }
                        case ParticleSystem.SubEmittersModule subEmitters:
                        {
                            builder.AddPropertyName("emitters");
                            builder.AddStartArray();

                            for (int i = 0; i < subEmitters.subEmittersCount; i++)
                            {
                                builder.AddStartObject();

                                ParticleSystem emitterParticleSystem = subEmitters.GetSubEmitterSystem(i);
                                buildPropertyWithValueWriteOperation("emitterParticleSystem", emitterParticleSystem, builder);

                                ParticleSystemSubEmitterType emitterType = subEmitters.GetSubEmitterType(i);
                                buildPropertyWithValueWriteOperation("emitterType", emitterType, builder);

                                ParticleSystemSubEmitterProperties emitterProperties = subEmitters.GetSubEmitterProperties(i);
                                buildPropertyWithValueWriteOperation("emitterProperties", emitterProperties, builder);

                                float emitterProbability = subEmitters.GetSubEmitterEmitProbability(i);
                                buildPropertyWithValueWriteOperation("emitterProbability", emitterProbability, builder);

                                builder.AddEndObject();
                            }

                            builder.AddEndArray();

                            break;
                        }
                        case ParticleSystem.TextureSheetAnimationModule textureSheetAnimationModule:
                        {
                            builder.AddPropertyName("sprites");
                            builder.AddStartArray();

                            for (int i = 0; i < textureSheetAnimationModule.spriteCount; i++)
                            {
                                Sprite sprite = textureSheetAnimationModule.GetSprite(i);
                                buildWriteOperation(sprite, builder);
                            }

                            builder.AddEndArray();

                            break;
                        }
                        case ParticleSystem.CustomDataModule customDataModule:
                        {
                            builder.AddPropertyName("streams");
                            builder.AddStartArray();

                            for (ParticleSystemCustomData stream = ParticleSystemCustomData.Custom1; stream <= ParticleSystemCustomData.Custom2; stream++)
                            {
                                builder.AddStartObject();

                                buildPropertyWithValueWriteOperation("stream", stream, builder);

                                ParticleSystemCustomDataMode mode = customDataModule.GetMode(stream);
                                buildPropertyWithValueWriteOperation("mode", mode, builder);

                                int vectorComponentCount = customDataModule.GetVectorComponentCount(stream);
                                buildPropertyWithValueWriteOperation("vectorComponentCount", vectorComponentCount, builder);

                                builder.AddPropertyName("vectorComponents");
                                builder.AddStartArray();

                                for (int i = 0; i < vectorComponentCount; i++)
                                {
                                    buildWriteOperation(customDataModule.GetVector(stream, i), builder);
                                }

                                builder.AddEndArray();

                                ParticleSystem.MinMaxGradient color = customDataModule.GetColor(stream);
                                buildPropertyWithValueWriteOperation("color", color, builder);

                                builder.AddEndObject();
                            }

                            builder.AddEndArray();

                            break;
                        }
                    }

                    builder.AddEndObject();

                    return true;
                }
                case HumanDescription:
                case HumanBone:
                case CharacterInfo:
                {
                    builder.AddStartObject();

                    buildTypeFieldWriteOperation(value.GetType(), builder);

                    buildSerializedMemberWriteOperations(value, builder, t =>
                    {
                        return new MemberSerializationContext(MemberTypes.Field | MemberTypes.Property,
                                                              false,
                                                              false,
                                                              false);
                    });

                    builder.AddEndObject();
                    return true;
                }
            }

            return false;
        }

        bool buildUnityObjectWriteOperation(object value, WriteOperationBuilder builder)
        {
            if (value is not UnityEngine.Object obj || !obj)
                return false;

            if (!isSerializingRootValue)
            {
                void addComponentString(StringBuilder sb, UnityEngine.Object obj)
                {
                    sb.Append($"component<{obj.GetType().Name}>(");

                    if (obj is Component component && tryGetComponentIndex(obj.GetGameObject(), component, out int componentIndex))
                    {
                        sb.Append($"component_idx={componentIndex}");
                    }

                    sb.Append(")");
                }

                void addObjectRefPath(StringBuilder sb, IEnumerable<Transform> childOrder, bool appendRootName)
                {
                    sb.Append("objref('");

                    bool appendedAnyObjectPath = false;
                    int currentChildIndex = 0;

                    void appendPath(string name)
                    {
                        if (appendedAnyObjectPath)
                            sb.Append('/');

                        sb.Append(name);
                        appendedAnyObjectPath = true;
                    }

                    if (appendRootName)
                    {
                        appendPath("$root");
                    }

                    List<string> childIndexNames = [];

                    if (childOrder is ICollection collection)
                    {
                        childIndexNames.Capacity = collection.Count;
                    }

                    foreach (Transform child in childOrder)
                    {
                        appendPath(child.name);

                        string childIndexName;
                        if (tryGetChildIndex(child, out int childIndex))
                        {
                            childIndexName = childIndex.ToString();
                        }
                        else if (currentChildIndex == 0 && child.parent == null)
                        {
                            childIndexName = "$root";
                        }
                        else
                        {
                            throw new Exception($"Failed to find child index for '{child}' ({currentChildIndex})");
                        }

                        childIndexNames.Add(childIndexName);

                        appendedAnyObjectPath = true;
                        currentChildIndex++;
                    }

                    sb.Append("'");

                    sb.Append($", child_idxs=[{string.Join(", ", childIndexNames)}]");

                    sb.Append(")");
                }

                bool tryGetAssetRefString(UnityEngine.Object obj, out string assetRefString)
                {
                    // Resolve asset reference
                    // - Check object for asset reference
                    // - Check parent(s) for asset reference

                    StringBuilder stringBuilder = HG.StringBuilderPool.RentStringBuilder();

                    GameObject gameObject = obj.GetGameObject();

                    UnityEngine.Object assetKey = gameObject ? gameObject : obj;
                    GameObject assetKeyGameObject;

                    bool appendComponentType = obj != assetKey;
                    bool foundAsset = false;

                    Stack<Transform> childPathStack = new Stack<Transform>();

                    do
                    {
                        if (AddressablesIterator.AssetInfoLookup.TryGetValue(assetKey, out AssetInfo assetInfo))
                        {
                            stringBuilder.Append($"assetref<{assetInfo.AssetType.FullName}>('{assetInfo.Key}')");

                            if (childPathStack.Count > 0)
                            {
                                stringBuilder.Append('.');
                                addObjectRefPath(stringBuilder, childPathStack, false);
                            }

                            foundAsset = true;
                            break;
                        }

                        assetKeyGameObject = assetKey as GameObject;

                        if (assetKeyGameObject)
                        {
                            childPathStack.Push(assetKeyGameObject.transform);
                        }

                    } while (assetKeyGameObject && (assetKey = assetKeyGameObject.transform.parent?.gameObject));

                    if (foundAsset)
                    {
                        if (appendComponentType)
                        {
                            stringBuilder.Append('.');
                            addComponentString(stringBuilder, obj);
                        }

                        assetRefString = stringBuilder.ToString();
                    }
                    else
                    {
                        assetRefString = default;
                    }

                    stringBuilder = HG.StringBuilderPool.ReturnStringBuilder(stringBuilder);

                    return foundAsset;
                }

                bool tryGetChildRefString(UnityEngine.Object obj, Transform root, out string childRefString)
                {
                    GameObject gameObject = obj.GetGameObject();
                    if (!gameObject)
                    {
                        childRefString = default;
                        return false;
                    }

                    Transform transform = gameObject.transform;
                    if (transform.root != root)
                    {
                        childRefString = default;
                        return false;
                    }

                    Stack<Transform> childPathStack = new Stack<Transform>();

                    for (Transform current = transform; current != root; current = current.parent)
                    {
                        childPathStack.Push(current);
                    }

                    StringBuilder stringBuilder = HG.StringBuilderPool.RentStringBuilder();

                    addObjectRefPath(stringBuilder, childPathStack, true);

                    stringBuilder.Append('.');

                    addComponentString(stringBuilder, obj);

                    childRefString = stringBuilder.ToString();

                    stringBuilder = HG.StringBuilderPool.ReturnStringBuilder(stringBuilder);

                    return true;
                }

                Transform[] serializingObjectRoots = _serializingObjectRoots.ToArray();
                for (int i = serializingObjectRoots.Length - 1; i >= 0; i--)
                {
                    if (tryGetChildRefString(obj, serializingObjectRoots[i], out string childRefString))
                    {
                        builder.AddValueRaw(childRefString);
                        return true;
                    }
                }

                if (tryGetAssetRefString(obj, out string assetRefString))
                {
                    builder.AddValueRaw(assetRefString);
                    return true;
                }
            }

            if (buildGameObjectWriteOperation(obj, builder))
                return true;

            if (buildGenericUnityObjectWriteOperation(obj, builder))
                return true;

            return false;
        }

        bool buildGameObjectWriteOperation(UnityEngine.Object value, WriteOperationBuilder builder)
        {
            GameObject gameObject = value.GetGameObject();
            if (gameObject is null)
                return false;

            Transform transform = gameObject.transform;

            Transform parent = transform.parent;
            bool isRootObject = !parent;

            builder.AddStartObject();

            if (tryGetChildIndex(transform, out int childIndex))
            {
                buildPropertyWithValueWriteOperation("$child_idx", childIndex, builder);
            }

            buildPropertyWithValueWriteOperation("name", gameObject.name, builder);

            buildPropertyWithValueWriteOperation("hideFlags", gameObject.hideFlags, builder);

            buildPropertyWithValueWriteOperation("layer", (LayerMask)(1 << gameObject.layer), builder);

            buildPropertyWithValueWriteOperation("activeSelf", gameObject.activeSelf, builder);

            buildPropertyWithValueWriteOperation("activeInHierarchy", gameObject.activeInHierarchy, builder);

            buildPropertyWithValueWriteOperation("isStatic", gameObject.isStatic, builder);

            buildPropertyWithValueWriteOperation("tag", gameObject.tag, builder);

            if (isRootObject)
            {
                buildPropertyWithValueWriteOperation("scene", gameObject.scene, builder);

                buildPropertyWithValueWriteOperation("sceneCullingMask", gameObject.sceneCullingMask, builder);
            }

            builder.AddPropertyName("$transform");
            buildTransformWriteOperation(transform, builder);

            builder.AddPropertyName("$components");
            builder.AddStartArray();

            foreach (Component component in gameObject.GetComponents(typeof(Component)))
            {
                if (component is Transform)
                    continue;

                buildComponentWriteOperation(component, builder);
            }

            builder.AddEndArray();

            builder.AddPropertyName("$children");
            builder.AddStartArray();

            for (int i = 0; i < transform.childCount; i++)
            {
                buildGameObjectWriteOperation(transform.GetChild(i), builder);
            }

            builder.AddEndArray();

            builder.AddEndObject();

            return true;
        }

        bool tryGetComponentIndex(GameObject obj, Component component, out int index)
        {
            index = 0;
            if (!obj)
                return false;

            foreach (Component comp in obj.GetComponents(typeof(Component)))
            {
                if (comp is Transform)
                    continue;

                if (comp == component)
                    return true;

                index++;
            }

            return false;
        }

        bool tryGetChildIndex(Transform transform, out int index)
        {
            index = 0;
            Transform parent = transform ? transform.parent : null;
            if (!parent)
                return false;

            for (index = 0; index < parent.childCount; index++)
            {
                if (parent.GetChild(index) == transform)
                {
                    return true;
                }
            }

            return false;
        }

        bool buildTransformWriteOperation(Transform value, WriteOperationBuilder builder)
        {
            builder.AddStartObject();

            if (value is RectTransform rectTransform)
            {
                buildPropertyWithValueWriteOperation("rect", rectTransform.rect, builder);

                buildPropertyWithValueWriteOperation("anchorMin", rectTransform.anchorMin, builder);

                buildPropertyWithValueWriteOperation("anchorMax", rectTransform.anchorMax, builder);

                buildPropertyWithValueWriteOperation("anchoredPosition", rectTransform.anchoredPosition, builder);

                buildPropertyWithValueWriteOperation("sizeDelta", rectTransform.sizeDelta, builder);

                buildPropertyWithValueWriteOperation("pivot", rectTransform.pivot, builder);
            }
            else
            {
                buildPropertyWithValueWriteOperation("localPosition", value.localPosition, builder);

                buildPropertyWithValueWriteOperation("localRotation", value.localRotation, builder);

                buildPropertyWithValueWriteOperation("localScale", value.localScale, builder);
            }

            builder.AddEndObject();

            return true;
        }

        bool buildComponentWriteOperation(Component value, WriteOperationBuilder builder)
        {
            if (!value)
            {
                builder.AddNull();
                return true;
            }

            builder.AddStartObject();

            Type type = value.GetType();

            buildTypeFieldWriteOperation(type, builder);

            if (tryGetComponentIndex(value.gameObject, value, out int componentIndex))
            {
                buildPropertyWithValueWriteOperation("$component_idx", componentIndex, builder);
            }

            buildSerializedMemberWriteOperations(value, builder, type =>
            {
                bool isUnityType = isUnityScriptType(type);

                return new MemberSerializationContext(isUnityType ? MemberTypes.Property : MemberTypes.Field,
                                                      isUnityType,
                                                      false,
                                                      isUnityType && type.IsAssignableFrom(typeof(Component)));
            });

            builder.AddEndObject();

            return true;
        }
        
        bool buildGenericUnityObjectWriteOperation(UnityEngine.Object value, WriteOperationBuilder builder)
        {
            if (!value)
            {
                builder.AddNull();
                return true;
            }

            builder.AddStartObject();

            Type type = value.GetType();

            buildTypeFieldWriteOperation(type, builder);

            buildSerializedMemberWriteOperations(value, builder, type =>
            {
                bool isUnityType = isUnityScriptType(type);

                return new MemberSerializationContext(isUnityType ? MemberTypes.Property : MemberTypes.Field,
                                                      isUnityType,
                                                      false,
                                                      false);
            });

            builder.AddEndObject();

            return true;
        }

        static bool isUnityScriptType(Type type)
        {
            return !string.IsNullOrEmpty(type.Namespace) &&
                   type.Namespace.StartsWith(nameof(UnityEngine)) &&
                   !typeof(MonoBehaviour).IsAssignableFrom(type) &&
                   !typeof(ScriptableObject).IsAssignableFrom(type);
        }

        readonly struct MemberSerializationContext
        {
            public readonly bool Skip;
            public readonly MemberTypes MemberTypesToSerialize;
            public readonly bool ShouldConsiderUnityScriptType;
            public readonly bool IncludeObsolete;

            public MemberSerializationContext(MemberTypes memberTypesToSerialize, bool shouldConsiderUnityScriptType, bool includeObsolete, bool skip)
            {
                Skip = skip;
                MemberTypesToSerialize = memberTypesToSerialize;
                ShouldConsiderUnityScriptType = shouldConsiderUnityScriptType;
                IncludeObsolete = includeObsolete;
            }
        }

        bool buildSerializedMemberWriteOperations(object value, WriteOperationBuilder builder, Func<Type, MemberSerializationContext> baseTypeContextGetter)
        {
            if (baseTypeContextGetter is null)
                throw new ArgumentNullException(nameof(baseTypeContextGetter));

            if (value is null)
                return false;

            Type type = value.GetType();

            List<Type> hierarchyTypes = ReflectionUtils.GetHierarchyTypes(type);
            hierarchyTypes.Reverse();

            foreach (Type baseType in hierarchyTypes)
            {
                MemberSerializationContext serializationContext = baseTypeContextGetter(baseType);
                if (serializationContext.Skip)
                    continue;

                const BindingFlags FLAGS = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;

                MemberTypes memberTypes = serializationContext.MemberTypesToSerialize;

                MemberInfo[] members = ReflectionUtils.GetSerializableMembers(baseType, memberTypes, FLAGS).ToArray();

                foreach (MemberInfo member in members)
                {
                    if (!serializationContext.IncludeObsolete && member.GetCustomAttribute(typeof(ObsoleteAttribute)) != null)
                        continue;

                    if (baseType == typeof(Renderer))
                    {
                        switch (member.Name)
                        {
                            // use materials/sharedMaterials instead
                            case nameof(Renderer.material):
                            case nameof(Renderer.sharedMaterial):

                            // Not necessary
                            case nameof(Renderer.worldToLocalMatrix):
                            case nameof(Renderer.localToWorldMatrix):
                                continue;
                        }
                    }
                    else if (baseType == typeof(Animator))
                    {
                        switch (member.Name)
                        {
                            // Only valid in OnAnimatorIK or OnStateIK
                            case nameof(Animator.bodyPosition):
                            case nameof(Animator.bodyRotation):
                                continue;
                        }
                    }

                    if (serializationContext.ShouldConsiderUnityScriptType)
                    {
                        // Use *only* shared versions of properties where available, otherwise new instances are created
                        if (!member.Name.StartsWith("shared", StringComparison.OrdinalIgnoreCase))
                        {
                            string sharedName = "shared" + member.Name;
                            if (members.Any(m => string.Equals(m.Name, sharedName, StringComparison.OrdinalIgnoreCase)))
                            {
                                continue;
                            }
                        }
                    }

                    Type memberType;
                    object memberValue;
                    switch (member)
                    {
                        case FieldInfo field:
                            memberType = field.FieldType;

                            try
                            {
                                memberValue = field.GetValue(value);
                            }
                            catch (Exception e)
                            {
                                Log.Error_NoCallerPrefix($"Failed to get field value of {member.DeclaringType.FullName}.{member.Name}: {e}");

                                builder.AddPropertyName(member.Name);
                                builder.AddNull($"Error retrieving member value: {e.Message}");

                                continue;
                            }

                            break;
                        case PropertyInfo property:
                            memberType = property.PropertyType;

                            try
                            {
                                memberValue = property.GetValue(value);
                            }
                            catch (Exception e)
                            {
                                Log.Error_NoCallerPrefix($"Failed to get property value of {member.DeclaringType.FullName}.{member.Name}: {e}");

                                builder.AddPropertyName(member.Name);
                                builder.AddNull($"Error retrieving member value: {e.Message}");

                                continue;
                            }

                            break;
                        default:
                            throw new NotImplementedException($"Member type {member.MemberType} ({member}) is not implemented");
                    }

                    if (!tryBuildPropertyWithValueWriteOperation(member.Name, memberValue, builder))
                    {
                        Log.Warning($"Failed to determine write operation for serialized {member.MemberType} {member.DeclaringType.FullName}.{member.Name}: {memberType.Name})");
                    }
                }
            }

            return true;
        }
    }
}
