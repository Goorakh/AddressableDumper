using AddressableDumper.Utils;
using AddressableDumper.Utils.Extensions;
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
        static readonly string _addressablesDumpPath = Path.Combine(Main.PersistentSaveDataDirectory, "values_dump");

        [ConCommand(commandName = "dump_addressable_values")]
        static void CCDumpAddressableValues(ConCommandArgs args)
        {
            if (Directory.Exists(_addressablesDumpPath))
            {
                Directory.Delete(_addressablesDumpPath, true);
            }

            foreach (AssetInfo assetInfo in AddressablesIterator.GetAllAssetsFlattened())
            {
                Log.Info($"Dumping asset values of: {assetInfo.Key} ({assetInfo.AssetType.Name})");

                string sanitizedFilePath = $"{assetInfo.Key} ({assetInfo.AssetType.Name})";

                {
                    int lastBracketStartIndex = sanitizedFilePath.LastIndexOf('[');

                    // exclude 0 from the 'valid range' path since that would result in a startIndex of -1
                    int directorySeparatorSearchEnd = lastBracketStartIndex > 0 ? lastBracketStartIndex - 1 : sanitizedFilePath.Length - 1;

                    // LastIndexOf 'startIndex' is actually the END index of the search range since it searches backwards
                    int lastDirectorySeparatorIndex = sanitizedFilePath.LastIndexOf('/', directorySeparatorSearchEnd);
                    if (lastDirectorySeparatorIndex >= 0)
                    {
                        sanitizedFilePath = sanitizedFilePath.ReplaceCharsFast(PathUtils.OrderedInvalidFileNameChars, '_', lastDirectorySeparatorIndex + 1);
                    }
                }

                FilePath dumpFilePath = $"{Path.Combine(_addressablesDumpPath, sanitizedFilePath)}.txt";

                const int MaxPathLength = 260;
                if (dumpFilePath.FullPath.Length > MaxPathLength)
                {
                    // This is dumb
                    int maxFileNameLength = MaxPathLength -
                                            (dumpFilePath.DirectoryPath.Length + 1) - // include trailing / in directory
                                            (dumpFilePath.DirectoryPath.Count(c => c == '\\') + 1) - // double count backslashes for some reason
                                            dumpFilePath.FileExtension.Length -
                                            1; // null terminator
                    if (maxFileNameLength <= 0)
                    {
                        throw new IndexOutOfRangeException("This operating system fucking sucks man");
                    }

                    dumpFilePath.FileNameWithoutExtension = dumpFilePath.FileNameWithoutExtension.Remove(maxFileNameLength);
                    Log.Warning("Trimming file name due to length");
                }

                FilePath originalFilePath = dumpFilePath;

                Directory.CreateDirectory(dumpFilePath.DirectoryPath);

                dumpFilePath.MakeUnique();

                using (FileStream fileStream = File.Open(dumpFilePath, FileMode.CreateNew, FileAccess.Write))
                {
                    using (StreamWriter fileWriter = new StreamWriter(fileStream, Encoding.UTF8, 1024, true))
                    {
                        fileWriter.WriteLine($"// Key: {assetInfo.Key}");
                        // TODO: Add asset guid here aswell
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

                FilePath[] duplicateFiles = [.. originalFilePath.GetAllExistingDuplicateFileNames()];
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
