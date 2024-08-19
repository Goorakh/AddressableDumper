using System.IO;

namespace AddressableDumper.ValueDumper
{
    public record struct FilePath(string FullPath)
    {
        public readonly bool Exists => File.Exists(FullPath);

        public string DirectoryName
        {
            readonly get
            {
                return Path.GetDirectoryName(FullPath);
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
                FullPath = Path.Combine(DirectoryName, value);
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
                FileName = FileNameWithoutExtension + (value ?? string.Empty);
            }
        }
    }
}
