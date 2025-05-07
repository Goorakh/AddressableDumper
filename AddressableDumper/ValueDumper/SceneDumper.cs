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

                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    foreach (Type type in assembly.GetTypes())
                    {
                        if (type.IsGenericType)
                            continue;

                        if (typeof(MonoBehaviour).IsAssignableFrom(type))
                        {
                            const BindingFlags FLAGS = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

                            MethodInfo awakeMethod = type.GetMethod("Awake", FLAGS);
                            if (awakeMethod != null)
                            {
                                _temporaryDetours.Add(new ILHook(awakeMethod, preventExecution));
                            }

                            MethodInfo onEnableMethod = type.GetMethod("OnEnable", FLAGS);
                            if (onEnableMethod != null)
                            {
                                _temporaryDetours.Add(new ILHook(onEnableMethod, preventExecution));
                            }
                        }
                    }
                }

                _temporaryDetours.Add(new ILHook(SymbolExtensions.GetMethodInfo<NetworkManager>(_ => _.ServerChangeScene(default)), preventExecution));

                _temporaryDetours.Add(new NativeDetour(SymbolExtensions.GetMethodInfo(() => GameObject.DontDestroyOnLoad(default)), SymbolExtensions.GetMethodInfo(() => preventDontDestroyOnLoad(default))));

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
                    new NetworkManagerSystem.AddressablesChangeSceneAsyncOperation(_sceneInfoIterator.Current.ResourceLocation.PrimaryKey, LoadSceneMode.Single, true);
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
                Directory.CreateDirectory(rootObjectsDirectory);

                List<GameObject> rootObjects = new List<GameObject>(sceneInstance.rootCount);
                sceneInstance.GetRootGameObjects(rootObjects);

                HashSet<Transform> rootTransformsSet = [];
                foreach (GameObject rootObject in rootObjects)
                {
                    rootTransformsSet.Add(rootObject.transform.root);
                }

                Transform[] rootTransforms = rootTransformsSet.ToArray();

                for (int i = 0; i < rootObjects.Count; i++)
                {
                    GameObject rootObject = rootObjects[i];
                    string fileName = rootObject.name.FilterChars(Path.GetInvalidFileNameChars());

                    FilePath objectDumpPath = Path.Combine(rootObjectsDirectory, fileName + ".txt");
                    if (objectDumpPath.Exists)
                    {
                        objectDumpPath.FileNameWithoutExtension += $" (obj index {i})";
                    }

                    objectDumpPath.MakeUnique();

                    Log.Info($"Dumping '{sceneInstance.name}' root object '{rootObject.name}' to {objectDumpPath.FullPath}");

                    using (FileStream fileStream = File.Open(objectDumpPath, FileMode.CreateNew, FileAccess.Write))
                    {
                        using (StreamWriter fileWriter = new StreamWriter(fileStream, Encoding.UTF8, 1024, true))
                        {
                            fileWriter.WriteLine($"// Scene: '{sceneInstance.path}'");
                            fileWriter.WriteLine($"// Root Object Index: {i}");
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
                        }
                    }
                }
            }
        }
    }
}
