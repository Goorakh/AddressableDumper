using AddressableDumper.Utils.Extensions;
using AddressableDumper.ValueDumper.Serialization;
using HarmonyLib;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;
using Newtonsoft.Json;
using RoR2;
using RoR2.Networking;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.SceneManagement;

using Path = System.IO.Path;

namespace AddressableDumper.ValueDumper
{
    static class SceneDumper
    {
        static readonly string _scenesDumpPath = Path.Combine(Main.PersistentSaveDataDirectory, "scenes_dump");

        static ScenesDumperOperation _currentScenesDump;

        [ConCommand(commandName = "dump_scenes")]
        static void CCDumpScenes(ConCommandArgs args)
        {
            if (_currentScenesDump == null || _currentScenesDump.IsDisposed)
            {
                _currentScenesDump = new ScenesDumperOperation();
                _currentScenesDump.Start();
            }
        }

        static void preventExecution(ILContext il)
        {
            ILCursor c = new ILCursor(il);

            static IEnumerable getDefaultValueEnumerable()
            {
                return Enumerable.Empty<object>();
            }

            static IEnumerator getDefaultValueEnumerator()
            {
                return getDefaultValueEnumerable().GetEnumerator();
            }

            TypeReference returnType = il.Method.ReturnType;

            Delegate getDefaultValueMethod = null;
            if (returnType.Is(typeof(IEnumerable)))
            {
                getDefaultValueMethod = getDefaultValueEnumerable;
            }
            else if (returnType.Is(typeof(IEnumerator)))
            {
                getDefaultValueMethod = getDefaultValueEnumerator;
            }
            else if (!returnType.Is(typeof(void)))
            {
                Log.Error($"Unhandled return type {returnType.FullName}");
                c.Emit(OpCodes.Ldnull);
            }

            if (getDefaultValueMethod != null)
            {
                c.EmitDelegate(getDefaultValueMethod);
            }

            c.Emit(OpCodes.Ret);
        }

        static void preventDontDestroyOnLoad(UnityEngine.Object target)
        {
        }

        delegate void orig_SceneManager_Internal_ActiveSceneChanged(Scene previousActiveScene, Scene newActiveScene);
        static void SceneManager_Internal_ActiveSceneChanged(orig_SceneManager_Internal_ActiveSceneChanged orig, Scene previousActiveScene, Scene newActiveScene)
        {
            UnityAction<Scene, Scene> activeSceneChanged = typeof(SceneManager).GetField(nameof(SceneManager.activeSceneChanged), BindingFlags.Static | BindingFlags.NonPublic).GetValue(null) as UnityAction<Scene, Scene>;
            if (activeSceneChanged != null)
            {
                foreach (UnityAction<Scene, Scene> activeSceneChangedInvoke in activeSceneChanged.GetInvocationList().Cast<UnityAction<Scene, Scene>>())
                {
                    try
                    {
                        activeSceneChangedInvoke(previousActiveScene, newActiveScene);
                    }
                    catch (Exception e)
                    {
                        Log.Error_NoCallerPrefix(e);
                    }
                }
            }
        }

        class ScenesDumperOperation : IDisposable
        {
            IEnumerator<SceneInfo> _sceneInfoIterator;

            readonly List<IDetour> _temporaryDetours = [];

            public bool IsDisposed { get; private set; }

            public void Start()
            {
                if (Directory.Exists(_scenesDumpPath))
                {
                    Directory.Delete(_scenesDumpPath, true);
                }

                SceneManager.activeSceneChanged += SceneManager_activeSceneChanged;

                _temporaryDetours.Add(new ILHook(SymbolExtensions.GetMethodInfo<NetworkManager>(_ => _.ServerChangeScene(default)), preventExecution));

                _temporaryDetours.Add(new NativeDetour(SymbolExtensions.GetMethodInfo(() => GameObject.DontDestroyOnLoad(default)), SymbolExtensions.GetMethodInfo(() => preventDontDestroyOnLoad(default))));

                _temporaryDetours.Add(new Hook(typeof(SceneManager).GetMethod("Internal_ActiveSceneChanged", BindingFlags.NonPublic |
                    BindingFlags.Static), SceneManager_Internal_ActiveSceneChanged));

                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    foreach (Type type in assembly.GetTypes())
                    {
                        if (type.IsGenericType || !type.IsClass)
                            continue;

                        if (typeof(MonoBehaviour).IsAssignableFrom(type))
                        {
                            const BindingFlags FLAGS = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

                            void tryAddPreventHook(string methodName)
                            {
                                MethodInfo method = type.GetMethod(methodName, FLAGS);
                                if (method != null && method.HasMethodBody())
                                {
                                    Log.Debug($"Adding prevent execution hook for {method.DeclaringType.FullName}.{method.Name}");
                                    _temporaryDetours.Add(new ILHook(method, preventExecution));
                                }
                            }

                            tryAddPreventHook("Awake");
                            tryAddPreventHook("OnEnable");
                        }
                    }
                }

