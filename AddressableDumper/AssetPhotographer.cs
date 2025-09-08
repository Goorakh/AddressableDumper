#define IMAGE_ONLY

using AddressableDumper.Utils;
using AddressableDumper.Utils.Extensions;
using EntityStates;
using RoR2;
using RoR2.UI;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Rendering;

namespace AddressableDumper
{
    public static class AssetPhotographer
    {
        [ConCommand(commandName = "addressables_generate_portraits")]
        static void CCGenerateAssetImages(ConCommandArgs args)
        {
            Main.Instance.StartCoroutine(generatePortraits(args.TryGetArgBool(0) ?? false));
        }

        readonly struct AssetPathInfo
        {
            public readonly AssetInfo AssetInfo;

            public readonly string Directory;
            public readonly string Name;

            public AssetPathInfo(AssetInfo asset)
            {
                AssetInfo = asset;

                string key = asset.Key;

                string assetName;

                int lastSlashIndex = key.LastIndexOf('/');
                if (lastSlashIndex > 0)
                {
                    Directory = key.Substring(0, lastSlashIndex).FilterCharsFast(PathUtils.OrderedInvalidPathChars);
                    assetName = key.Remove(0, lastSlashIndex + 1);
                }
                else
                {
                    Directory = string.Empty;
                    assetName = key;
                }

                int assetNameDotIndex = assetName.LastIndexOf('.');
                if (assetNameDotIndex > 0)
                {
                    Name = assetName.Substring(0, assetNameDotIndex).FilterCharsFast(PathUtils.OrderedInvalidFileNameChars);
                }
                else
                {
                    Name = assetName;
                }
            }
        }

        static ModelPanel _modelPanel;

        static IEnumerator generatePortraits(bool forceRegenerate)
        {
            string basePath = System.IO.Path.Combine(Main.PersistentSaveDataDirectory, "export");

            _modelPanel = GameObject.Instantiate(LegacyResourcesAPI.Load<GameObject>("Prefabs/UI/IconGenerator")).GetComponentInChildren<ModelPanel>();

            // The default value of this has stolen days of my life, hopoo why
            _modelPanel.renderSettings = new RenderSettingsState
            {
                ambientIntensity = 1f,
                ambientLight = Color.white,
                ambientMode = AmbientMode.Flat,
                ambientGroundColor = Color.white
            };

            yield return new WaitForEndOfFrame();

            foreach (IGrouping<string, AssetPathInfo> assetsByPath in from asset in AddressablesIterator.GetAllAssetsFlattened()
                                                                      select new AssetPathInfo(asset) into assetPath
                                                                      group assetPath by assetPath.Directory)
            {
                string directoryPath = System.IO.Path.Combine(basePath, assetsByPath.Key);

                foreach (IGrouping<string, AssetPathInfo> assetsByName in from assetPath in assetsByPath
                                                                          group assetPath by assetPath.Name)
                {
                    AssetPathInfo[] assetPaths = assetsByName.ToArray();
                    bool appendType = assetPaths.Length > 1;

                    foreach (AssetPathInfo assetPathInfo in assetPaths)
                    {
                        DirectoryInfo directory = Directory.CreateDirectory(directoryPath);

                        string fileName = assetPathInfo.Name;
                        if (appendType)
                        {
                            fileName += $" ({assetPathInfo.AssetInfo.AssetType.Name})";
                        }

                        if (!forceRegenerate && directory.EnumerateFiles($"{fileName}.*").Any())
                        {
                            Log.Info($"Skipping already generated file for asset {assetPathInfo.AssetInfo.Key}");
                            continue;
                        }

                        string filePath = System.IO.Path.Combine(directoryPath, fileName);

                        Log.Info($"Generating portrait: {assetPathInfo.AssetInfo.Key}...");

                        yield return generatePortrait(assetPathInfo, filePath);
                    }
                }
            }

            GameObject.Destroy(_modelPanel.transform.root.gameObject);
            _modelPanel = null;

            Log.Info("Finished exporting assets");
        }

