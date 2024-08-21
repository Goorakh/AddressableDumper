using AddressableDumper.Utils.Extensions;
using AddressableDumper.ValueDumper.Serialization;
using HarmonyLib;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using Newtonsoft.Json;
using RoR2;
using RoR2.Networking;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.SceneManagement;

namespace AddressableDumper.ValueDumper
{
    static class SceneDumper
    {
        static readonly string _scenesDumpPath = System.IO.Path.Combine(Main.PersistentSaveDataDirectory, "scenes_dump");

        static ScenesDumperOperation _currentScenesDump;

        [ConCommand(commandName = "dump_scenes")]
        static void CCDumpScenes(ConCommandArgs args)
        {
            if (_currentScenesDump != null)
                return;

            _currentScenesDump = new ScenesDumperOperation();
            _currentScenesDump.Start();
        }

        static void RoachControllerPreventSpawn(On.RoR2.RoachController.orig_Awake orig, RoachController self)
        {
        }

        static void preventSceneLoad(ILContext il)
        {
            ILCursor c = new ILCursor(il);

            c.Emit(OpCodes.Ret);
        }

        class ScenesDumperOperation : IDisposable
        {
            IEnumerator<SceneInfo> _sceneInfoIterator;

            ILHook _changeSceneHook;

            public void Dispose()
            {
                SceneManager.activeSceneChanged -= SceneManager_activeSceneChanged;

                On.RoR2.RoachController.Awake -= RoachControllerPreventSpawn;
                _changeSceneHook?.Dispose();
                _changeSceneHook = null;

                _sceneInfoIterator?.Dispose();
                _sceneInfoIterator = null;
            }

            public void Start()
            {
                if (Directory.Exists(_scenesDumpPath))
                {
                    Directory.Delete(_scenesDumpPath, true);
                }

                SceneManager.activeSceneChanged += SceneManager_activeSceneChanged;

                On.RoR2.RoachController.Awake += RoachControllerPreventSpawn;
                _changeSceneHook = new ILHook(SymbolExtensions.GetMethodInfo<NetworkManager>(_ => _.ServerChangeScene(default)), preventSceneLoad);

                _sceneInfoIterator = AddressablesIterator.GetSceneResourceLocations()
                                                         .Select(location => new SceneInfo(location))
                                                         .GetEnumerator();

                tryMoveToNextScene();
            }

            void SceneManager_activeSceneChanged(Scene prevScene, Scene newScene)
            {
                _sceneInfoIterator.Current.DumpToFile(newScene);
                tryMoveToNextScene();
            }

            void tryMoveToNextScene()
            {
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

                _sceneDumpDirectory = System.IO.Path.Combine(_scenesDumpPath, ResourceLocation.PrimaryKey);
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

                FilePath sceneInfoFilePath = System.IO.Path.Combine(_sceneDumpDirectory, "scene.txt");
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

                string rootObjectsDirectory = System.IO.Path.Combine(_sceneDumpDirectory, "RootObjects");
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
                    string fileName = rootObject.name.FilterChars(System.IO.Path.GetInvalidFileNameChars());

                    FilePath objectDumpPath = System.IO.Path.Combine(rootObjectsDirectory, fileName + ".txt");
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
                                AdditionalReferenceRoots = rootTransforms
                            };

                            serializer.Write();
                        }
                    }
                }
            }
        }
    }
}
