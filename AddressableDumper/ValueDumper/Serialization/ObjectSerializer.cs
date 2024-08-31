using AddressableDumper.Utils;
using AddressableDumper.Utils.Extensions;
using Newtonsoft.Json;
using RoR2;
using RoR2.Navigation;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.PostProcessing;
using UnityEngine.SceneManagement;

namespace AddressableDumper.ValueDumper.Serialization
{
    public class ObjectSerializer
    {
        struct ObjectSerializationArgs
        {
            public int MaxCollectionCapacity;

            public string CustomFormat;
        }

        readonly struct SerializingObjectStep
        {
            public readonly object Value;

            public readonly UnityEngine.Object AsUnityObject;

            public readonly Transform RootTransform;

            public SerializingObjectStep(object value)
            {
                Value = value;

                if (Value is UnityEngine.Object unityObject)
                {
                    AsUnityObject = unityObject;

                    GameObject gameObject = AsUnityObject.GetGameObject();
                    if (gameObject)
                    {
                        RootTransform = gameObject.transform.root;
                    }
                }
            }
        }

        static readonly IFormatProvider _formatProvider = new DumpedValueFormatter();

        readonly IHashProvider _hashProvider = new DumpedValueHasher();

        readonly JsonWriter _writer;
        readonly object _rootValue;

        readonly Stack<SerializingObjectStep> _serializingObjectStack = [];

        bool isSerializingRootValue => _serializingObjectStack.Count <= 1;

        public Scene? SerializingScene { get; set; }

        public Transform[] AdditionalReferenceRoots { get; set; } = [];

        public bool ExcludeNonDeterministicValues { get; set; } = false;

        public ObjectSerializer(JsonWriter writer, object value)
        {
            _writer = writer;
            _rootValue = value;
        }

        public void Write()
        {
            ObjectSerializationArgs serializationArgs = new ObjectSerializationArgs
            {
                MaxCollectionCapacity = 150,
                CustomFormat = null
            };

            writeValue(_rootValue, serializationArgs);
        }