        static IEnumerator generatePortrait(AssetPathInfo assetPathInfo, string path)
        {
            return assetPathInfo.AssetInfo.Asset switch
            {
#if !IMAGE_ONLY
                ScriptableObject scriptableObject => exportScriptableObject(scriptableObject, path),
                Texture2D tex2D => exportTexture2D(tex2D, path),
                TextAsset textAsset => exportTextAsset(textAsset, path),
#endif
                GameObject gameObject => exportPrefab(gameObject, path),
                _ => unhandledAssetExportType(assetPathInfo, path),
            };
        }

        static IEnumerator unhandledAssetExportType(AssetPathInfo assetPathInfo, string path)
        {
#if !IMAGE_ONLY
            Log.Warning($"{path}: Unhandled asset type: {assetPathInfo.AssetInfo.AssetType.FullName}");
#endif
            yield break;
        }

        static IEnumerator exportScriptableObject(ScriptableObject asset, string path)
        {
            StringBuilder stringBuilder = new StringBuilder();

            Stack<MemberInfo> printedMembersChain = new Stack<MemberInfo>();

            Dictionary<Type, MemberInfo[]> printMembersCache = new Dictionary<Type, MemberInfo[]>();

            path += ".txt";
            using FileStream fileStream = File.Create(path);
            using StreamWriter fileWriter = new StreamWriter(fileStream);

            void appendValuesInType(object instance, Type declaringType, int indentation)
            {
                Type baseType = declaringType.BaseType;
                if (baseType != null)
                {
                    appendValuesInType(instance, baseType, indentation);
                }

                MemberInfo[] getPrintMembers()
                {
                    if (printMembersCache.TryGetValue(declaringType, out MemberInfo[] cachedPrintMembers))
                        return cachedPrintMembers;

                    List<MemberInfo> printMembers = new List<MemberInfo>();

                    foreach (FieldInfo field in declaringType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    {
                        if (!field.IsPublic && field.GetCustomAttribute<SerializeField>() == null)
                            continue;

                        if (field.GetCustomAttribute<NonSerializedAttribute>() != null)
                            continue;

                        printMembers.Add(field);
                    }

                    foreach (PropertyInfo property in declaringType.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    {
                        if (property.GetMethod == null || !property.GetMethod.IsPublic || property.SetMethod == null || !property.SetMethod.IsPublic)
                            continue;

                        printMembers.Add(property);
                    }

                    MemberInfo[] printMembersArray = printMembers.ToArray();
                    printMembersCache.Add(declaringType, printMembersArray);
                    return printMembersArray;
                }

                foreach (MemberInfo member in getPrintMembers())
                {
                    Type exposedType;
                    object memberValue;
                    string customDisplay = null;
                    switch (member)
                    {
                        case FieldInfo field:
                            exposedType = field.FieldType;
                            memberValue = field.GetValue(instance);
                            break;
                        case PropertyInfo property:
                            exposedType = property.PropertyType;

                            try
                            {
                                memberValue = property.GetValue(instance);
                            }
                            catch (Exception e)
                            {
                                memberValue = null;
                                customDisplay = $"[ Exception: {e.Message} ]";
                            }

                            break;
                        default:
                            Log.Warning($"Unhandled member type {member.MemberType}");
                            continue;
                    }

                    stringBuilder.Append('\t', indentation)
                                 .Append(member.Name)
                                 .Append(" (")
                                 .Append(exposedType.FullName)
                                 .Append("): ");

                    if (printedMembersChain.Contains(member))
                    {
                        stringBuilder.Append("[ Recursive Reference ]");
                    }
                    else
                    {
                        printedMembersChain.Push(member);

                        if (customDisplay != null)
                        {
                            stringBuilder.Append(customDisplay);
                        }
                        else
                        {
                            appendValue(memberValue, indentation);
                        }

                        printedMembersChain.Pop();
                    }

                    stringBuilder.AppendLine();
                }
            }

            void appendValue(object value, int indentation)
            {
                if (value is null)
                {
                    stringBuilder.Append("null");
                    return;
                }

                switch (value)
                {
                    case SerializableEntityStateType serializableEntityStateType:
                        stringBuilder.Append(serializableEntityStateType.typeName);
                        return;
                    case MemberInfo memberReference:
                        stringBuilder.Append(memberReference.Name);
                        return;
                    case string str:
                        stringBuilder.Append("\"").Append(str).Append("\"");
                        return;
                    case ICollection collection:
                        stringBuilder.Append('[').Append(collection.Count).Append(']');
                        if (collection.Count > 0)
                        {
                            stringBuilder.AppendLine(":").Append('\t', indentation).Append('{').AppendLine();

                            foreach (object item in collection)
                            {
                                stringBuilder.Append('\t', indentation + 1);
                                appendValue(item, indentation + 1);
                                stringBuilder.AppendLine(",");
                            }

                            stringBuilder.Append('\t', indentation).Append('}');

                            fileWriter.Write(stringBuilder.Take());
                        }

                        return;
                }

                Type type = value.GetType();

                if (type.IsPrimitive || type.IsEnum)
                {
                    stringBuilder.Append(value);
                    return;
                }

                if (type.IsSerializable || ReferenceEquals(value, asset))
                {
                    stringBuilder.Append('{').AppendLine();
                    appendValuesInType(value, type, indentation + 1);
                    stringBuilder.Append('\t', indentation).Append('}');

                    fileWriter.Write(stringBuilder.Take());
                }
                else
                {
                    stringBuilder.Append(value);
                }
            }

            stringBuilder.Append('(').Append(asset.GetType().FullName).AppendLine("):");
            appendValue(asset, 0);

            Log.Info($"Created file '{path}' for {asset}");

            yield break;
        }

        static IEnumerator exportTexture2D(Texture2D asset, string path)
        {
            using TemporaryTexture readable = asset.AsReadable();

            byte[] pngBytes;
            try
            {
                pngBytes = readable.Texture.EncodeToPNG();
            }
            catch (Exception e)
            {
                Log.Warning($"Unable to export {asset}: {e}");
                yield break;
            }

            if (pngBytes is null || pngBytes.Length == 0)
            {
                Log.Warning($"Unable to export {asset}");
                yield break;
            }

            path += ".png";
            File.WriteAllBytes(path, pngBytes);

            Log.Info($"Created file '{path}' for {asset}");

            yield break;
        }

        static IEnumerator exportTextAsset(TextAsset asset, string path)
        {
            path += ".txt";
            File.WriteAllText(path, asset.text);

            Log.Info($"Created file '{path}' for {asset}");

            yield break;
        }

        static IEnumerator exportPrefab(GameObject asset, string path)
        {
            if (asset.GetComponentsInChildren<Renderer>().Length > 0)
            {
                return exportPrefabImage(asset, path);
            }
            else
            {
#if !IMAGE_ONLY
                return exportPrefabValues(asset, path);
#else
                IEnumerator empty()
                {
                    yield break;
                }

                return empty();
#endif
            }
        }

        static IEnumerator exportPrefabImage(GameObject asset, string path)
        {
            _modelPanel.modelPrefab = asset;

            bool isStaticModel = asset.GetComponentsInChildren<Component>().All(c =>
            {
                switch (c)
                {
                    case Transform:
                    case NetworkTransform:
                    case MeshRenderer:
                    case SkinnedMeshRenderer:
                    case BillboardRenderer:
                    case LineRenderer:
                    case TrailRenderer:
                    case MeshFilter:
                    case Collider:
                    case SurfaceDefProvider:
                    case NetworkIdentity:
                    case ModelPanelParameters:
                    case LODGroup:
                    case AkGameObj:
                    case ItemDisplay:
                    case Tree:
                    case PaintDetailsBelow:
                    case Light:
                    case DestroyOnDestroy:
                    case DestroyOnKill:
                    case DestroyOnParticleEnd:
                    case DestroyOnSoundEnd:
                    case DestroyOnTimer:
                    case DestroyOnUnseen:
                    case SetDontDestroyOnLoad:
                    case ChildLocator:
                    case DetachParticleOnDestroyAndEndEmission:
                    case EntityLocator:
                    case NonSolidToCamera:
                        return true;
                    default:
                        Log.Debug($"{asset} is not static model: has component {c}");
                        return false;
                }
            });

            if (!isStaticModel)
            {
                float initialWaitTime = 0.5f;

                if (asset.TryGetComponent(out PrintController printController))
                {
                    initialWaitTime = Mathf.Max(initialWaitTime, printController.printTime + 1f);
                }

                if (asset.TryGetComponent(out TemporaryOverlay temporaryOverlay))
                {
                    initialWaitTime = Mathf.Max(initialWaitTime, temporaryOverlay.duration + 1f);
                }

                if (asset.TryGetComponent(out DestroyOnTimer destroyOnTimer))
                {
                    destroyOnTimer.enabled = false;
                }

                if (asset.TryGetComponent(out EffectComponent effectComponent))
                {
                    bool tryEstimateEffectDuration(out float estimatedDuration)
                    {
                        ParticleSystem[] particleSystems = asset.GetComponentsInChildren<ParticleSystem>();
                        if (particleSystems.Length > 0)
                        {
                            ParticleSystem[] nonLoopingParticleSystems = particleSystems.Where(p => !p.main.loop).ToArray();
                            if (nonLoopingParticleSystems.Length > 0)
                            {
                                estimatedDuration = nonLoopingParticleSystems.Select(p =>
                                {
                                    float maxStartDelay = p.main.startDelay.Evaluate(0f, 1f) * p.main.startDelayMultiplier;
                                    float maxStartLifetime = p.main.startLifetime.Evaluate(0f, 1f) * p.main.startLifetimeMultiplier;

                                    return maxStartDelay + maxStartLifetime;
                                }).Average();

                                return true;
                            }

                            // If all particles are looping, it won't really matter when the picture is taken, so just use whatever value would already be used
                        }

                        if (destroyOnTimer)
                        {
                            estimatedDuration = destroyOnTimer.duration;
                            return true;
                        }

                        estimatedDuration = float.NaN;
                        return false;
                    }

                    if (tryEstimateEffectDuration(out float estimatedEffectDuration))
                    {
                        initialWaitTime = estimatedEffectDuration * 0.4f;
                    }
                }

                Log.Debug($"Waiting {initialWaitTime} seconds for model to settle");

                yield return new WaitForSeconds(initialWaitTime);
            }
            
            // Seems to cause a crash if the target object has no visible renderers (bounds can't be calculated)
            //_modelPanel.SetAnglesForCharacterThumbnail(true);
            
            yield return new WaitForEndOfFrame();

            int width = _modelPanel.renderTexture.width;
            int height = _modelPanel.renderTexture.height;
            using TemporaryTexture portrait = new TemporaryTexture(new Texture2D(width, height, textureFormat, false, linear), true);
            portrait.Texture.name = asset.name + "_Portrait";
            
            RenderTexture active = RenderTexture.active;
            RenderTexture.active = _modelPanel.renderTexture;
            
            portrait.Texture.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
            portrait.Texture.Apply(false, false);

            RenderTexture.active = active;
            
            yield return exportTexture2D(portrait.Texture, path);

            _modelPanel.modelPrefab = null;
        }

        static TextureFormat textureFormat = TextureFormat.ARGB32;
        static bool linear = false;

        static IEnumerator exportPrefabValues(GameObject asset, string path)
        {
            Log.Info($"Not implemented :]");
            yield break;
        }
    }
}
