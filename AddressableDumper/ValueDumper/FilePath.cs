using System.Collections.Generic;
using System.IO;

namespace AddressableDumper.ValueDumper
{
    public record struct FilePath(string FullPath)
    {
        public readonly bool Exists => File.Exists(FullPath);

        public string DirectoryPath
        {
            readonly get
            {
                return Path.GetDirectoryName(FullPath) ?? string.Empty;
            }
            set
            {
                FullPath = Path.Combine(value, FileName);
            }
        }

        public string FileName
        {
            readonly get
            {
                return Path.GetFileName(FullPath);
            }
            set
            {
                FullPath = Path.Combine(DirectoryPath, value);
            }
        }

        public string FileNameWithoutExtension
        {
            readonly get
            {
                return Path.GetFileNameWithoutExtension(FullPath);
            }
            set
            {
                FileName = value + FileExtension;
            }
        }

        public string FileExtension
        {
            readonly get
            {
                return Path.GetExtension(FullPath);
            }
            set
            {
                string extension = value ?? string.Empty;
                if (!string.IsNullOrEmpty(extension) && extension[0] != '.')
                {
                    extension = "." + extension;
                }

                FileName = FileNameWithoutExtension + extension;
            }
        }

        public void MakeUnique()
        {
            string originalFileName = FileNameWithoutExtension;

            int fileNumber = 1;
            while (Exists)
            {
                FileNameWithoutExtension = originalFileName + $" ({fileNumber})";
                fileNumber++;
            }
        }

        public readonly IEnumerable<FilePath> GetAllExistingDuplicateFileNames()
        {
            FilePath current = this;
            int fileNumber = 0;

            while (current.Exists)
            {
                yield return current;
                fileNumber++;
                current.FileNameWithoutExtension = FileNameWithoutExtension + $" ({fileNumber})";
            }
        }

        public override readonly string ToString()
        {
            return FullPath;
        }

        public override readonly int GetHashCode()
        {
            return FullPath.GetHashCode();
        }

        public static implicit operator string(FilePath file)
        {
            return file.FullPath;
        }

        public static implicit operator FilePath(string path)
        {
            return new FilePath(path);
        }
    }
}