                _sceneInfoIterator = AddressablesIterator.GetSceneResourceLocations()
                                                         .Select(location => new SceneInfo(location))
                                                         .GetEnumerator();

                tryMoveToNextScene();
            }

            public void Dispose()
            {
                if (IsDisposed)
                    return;

                IsDisposed = true;

                SceneManager.activeSceneChanged -= SceneManager_activeSceneChanged;

                foreach (IDetour detour in _temporaryDetours)
                {
                    detour?.Dispose();
                }

                _temporaryDetours.Clear();
            }

            void SceneManager_activeSceneChanged(Scene prevScene, Scene newScene)
            {
                if (IsDisposed)
                    return;

                _sceneInfoIterator.Current.DumpToFile(newScene);
                tryMoveToNextScene();
            }

            void tryMoveToNextScene()
            {
                if (IsDisposed)
                    return;

                if (_sceneInfoIterator.MoveNext())
                {
                    new NetworkManagerSystem.AddressablesChangeSceneAsyncOperation(_sceneInfoIterator.Current.ResourceLocation.PrimaryKey, LoadSceneMode.Single, true, false);
                }
                else
                {
                    Log.Info("Finished dumping scenes");

                    Dispose();

                    NetworkManager.singleton.ServerChangeScene("title");
                }
            }
        }

        class SceneInfo
        {
            public readonly IResourceLocation ResourceLocation;

            readonly string _sceneDumpDirectory;

            public SceneInfo(IResourceLocation resourceLocation)
            {
                ResourceLocation = resourceLocation;

                _sceneDumpDirectory = Path.Combine(_scenesDumpPath, ResourceLocation.PrimaryKey);
            }

