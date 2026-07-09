using System;
using System.IO;
using System.Text;

using CycloneGames.IO.Runtime;

namespace CycloneGames.AssetManagement.Runtime
{
    public sealed class FileAssetPatchJournalStore : IAssetPatchJournalStore
    {
        private readonly object _gate = new object();
        private readonly StringBuilder _builder;

        public FileAssetPatchJournalStore(string filePath, int initialCapacity = 512)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("Patch journal file path cannot be null or empty.", nameof(filePath));
            }

            FilePath = filePath;
            _builder = new StringBuilder(initialCapacity <= 0 ? 512 : initialCapacity);
        }

        public string FilePath { get; }

        public void Write(in AssetPatchJournalRecord record)
        {
            lock (_gate)
            {
                _builder.Length = 0;
                AssetPatchJournalCodec.AppendJson(_builder, in record);
                FileUtility.WriteAllTextAtomic(FilePath, _builder.ToString(), FileUtility.Utf8NoBom);
            }
        }

        public bool TryRead(out AssetPatchJournalRecord record, out string error)
        {
            record = default;
            error = null;

            try
            {
                if (!File.Exists(FilePath))
                {
                    return false;
                }

                string json = FileUtility.ReadAllText(FilePath);
                return AssetPatchJournalCodec.TryFromJson(json, out record, out error);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)
            {
                error = ex.Message;
                return false;
            }
        }

        public bool TryClear(out string error)
        {
            error = null;

            lock (_gate)
            {
                try
                {
                    if (File.Exists(FilePath))
                    {
                        File.Delete(FilePath);
                    }

                    return true;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)
                {
                    error = ex.Message;
                    return false;
                }
            }
        }
    }
}