        void writeValue(object value, in ObjectSerializationArgs serializationArgs)
        {
            WriteOperationBuilder builder = new WriteOperationBuilder(new JsonWriteOperationBuilderWriter(_writer));

            try
            {
                if (!buildWriteOperation(value, builder, serializationArgs))
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
        
        bool buildWriteOperation(object value, WriteOperationBuilder builder, in ObjectSerializationArgs serializationArgs)
        {
            // TODO: This does not detect recursive references in value types
            foreach (SerializingObjectStep step in _serializingObjectStack)
            {
                if (ReferenceEquals(step.Value, value))
                {
                    builder.AddNull($"Recursive reference ({value})");
                    return true;
                }
            }

            // Don't serialize references to the current scene
            if (SerializingScene.HasValue && value is Scene scene && SerializingScene == scene)
            {
                return false;
            }

            _serializingObjectStack.Push(new SerializingObjectStep(value));

            bool anythingWritten = buildWrite(value, builder, serializationArgs);

            if (_serializingObjectStack.Count == 0 || !ReferenceEquals(_serializingObjectStack.Peek().Value, value))
            {
                throw new Exception($"Invalid state, expected {value} at top of stack, found {(_serializingObjectStack.Count == 0 ? "nothing" : _serializingObjectStack.Peek())}");
            }

            _serializingObjectStack.Pop();
            return anythingWritten;

            bool buildWrite(object value, WriteOperationBuilder builder, in ObjectSerializationArgs serializationArgs)
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

                if (buildCustomFormattedWriteOperation(value, builder, serializationArgs))
                    return true;
                
                if (buildCollectionWriteOperation(value, builder, serializationArgs))
                    return true;

                if (buildUnityObjectWriteOperation(value, builder, serializationArgs))
                    return true;

                if (getSerializableTypeWriteOperation(value, builder, serializationArgs))
                    return true;

                return false;
            }
        }

        bool buildCollectionWriteOperation(object value, WriteOperationBuilder builder, in ObjectSerializationArgs serializationArgs)
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
                    if (writtenItems >= serializationArgs.MaxCollectionCapacity)
                    {
                        byte[] elementsHash = _hashProvider.ComputeHash(value);
                        string hashString = Convert.ToBase64String(elementsHash);

                        StringBuilder commentBuilder = HG.StringBuilderPool.RentStringBuilder();

                        commentBuilder.Append($"... Remaining {collection.Count - writtenItems} value(s) excluded ");

                        if (!string.IsNullOrEmpty(hashString))
                        {
                            commentBuilder.Append($"(hash: '{hashString}') ");
                        }

                        commentBuilder.Append("...");

                        builder.AddComment(commentBuilder.ToString());

                        commentBuilder = HG.StringBuilderPool.ReturnStringBuilder(commentBuilder);
                        break;
                    }

                    if (buildWriteOperation(element, builder, serializationArgs))
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

        bool getSerializableTypeWriteOperation(object value, WriteOperationBuilder builder, in ObjectSerializationArgs serializationArgs)
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

            buildTypeFieldWriteOperation(type, serializedObjectBuilder, serializationArgs);

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

                    if (tryBuildPropertyWithValueWriteOperation(field.Name, fieldValue, serializedObjectBuilder, serializationArgs))
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

        bool tryBuildPropertyWithValueWriteOperation(string propertyName, object value, WriteOperationBuilder builder, in ObjectSerializationArgs serializationArgs)
        {
            WriteOperationBuilder propertyValueBuilder = new WriteOperationBuilder(builder)
            {
                AutoFlush = false,
                AutoFlushCapacity = builder.AutoFlushCapacity,
            };

            propertyValueBuilder.AddPropertyName(propertyName);

            propertyValueBuilder.AutoFlush = true;

            if (buildWriteOperation(value, propertyValueBuilder, serializationArgs))
            {
                propertyValueBuilder.Flush();
                return true;
            }
            else
            {
                return false;
            }
        }

        void buildPropertyWithValueWriteOperation(string propertyName, object value, WriteOperationBuilder builder, in ObjectSerializationArgs serializationArgs)
        {
            builder.AddPropertyName(propertyName);

            if (!buildWriteOperation(value, builder, serializationArgs))
            {
                throw new ArgumentException($"Value {value} ({value?.GetType().FullName}) was not serialized for property '{propertyName}'");
            }
        }

        void buildTypeFieldWriteOperation(Type type, WriteOperationBuilder builder, in ObjectSerializationArgs serializationArgs)
        {
            buildPropertyWithValueWriteOperation("$type", type.FullName, builder, serializationArgs);
        }

        bool buildCustomFormattedWriteOperation(object value, WriteOperationBuilder builder, in ObjectSerializationArgs serializationArgs)
        {
            string formatString = serializationArgs.CustomFormat;
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
                        buildPropertyWithValueWriteOperation($"row{i}", matrix4x4.GetRow(i), builder, serializationArgs);
                    }

                    builder.AddEndObject();

                    return true;
                }
                case ParticleSystem.MinMaxCurve minMaxCurve:
                {
                    builder.AddStartObject();

                    buildTypeFieldWriteOperation(value.GetType(), builder, serializationArgs);

                    buildPropertyWithValueWriteOperation("mode", minMaxCurve.mode, builder, serializationArgs);

                    switch (minMaxCurve.mode)
                    {
                        case ParticleSystemCurveMode.Constant:
                            buildPropertyWithValueWriteOperation("constant", minMaxCurve.constantMax, builder, serializationArgs);
                            break;
                        case ParticleSystemCurveMode.Curve:
                            buildPropertyWithValueWriteOperation("curveMultiplier", minMaxCurve.curveMultiplier, builder, serializationArgs);
                            buildPropertyWithValueWriteOperation("curve", minMaxCurve.curveMax, builder, serializationArgs);
                            break;
                        case ParticleSystemCurveMode.TwoCurves:
                            buildPropertyWithValueWriteOperation("curveMultiplier", minMaxCurve.curveMultiplier, builder, serializationArgs);
                            buildPropertyWithValueWriteOperation("curveMin", minMaxCurve.curveMin, builder, serializationArgs);
                            buildPropertyWithValueWriteOperation("curveMax", minMaxCurve.curveMax, builder, serializationArgs);
                            break;
                        case ParticleSystemCurveMode.TwoConstants:
                            buildPropertyWithValueWriteOperation("constantMin", minMaxCurve.constantMin, builder, serializationArgs);
                            buildPropertyWithValueWriteOperation("constantMax", minMaxCurve.constantMax, builder, serializationArgs);
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

                    buildTypeFieldWriteOperation(value.GetType(), builder, serializationArgs);

                    buildPropertyWithValueWriteOperation("mode", minMaxGradient.mode, builder, serializationArgs);

                    switch (minMaxGradient.mode)
                    {
                        case ParticleSystemGradientMode.Color:
                            buildPropertyWithValueWriteOperation("color", minMaxGradient.colorMax, builder, serializationArgs);
                            break;
                        case ParticleSystemGradientMode.Gradient:
                            buildPropertyWithValueWriteOperation("gradient", minMaxGradient.gradientMax, builder, serializationArgs);
                            break;
                        case ParticleSystemGradientMode.TwoColors:
                            buildPropertyWithValueWriteOperation("colorMin", minMaxGradient.colorMin, builder, serializationArgs);
                            buildPropertyWithValueWriteOperation("colorMax", minMaxGradient.colorMax, builder, serializationArgs);
                            break;
                        case ParticleSystemGradientMode.TwoGradients:
                            buildPropertyWithValueWriteOperation("gradientMin", minMaxGradient.gradientMin, builder, serializationArgs);
                            buildPropertyWithValueWriteOperation("gradientMax", minMaxGradient.gradientMax, builder, serializationArgs);
                            break;
                        case ParticleSystemGradientMode.RandomColor:
                            buildPropertyWithValueWriteOperation("colorCurve", minMaxGradient.gradientMax, builder, serializationArgs);
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

                    buildTypeFieldWriteOperation(value.GetType(), builder, serializationArgs);

                    buildPropertyWithValueWriteOperation("time", animationEvent.time, builder, serializationArgs);

                    buildPropertyWithValueWriteOperation("functionName", animationEvent.functionName, builder, serializationArgs);

                    buildPropertyWithValueWriteOperation("stringParameter", animationEvent.stringParameter, builder, serializationArgs);

                    buildPropertyWithValueWriteOperation("floatParameter", animationEvent.floatParameter, builder, serializationArgs);

                    buildPropertyWithValueWriteOperation("intParameter", animationEvent.intParameter, builder, serializationArgs);

                    buildPropertyWithValueWriteOperation("objectReferenceParameter", animationEvent.objectReferenceParameter, builder, serializationArgs);

                    buildPropertyWithValueWriteOperation("isFiredByLegacy", animationEvent.isFiredByLegacy, builder, serializationArgs);

                    if (animationEvent.isFiredByLegacy)
                    {
                        buildPropertyWithValueWriteOperation("animationState", animationEvent.animationState, builder, serializationArgs);
                    }

                    buildPropertyWithValueWriteOperation("isFiredByAnimator", animationEvent.isFiredByAnimator, builder, serializationArgs);

                    if (animationEvent.isFiredByAnimator)
                    {
                        buildPropertyWithValueWriteOperation("animatorStateInfo", animationEvent.animatorStateInfo, builder, serializationArgs);

                        buildPropertyWithValueWriteOperation("animatorClipInfo", animationEvent.animatorClipInfo, builder, serializationArgs);
                    }

                    builder.AddEndObject();

                    return true;
                }
                case SphericalHarmonicsL2 sphericalHarmonicsL2:
                {
                    byte[] valueHash = _hashProvider.ComputeHash(sphericalHarmonicsL2);

                    builder.AddValueRaw($"valuehash('{Convert.ToBase64String(valueHash)}')");

                    return true;
                }
                case LocalKeywordSpace localKeywordSpace:
                {
                    builder.AddStartObject();

                    buildTypeFieldWriteOperation(value.GetType(), builder, serializationArgs);

                    buildPropertyWithValueWriteOperation("keywords", localKeywordSpace.keywords, builder, serializationArgs);

                    builder.AddEndObject();

                    return true;
                }
                case RenderBuffer renderBuffer:
                {
                    builder.AddStartObject();

                    buildTypeFieldWriteOperation(value.GetType(), builder, serializationArgs);

                    buildPropertyWithValueWriteOperation("loadAction", renderBuffer.loadAction, builder, serializationArgs);

                    buildPropertyWithValueWriteOperation("storeAction", renderBuffer.storeAction, builder, serializationArgs);

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

                    buildTypeFieldWriteOperation(value.GetType(), builder, serializationArgs);

                    buildSerializedMemberWriteOperations(value, builder, serializationArgs, t =>
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
                case ParticleSystem.Burst:
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
                case PostProcessBundle:
                case AnimatorControllerParameter:
                case LocalKeyword:
                {
                    builder.AddStartObject();

                    buildTypeFieldWriteOperation(value.GetType(), builder, serializationArgs);

                    buildSerializedMemberWriteOperations(value, builder, serializationArgs, t =>
                    {
                        return new MemberSerializationContext(MemberTypes.Property,
                                                              false,
                                                              false,
                                                              false);
                    });

                    builder.AddEndObject();

                    return true;
                }
                case ParticleSystem.MainModule:
                case ParticleSystem.EmissionModule:
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
                case ParticleSystem.LifetimeByEmitterSpeedModule:
                {
                    builder.AddStartObject();

                    buildTypeFieldWriteOperation(value.GetType(), builder, serializationArgs);

                    bool enabled = value switch
                    {
                        ParticleSystem.MainModule => true,
                        ParticleSystem.EmissionModule m => m.enabled,
                        ParticleSystem.ShapeModule m => m.enabled,
                        ParticleSystem.VelocityOverLifetimeModule m => m.enabled,
                        ParticleSystem.LimitVelocityOverLifetimeModule m => m.enabled,
                        ParticleSystem.InheritVelocityModule m => m.enabled,
                        ParticleSystem.ForceOverLifetimeModule m => m.enabled,
                        ParticleSystem.ColorOverLifetimeModule m => m.enabled,
                        ParticleSystem.ColorBySpeedModule m => m.enabled,
                        ParticleSystem.SizeOverLifetimeModule m => m.enabled,
                        ParticleSystem.SizeBySpeedModule m => m.enabled,
                        ParticleSystem.RotationOverLifetimeModule m => m.enabled,
                        ParticleSystem.RotationBySpeedModule m => m.enabled,
                        ParticleSystem.ExternalForcesModule m => m.enabled,
                        ParticleSystem.NoiseModule m => m.enabled,
                        ParticleSystem.CollisionModule m => m.enabled,
                        ParticleSystem.TriggerModule m => m.enabled,
                        ParticleSystem.SubEmittersModule m => m.enabled,
                        ParticleSystem.TextureSheetAnimationModule m => m.enabled,
                        ParticleSystem.LightsModule m => m.enabled,
                        ParticleSystem.TrailModule m => m.enabled,
                        ParticleSystem.CustomDataModule m => m.enabled,
                        ParticleSystem.LifetimeByEmitterSpeedModule m => m.enabled,
                        _ => throw new NotImplementedException($"{value.GetType().FullName} is not implemented")
                    };

                    if (enabled)
                    {
                        buildSerializedMemberWriteOperations(value, builder, serializationArgs, t =>
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

                                buildPropertyWithValueWriteOperation("bursts", bursts, builder, serializationArgs);

                                break;
                            }
                            case ParticleSystem.CollisionModule collisionModule:
                            {
                                Transform[] planes = new Transform[collisionModule.planeCount];

                                for (int i = 0; i < planes.Length; i++)
                                {
                                    planes[i] = collisionModule.GetPlane(i);
                                }

                                buildPropertyWithValueWriteOperation("planes", planes, builder, serializationArgs);

                                break;
                            }
                            case ParticleSystem.TriggerModule triggerModule:
                            {
                                Component[] colliders = new Component[triggerModule.colliderCount];

                                for (int i = 0; i < colliders.Length; i++)
                                {
                                    colliders[i] = triggerModule.GetCollider(i);
                                }

                                buildPropertyWithValueWriteOperation("colliders", colliders, builder, serializationArgs);

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
                                    buildPropertyWithValueWriteOperation("emitterParticleSystem", emitterParticleSystem, builder, serializationArgs);

                                    ParticleSystemSubEmitterType emitterType = subEmitters.GetSubEmitterType(i);
                                    buildPropertyWithValueWriteOperation("emitterType", emitterType, builder, serializationArgs);

                                    ParticleSystemSubEmitterProperties emitterProperties = subEmitters.GetSubEmitterProperties(i);
                                    buildPropertyWithValueWriteOperation("emitterProperties", emitterProperties, builder, serializationArgs);

                                    float emitterProbability = subEmitters.GetSubEmitterEmitProbability(i);
                                    buildPropertyWithValueWriteOperation("emitterProbability", emitterProbability, builder, serializationArgs);

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
                                    buildWriteOperation(sprite, builder, serializationArgs);
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

                                    buildPropertyWithValueWriteOperation("stream", stream, builder, serializationArgs);

                                    ParticleSystemCustomDataMode mode = customDataModule.GetMode(stream);
                                    buildPropertyWithValueWriteOperation("mode", mode, builder, serializationArgs);

                                    int vectorComponentCount = customDataModule.GetVectorComponentCount(stream);
                                    buildPropertyWithValueWriteOperation("vectorComponentCount", vectorComponentCount, builder, serializationArgs);

                                    builder.AddPropertyName("vectorComponents");
                                    builder.AddStartArray();

                                    for (int i = 0; i < vectorComponentCount; i++)
                                    {
                                        buildWriteOperation(customDataModule.GetVector(stream, i), builder, serializationArgs);
                                    }

                                    builder.AddEndArray();

                                    ParticleSystem.MinMaxGradient color = customDataModule.GetColor(stream);
                                    buildPropertyWithValueWriteOperation("color", color, builder, serializationArgs);

                                    builder.AddEndObject();
                                }

                                builder.AddEndArray();

                                break;
                            }
                        }
                    }
                    else
                    {
                        buildPropertyWithValueWriteOperation("enabled", enabled, builder, serializationArgs);
                    }

                    builder.AddEndObject();

                    return true;
                }
                case HumanDescription:
                case HumanBone:
                case CharacterInfo:
                case Attribute:
                {
                    builder.AddStartObject();

                    buildTypeFieldWriteOperation(value.GetType(), builder, serializationArgs);

                    buildSerializedMemberWriteOperations(value, builder, serializationArgs, t =>
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

        bool buildUnityObjectWriteOperation(object value, WriteOperationBuilder builder, in ObjectSerializationArgs serializationArgs)
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

                    sb.Append(')');
                }

                void addObjectRefPath(StringBuilder sb, IEnumerable<Transform> childOrder, string rootName)
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

                    List<string> childIndexNames = [];

                    if (childOrder is ICollection collection)
                    {
                        childIndexNames.Capacity = collection.Count;
                    }

                    if (!string.IsNullOrEmpty(rootName))
                    {
                        appendPath(rootName);
                        childIndexNames.Add("$root");
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

                    sb.Append('\'');

                    sb.Append($", child_idxs=[{string.Join(", ", childIndexNames)}]");

                    sb.Append(')');
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
                                addObjectRefPath(stringBuilder, childPathStack, null);
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

                    addObjectRefPath(stringBuilder, childPathStack, root.name);

                    stringBuilder.Append('.');

                    addComponentString(stringBuilder, obj);

                    childRefString = stringBuilder.ToString();

                    stringBuilder = HG.StringBuilderPool.ReturnStringBuilder(stringBuilder);

                    return true;
                }

                foreach (Transform referenceRoot in AdditionalReferenceRoots)
                {
                    if (tryGetChildRefString(obj, referenceRoot, out string childRefString))
                    {
                        builder.AddValueRaw(childRefString);
                        return true;
                    }
                }

                SerializingObjectStep[] serializingSteps = _serializingObjectStack.ToArray();
                // Skip currently serializing object (obj) at index 0
                for (int i = serializingSteps.Length - 1; i > 0; i--)
                {
                    SerializingObjectStep step = serializingSteps[i];

                    if (ReferenceEquals(step.Value, obj))
                        continue;

                    Transform rootTransform = step.RootTransform;
                    if (rootTransform)
                    {
                        if (tryGetChildRefString(obj, rootTransform, out string childRefString))
                        {
                            builder.AddValueRaw(childRefString);
                            return true;
                        }
                    }
                }

                if (tryGetAssetRefString(obj, out string assetRefString))
                {
                    builder.AddValueRaw(assetRefString);
                    return true;
                }
            }

            if (buildGameObjectWriteOperation(obj, builder, serializationArgs))
                return true;

            if (buildGenericUnityObjectWriteOperation(obj, builder, serializationArgs))
                return true;

            return false;
        }

        bool buildGameObjectWriteOperation(UnityEngine.Object value, WriteOperationBuilder builder, in ObjectSerializationArgs serializationArgs)
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
                buildPropertyWithValueWriteOperation("$child_idx", childIndex, builder, serializationArgs);
            }

