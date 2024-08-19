using AddressableDumper.ValueDumper.Serialization;
using BepInEx;
using Newtonsoft.Json;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.ResourceLocations;

namespace AddressableDumper
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    public class Main : BaseUnityPlugin
    {
        public const string PluginGUID = PluginAuthor + "." + PluginName;
        public const string PluginAuthor = "Gorakh";
        public const string PluginName = "AddressableDumper";
        public const string PluginVersion = "1.0.0";

        internal static Main Instance { get; private set; }

        internal static string PluginPath { get; private set; }

        static readonly string _persistentSaveDataDirectory = Path.Combine(Application.persistentDataPath, PluginName);
        public static string PersistentSaveDataDirectory
        {
            get
            {
                if (!Directory.Exists(_persistentSaveDataDirectory))
                {
                    Directory.CreateDirectory(_persistentSaveDataDirectory);

#if DEBUG
                    Log.Debug($"Created persistent save data directory: {_persistentSaveDataDirectory}");
#endif
                }

                return _persistentSaveDataDirectory;
            }
        }

        void Awake()
        {
            Stopwatch stopwatch = Stopwatch.StartNew();

            Log.Init(Logger);

            Instance = SingletonHelper.Assign(Instance, this);

            PluginPath = Path.GetDirectoryName(Info.Location);

            stopwatch.Stop();
            Log.Info_NoCallerPrefix($"Initialized in {stopwatch.Elapsed.TotalSeconds:F2} seconds");
        }

        void OnDestroy()
        {
            Instance = SingletonHelper.Unassign(Instance, this);
        }

        public T LoadAsset<T>(string key) where T : UnityEngine.Object
        {
            return Addressables.LoadAssetAsync<T>(key).WaitForCompletion();
        }

        public T LoadAsset<T>(IResourceLocation location) where T : UnityEngine.Object
        {
            return Addressables.LoadAssetAsync<T>(location).WaitForCompletion();
        }

        public IList<IResourceLocation> GetResourceLocations(string[] keys, Addressables.MergeMode mergeMode)
        {
            return Addressables.LoadResourceLocationsAsync((IEnumerable)keys, mergeMode).WaitForCompletion();
        }

        /*
        public void WriterTest()
        {
            using MemoryStream ms = new MemoryStream();
            using StreamWriter writer = new StreamWriter(ms, Encoding.UTF8, 2048, true);
            using JsonWriter jsonWriter = new JsonTextWriter(writer)
            {
                Formatting = Formatting.Indented,
                CloseOutput = false,
                AutoCompleteOnClose = false,
            };

            try
            {
                WriteOperationBuilder builder = new WriteOperationBuilder(new JsonWriteOperationBuilderWriter(jsonWriter))
                {
                    AutoFlushCapacity = int.MaxValue
                };

                WriteOperationBuilder splitBuilder = new WriteOperationBuilder(null)
                {
                    AutoFlushCapacity = int.MaxValue
                };

                void writeRandomObject(float elementChance, WriteOperationBuilder splitBuilder)
                {
                    bool startedObject = false;

                    Xoroshiro128Plus rng = RoR2.RoR2Application.rng;

                    splitBuilder = new WriteOperationBuilder(splitBuilder);

                    void add(WriteOperation operation)
                    {
                        builder.Add(operation.Clone());
                        splitBuilder.Add(operation.Clone());
                    }

                    while (rng.nextNormalizedFloat < elementChance)
                    {
                        elementChance *= 0.75f;

                        if (!startedObject)
                        {
                            startedObject = true;
                            add(WriteOperation.StartObject());
                        }

                        int propId = rng.RangeInt(1, 1025);
                        add(WriteOperation.PropertyName($"prop_{propId:X}"));

                        switch (rng.RangeInt(0, 2))
                        {
                            case 0:
                                int value = rng.RangeInt(1, 1025);

                                if (rng.nextNormalizedFloat < 0.3f)
                                {
                                    writeRandomObject(elementChance, splitBuilder);
                                }
                                else
                                {
                                    add(WriteOperation.ValueRaw(value.ToString()));
                                }

                                break;
                            case 1:
                                add(WriteOperation.StartArray());

                                int arrayCount = rng.RangeInt(0, 6);
                                for (int i = 0; i < arrayCount; i++)
                                {
                                    writeRandomObject(elementChance, splitBuilder);
                                }

                                add(WriteOperation.EndArray());
                                break;
                        }
                    }

                    if (startedObject)
                    {
                        add(WriteOperation.EndObject());
                    }
                    else
                    {
                        add(WriteOperation.Null());
                    }

                    splitBuilder.Flush();
                }
                
                float elementChance = 1f;
                writeRandomObject(elementChance, splitBuilder);

                Log.Info($"builder:\n{string.Join("\n", builder)}");

                Log.Info($"split builder:\n{string.Join("\n", splitBuilder)}");

                builder.Flush();
            }
            finally
            {
                jsonWriter.Flush();

                ms.Position = 0;
                using StreamReader reader = new StreamReader(ms, Encoding.UTF8, false, 2048, true);
                Log.Info("\n" + reader.ReadToEnd());
            }
        }
        */
    }
}
