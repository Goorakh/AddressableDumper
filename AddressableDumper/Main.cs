using BepInEx;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
        public const string PluginVersion = "1.1.1";

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

                    Log.Debug($"Created persistent save data directory: {_persistentSaveDataDirectory}");
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
    }
}