            buildPropertyWithValueWriteOperation("name", gameObject.name, builder, serializationArgs);

            buildPropertyWithValueWriteOperation("hideFlags", gameObject.hideFlags, builder, serializationArgs);

            buildPropertyWithValueWriteOperation("layer", (LayerMask)(1 << gameObject.layer), builder, serializationArgs);

            buildPropertyWithValueWriteOperation("activeSelf", gameObject.activeSelf, builder, serializationArgs);

            buildPropertyWithValueWriteOperation("activeInHierarchy", gameObject.activeInHierarchy, builder, serializationArgs);

            buildPropertyWithValueWriteOperation("isStatic", gameObject.isStatic, builder, serializationArgs);

            buildPropertyWithValueWriteOperation("tag", gameObject.tag, builder, serializationArgs);

            if (isRootObject)
            {
                tryBuildPropertyWithValueWriteOperation("scene", gameObject.scene, builder, serializationArgs);

                buildPropertyWithValueWriteOperation("sceneCullingMask", gameObject.sceneCullingMask, builder, serializationArgs);
            }

            builder.AddPropertyName("$transform");
            buildTransformWriteOperation(transform, builder, serializationArgs);

            builder.AddPropertyName("$components");
            builder.AddStartArray();

            foreach (Component component in gameObject.GetComponents(typeof(Component)))
            {
                if (component is Transform)
                    continue;

                buildComponentWriteOperation(component, builder, serializationArgs);
            }

