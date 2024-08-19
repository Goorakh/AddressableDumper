using AddressableDumper.ValueDumper.Serialization;
using Newtonsoft.Json;
using RoR2;
using System.IO;
using System.Text;

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
                FilePath dumpFilePath = new FilePath(System.IO.Path.Combine(_addressablesDumpPath, assetInfo.Key) + ".txt");

                Directory.CreateDirectory(dumpFilePath.DirectoryName);

                if (dumpFilePath.Exists)
                {
                    dumpFilePath.FileNameWithoutExtension += $" ({assetInfo.AssetType.Name})";

                    if (dumpFilePath.Exists)
                    {
                        FilePath numberedFilePath;

                        int fileNumber = 1;
                        do
                        {
                            numberedFilePath = dumpFilePath;
                            numberedFilePath.FileNameWithoutExtension += $" ({fileNumber})";

                            fileNumber++;
                        } while (numberedFilePath.Exists);

                        dumpFilePath = numberedFilePath;
                    }
                }

                Log.Info($"Dumping asset values of '{assetInfo.Key}'");

                using (FileStream fileStream = File.Open(dumpFilePath.FullPath, FileMode.CreateNew, FileAccess.Write))
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
            }
        }
    }
}