            public void DumpToFile(Scene sceneInstance)
            {
                if (Directory.Exists(_sceneDumpDirectory))
                {
                    Log.Warning($"Existing scene dump at '{_sceneDumpDirectory}', existing files will be replaced");
                    Directory.Delete(_sceneDumpDirectory, true);
                }

                Directory.CreateDirectory(_sceneDumpDirectory);

                Log.Info($"Dumping scene '{ResourceLocation.PrimaryKey}'");

                // Split each root object into its own dump file to keep filesizes down

                FilePath sceneInfoFilePath = Path.Combine(_sceneDumpDirectory, "scene.txt");
                using (FileStream sceneInfoFile = File.Open(sceneInfoFilePath, FileMode.CreateNew, FileAccess.Write))
                {
                    using (StreamWriter fileWriter = new StreamWriter(sceneInfoFile, Encoding.UTF8, 1024, true))
                    {
                        fileWriter.WriteLine($"// Key: {ResourceLocation.PrimaryKey}");
                        fileWriter.WriteLine();

                        using JsonTextWriter jsonWriter = new JsonTextWriter(fileWriter)
                        {
                            Formatting = Formatting.Indented,
                            CloseOutput = false,
                            AutoCompleteOnClose = false,
                        };

                        jsonWriter.WriteStartObject();

                        jsonWriter.WritePropertyName("sceneName");
                        jsonWriter.WriteValue(sceneInstance.name);

                        jsonWriter.WritePropertyName("path");
                        jsonWriter.WriteValue(sceneInstance.path);

                        jsonWriter.WritePropertyName("buildIndex");
                        jsonWriter.WriteValue(sceneInstance.buildIndex);

                        jsonWriter.WritePropertyName("rootObjectCount");
                        jsonWriter.WriteValue(sceneInstance.rootCount);

                        jsonWriter.WriteEndObject();
                    }
                }

                string rootObjectsDirectory = Path.Combine(_sceneDumpDirectory, "RootObjects");

                List<GameObject> rootObjects = new List<GameObject>(sceneInstance.rootCount);
                sceneInstance.GetRootGameObjects(rootObjects);

                serializeRootObjects(sceneInstance, rootObjects, rootObjectsDirectory);

                static void serializeRootObjects(Scene sceneInstance, IList<GameObject> rootObjects, string rootFilePath)
                {
                    Directory.CreateDirectory(rootFilePath);

                    HashSet<Transform> rootTransformsSet = [];
                    foreach (GameObject rootObject in rootObjects)
                    {
                        rootTransformsSet.Add(rootObject.transform.root);
                    }

                    Transform[] rootTransforms = [.. rootTransformsSet];

                    for (int i = 0; i < rootObjects.Count; i++)
                    {
                        bool requiresSplit = false;

                        GameObject rootObject = rootObjects[i];
                        string fileName = rootObject.name.FilterChars(Path.GetInvalidFileNameChars());

                        FilePath objectDumpPath = Path.Combine(rootFilePath, $"{i} -- {fileName}.txt");
                        objectDumpPath.MakeUnique();

                        if (!requiresSplit)
                        {
                            Log.Info($"Dumping '{sceneInstance.name}' root object '{rootObject.name}' to {objectDumpPath.FullPath}");

                            using (FileStream fileStream = File.Open(objectDumpPath, FileMode.CreateNew, FileAccess.Write))
                            {
                                using (StreamWriter fileWriter = new StreamWriter(fileStream, Encoding.UTF8, 1024, true))
                                {
                                    fileWriter.WriteLine($"// Scene: '{sceneInstance.path}'");
                                    fileWriter.WriteLine();

                                    using JsonTextWriter jsonWriter = new JsonTextWriter(fileWriter)
                                    {
                                        Formatting = Formatting.Indented,
                                        CloseOutput = false,
                                        AutoCompleteOnClose = false,
                                    };

                                    ObjectSerializer serializer = new ObjectSerializer(jsonWriter, rootObject)
                                    {
                                        SerializingScene = sceneInstance,
                                        AdditionalReferenceRoots = rootTransforms,
                                        ExcludeNonDeterministicValues = true
                                    };

                                    serializer.Write();

                                    const long KB_IN_BYTES = 1000;
                                    const long MB_IN_BYTES = 1000 * KB_IN_BYTES;

                                    if (fileStream.Length > 99 * MB_IN_BYTES)
                                    {
                                        Log.Warning($"Splitting large scene file at '{objectDumpPath}'");
                                        requiresSplit = true;
                                    }
                                }
                            }

                            if (requiresSplit)
                            {
                                File.Delete(objectDumpPath);
                            }
                        }

                        if (requiresSplit)
                        {
                            GameObject[] childObjects = new GameObject[rootObject.transform.childCount];
                            for (int j = 0; j < rootObject.transform.childCount; j++)
                            {
                                childObjects[j] = rootObject.transform.GetChild(j).gameObject;
                            }

                            using (FileStream fileStream = File.Open(objectDumpPath, FileMode.CreateNew, FileAccess.Write))
                            {
                                using (StreamWriter fileWriter = new StreamWriter(fileStream, Encoding.UTF8, 1024, true))
                                {
                                    fileWriter.WriteLine($"// Scene: '{sceneInstance.path}'");
                                    fileWriter.WriteLine();

                                    using JsonTextWriter jsonWriter = new JsonTextWriter(fileWriter)
                                    {
                                        Formatting = Formatting.Indented,
                                        CloseOutput = false,
                                        AutoCompleteOnClose = false,
                                    };

                                    ObjectSerializer serializer = new ObjectSerializer(jsonWriter, rootObject)
                                    {
                                        SerializingScene = sceneInstance,
                                        AdditionalReferenceRoots = rootTransforms,
                                        ExcludeNonDeterministicValues = true,
                                        SerializeChildren = false
                                    };

                                    serializer.Write();
                                }
                            }

                            serializeRootObjects(sceneInstance, childObjects, Path.Combine(objectDumpPath.DirectoryPath, objectDumpPath.FileNameWithoutExtension));
                        }
                    }
                }
            }
        }
    }
}