            builder.AddEndArray();

            builder.AddPropertyName("$children");
            builder.AddStartArray();

            for (int i = 0; i < transform.childCount; i++)
            {
                buildGameObjectWriteOperation(transform.GetChild(i), builder, serializationArgs);
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

        bool buildTransformWriteOperation(Transform value, WriteOperationBuilder builder, in ObjectSerializationArgs serializationArgs)
        {
            builder.AddStartObject();

            if (value is RectTransform rectTransform)
            {
                buildPropertyWithValueWriteOperation("rect", rectTransform.rect, builder, serializationArgs);

                buildPropertyWithValueWriteOperation("anchorMin", rectTransform.anchorMin, builder, serializationArgs);

                buildPropertyWithValueWriteOperation("anchorMax", rectTransform.anchorMax, builder, serializationArgs);

                buildPropertyWithValueWriteOperation("anchoredPosition", rectTransform.anchoredPosition, builder, serializationArgs);

                buildPropertyWithValueWriteOperation("sizeDelta", rectTransform.sizeDelta, builder, serializationArgs);

                buildPropertyWithValueWriteOperation("pivot", rectTransform.pivot, builder, serializationArgs);
            }
            else
            {
                buildPropertyWithValueWriteOperation("localPosition", value.localPosition, builder, serializationArgs);

                buildPropertyWithValueWriteOperation("localRotation", value.localRotation, builder, serializationArgs);

                buildPropertyWithValueWriteOperation("localScale", value.localScale, builder, serializationArgs);
            }

            builder.AddEndObject();

            return true;
        }

        bool buildComponentWriteOperation(Component value, WriteOperationBuilder builder, in ObjectSerializationArgs serializationArgs)
        {
            if (!value)
            {
                builder.AddNull();
                return true;
            }

            builder.AddStartObject();

            Type type = value.GetType();

            buildTypeFieldWriteOperation(type, builder, serializationArgs);

            if (tryGetComponentIndex(value.gameObject, value, out int componentIndex))
            {
                buildPropertyWithValueWriteOperation("$component_idx", componentIndex, builder, serializationArgs);
            }

            buildSerializedMemberWriteOperations(value, builder, serializationArgs, type =>
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
        
        bool buildGenericUnityObjectWriteOperation(UnityEngine.Object value, WriteOperationBuilder builder, in ObjectSerializationArgs serializationArgs)
        {
            if (!value)
            {
                builder.AddNull();
                return true;
            }

            builder.AddStartObject();

            Type type = value.GetType();

            buildTypeFieldWriteOperation(type, builder, serializationArgs);

            buildSerializedMemberWriteOperations(value, builder, serializationArgs, type =>
            {
                bool isUnityType = isUnityScriptType(type);

                return new MemberSerializationContext(isUnityType ? MemberTypes.Property : MemberTypes.Field,
                                                      isUnityType,
                                                      false,
                                                      false);
            });

            // TODO: Check for standard shader/material and don't serialize if so
            switch (value)
            {
                case Shader shader:
                {
                    builder.AddPropertyName("properties");

                    builder.AddStartArray();

                    int propertyCount = shader.GetPropertyCount();
                    for (int i = 0; i < propertyCount; i++)
                    {
                        builder.AddStartObject();

                        string propertyName = shader.GetPropertyName(i);
                        buildPropertyWithValueWriteOperation("name", propertyName, builder, serializationArgs);

                        ShaderPropertyType propertyType = shader.GetPropertyType(i);
                        buildPropertyWithValueWriteOperation("type", propertyType, builder, serializationArgs);

                        string propertyDescription = shader.GetPropertyDescription(i);
                        buildPropertyWithValueWriteOperation("description", propertyDescription, builder, serializationArgs);

                        string[] propertyAttributes = shader.GetPropertyAttributes(i);
                        buildPropertyWithValueWriteOperation("attributes", propertyAttributes, builder, serializationArgs);

                        ShaderPropertyFlags propertyFlags = shader.GetPropertyFlags(i);
                        buildPropertyWithValueWriteOperation("flags", propertyFlags, builder, serializationArgs);

                        object defaultValue;
                        switch (propertyType)
                        {
                            case ShaderPropertyType.Color:
                                defaultValue = (Color)shader.GetPropertyDefaultVectorValue(i);
                                break;
                            case ShaderPropertyType.Vector:
                                defaultValue = shader.GetPropertyDefaultVectorValue(i);
                                break;
                            case ShaderPropertyType.Float:
                            case ShaderPropertyType.Range:
                                defaultValue = shader.GetPropertyDefaultFloatValue(i);
                                break;
                            case ShaderPropertyType.Texture:
                                defaultValue = shader.GetPropertyTextureDefaultName(i);
                                break;
                            default:
                                throw new NotImplementedException($"Property type {propertyType} is not implemented");
                        }

                        buildPropertyWithValueWriteOperation("defaultValue", defaultValue, builder, serializationArgs);

                        switch (propertyType)
                        {
                            case ShaderPropertyType.Range:
                                Vector2 rangeLimits = shader.GetPropertyRangeLimits(i);
                                builder.AddPropertyName("rangeLimits");

                                builder.AddStartObject();

                                buildPropertyWithValueWriteOperation("min", rangeLimits.x, builder, serializationArgs);
                                buildPropertyWithValueWriteOperation("max", rangeLimits.y, builder, serializationArgs);

                                builder.AddEndObject();
                                break;
                            case ShaderPropertyType.Texture:
                                TextureDimension textureDimension = shader.GetPropertyTextureDimension(i);
                                buildPropertyWithValueWriteOperation("textureDimension", textureDimension, builder, serializationArgs);
                                break;
                        }

                        builder.AddEndObject();
                    }

                    builder.AddEndArray();
                    break;
                }
                case Material material when material.shader:
                {
                    Shader shader = material.shader;

                    builder.AddPropertyName("passes");

                    builder.AddStartArray();

                    for (int i = 0; i < material.passCount; i++)
                    {
                        builder.AddValue(material.GetPassName(i));
                    }

                    builder.AddEndArray();

                    builder.AddPropertyName("properties");

                    builder.AddStartObject();

                    int propertyCount = shader.GetPropertyCount();
                    for (int i = 0; i < propertyCount; i++)
                    {
                        string propertyName = shader.GetPropertyName(i);
                        builder.AddPropertyName(propertyName);

                        int propertyNameId = shader.GetPropertyNameId(i);
                        ShaderPropertyType propertyType = shader.GetPropertyType(i);

                        object propertyValue;
                        switch (propertyType)
                        {
                            case ShaderPropertyType.Color:
                                propertyValue = material.GetColor(propertyNameId);
                                break;
                            case ShaderPropertyType.Vector:
                                propertyValue = material.GetVector(propertyNameId);
                                break;
                            case ShaderPropertyType.Float:
                            case ShaderPropertyType.Range:
                                propertyValue = material.GetFloat(propertyNameId);
                                break;
                            case ShaderPropertyType.Texture:
                                propertyValue = material.GetTexture(propertyNameId);
                                break;
                            default:
                                throw new NotImplementedException($"Property type {propertyType} is not implemented");
                        }

                        buildWriteOperation(propertyValue, builder, serializationArgs);
                    }

                    builder.AddEndObject();

                    break;
                }
            }

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

        bool buildSerializedMemberWriteOperations(object value, WriteOperationBuilder builder, in ObjectSerializationArgs serializationArgs, Func<Type, MemberSerializationContext> baseTypeContextGetter)
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

                    bool appendValueHashInstead = false;

                    ObjectSerializationArgs memberSerializationArgs = serializationArgs;

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

                            case nameof(Renderer.bounds) when ExcludeNonDeterministicValues && value is ParticleSystemRenderer:
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
                    else if (baseType == typeof(Camera))
                    {
                        switch (member.Name)
                        {
                            // Does not make sense for value dump, effectively a random value every dump
                            case nameof(Camera.previousViewProjectionMatrix):
                                continue;
                        }
                    }
                    else if (baseType == typeof(Texture))
                    {
                        switch (member.Name)
                        {
                            // Does not make sense for value dump, effectively a random value every dump
                            case nameof(Texture.updateCount):
                                continue;
                        }
                    }
                    else if (baseType == typeof(Mesh))
                    {
                        switch (member.Name)
                        {
                            case nameof(Mesh.bindposes):
                            case nameof(Mesh.vertices):
                            case nameof(Mesh.normals):
                            case nameof(Mesh.tangents):
                            case nameof(Mesh.uv):
                            case nameof(Mesh.uv2):
                            case nameof(Mesh.uv3):
                            case nameof(Mesh.uv4):
                            case nameof(Mesh.uv5):
                            case nameof(Mesh.uv6):
                            case nameof(Mesh.uv7):
                            case nameof(Mesh.uv8):
                            case nameof(Mesh.colors):
                            case nameof(Mesh.colors32):
                            case nameof(Mesh.triangles):
                            case nameof(Mesh.boneWeights):
                                Mesh meshInstance = value as Mesh;
                                if (meshInstance && !meshInstance.isReadable)
                                    continue;

                                appendValueHashInstead = true;
                                break;
                        }
                    }
                    else if (baseType == typeof(Sprite))
                    {
                        switch (member.Name)
                        {
                            case nameof(Sprite.vertices):
                            case nameof(Sprite.triangles):
                            case nameof(Sprite.uv):
                                appendValueHashInstead = true;
                                break;
                        }
                    }
                    else if (baseType == typeof(Canvas))
                    {
                        switch (member.Name)
                        {
                            // FIXME: Should ideally not be excluded like this, but causes crashes sometimes (even with the crash fix patch)
                            //        Doesn't feel *super* important of a value, so for now just exclude it.
                            case nameof(Canvas.renderingDisplaySize):
                                continue;
                        }
                    }
                    else if (baseType == typeof(ItemDisplayRuleSet))
                    {
                        switch (member.Name)
                        {
                            case nameof(ItemDisplayRuleSet.keyAssetRuleGroups):
                                memberSerializationArgs.MaxCollectionCapacity = int.MaxValue;
                                break;
                        }
                    }
                    else if (baseType == typeof(WheelCollider))
                    {
                        switch (member.Name)
                        {
                            // Causes a crash when accessed for some reason, not a very important value for RoR2 specificially so I don't care to look into it
                            case nameof(WheelCollider.suspensionExpansionLimited):
                                continue;
                        }
                    }

                    if (ExcludeNonDeterministicValues)
                    {
                        if (baseType == typeof(ParticleSystem))
                        {
                            switch (member.Name)
                            {
                                case nameof(ParticleSystem.particleCount):
                                case nameof(ParticleSystem.time):
                                case nameof(ParticleSystem.randomSeed):
                                    continue;
                            }
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

                    if (memberType.IsArray)
                    {
                        Type elementType = memberType.GetElementType();
                        if (elementType == typeof(NodeGraph.Node) || elementType == typeof(NodeGraph.Link))
                        {
                            appendValueHashInstead = true;
                        }
                        else if (!appendValueHashInstead)
                        {
                            if (memberValue is Array array)
                            {
                                if (array.Length > 200)
                                {
                                    Log.Warning($"Large array with {array.Length} elements at {member.MemberType} {member.DeclaringType.FullName}.{member.Name}: {memberType.Name}), consider allowing hash?");
                                }
                            }
                        }
                    }

                    if (appendValueHashInstead)
                    {
                        byte[] hashBytes = _hashProvider.ComputeHash(memberValue);

                        builder.AddPropertyName(member.Name);
                        builder.AddValueRaw($"valuehash('{Convert.ToBase64String(hashBytes)}')");
                    }
                    else
                    {
                        if (!tryBuildPropertyWithValueWriteOperation(member.Name, memberValue, builder, memberSerializationArgs))
                        {
                            Log.Warning($"Failed to determine write operation for serialized {member.MemberType} {member.DeclaringType.FullName}.{member.Name}: {memberType.Name})");
                        }
                    }
                }
            }

            return true;
        }
    }
}
