using BepInEx;
using System.Diagnostics;
using System.IO;
using UnityEngine;

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
    }
}
