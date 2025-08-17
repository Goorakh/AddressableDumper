using AddressableDumper.ValueDumper.Serialization;
using Newtonsoft.Json;
using RoR2;
using System;
using System.IO;
using System.Linq;
using System.Text;

using Path = System.IO.Path;

namespace AddressableDumper.ValueDumper
{
    static class AddressablesDetailDumper
    {
        static readonly string _addressablesDumpPath = System.IO.Path.Combine(Main.PersistentSaveDataDirectory, "values_dump");

        [ConCommand(commandName = "dump_addressable_values")]
        static void CCDumpAddressableValues(ConCommandArgs args)
        {
            if (Directory.Exists(_addressablesDumpPath))
            {
                Directory.Delete(_addressablesDumpPath, true);
            }

            AssetInfo[] assetInfos = AddressablesIterator.GetAllAssets();

            foreach (AssetInfo assetInfo in assetInfos)
            {
                FilePath dumpFilePath = $"{Path.Combine(_addressablesDumpPath, $"{assetInfo.Key} ({assetInfo.AssetType.Name})")}.txt";
                FilePath originalFilePath = dumpFilePath;

                Directory.CreateDirectory(dumpFilePath.DirectoryPath);

                dumpFilePath.MakeUnique();

                Log.Info($"Dumping asset values of '{assetInfo.Key}'");

                using (FileStream fileStream = File.Open(dumpFilePath, FileMode.CreateNew, FileAccess.Write))
                {
                    using (StreamWriter fileWriter = new StreamWriter(fileStream, Encoding.UTF8, 1024, true))
                    {
                        fileWriter.WriteLine($"// Key: {assetInfo.Key}");
                        fileWriter.WriteLine($"// Asset Type: {assetInfo.AssetType.FullName}");
                        fileWriter.WriteLine();

                        using JsonTextWriter jsonWriter = new JsonTextWriter(fileWriter)
                        {
                            Formatting = Formatting.Indented,
                            CloseOutput = false,
                            AutoCompleteOnClose = false,
                        };

                        ObjectSerializer serializer = new ObjectSerializer(jsonWriter, assetInfo.Asset);
                        serializer.Write();
                    }
                }

                FilePath[] duplicateFiles = originalFilePath.GetAllExistingDuplicateFileNames().ToArray();
                if (duplicateFiles.Length > 1)
                {
                    string[] fileContents = new string[duplicateFiles.Length];
                    for (int i = 0; i < duplicateFiles.Length; i++)
                    {
                        fileContents[i] = File.ReadAllText(duplicateFiles[i]);
                    }

                    Array.Sort(fileContents, StringComparer.Ordinal);

                    for (int i = 0; i < duplicateFiles.Length; i++)
                    {
                        File.WriteAllText(duplicateFiles[i], fileContents[i]);
                    }
                }
            }
        }
    }
}
