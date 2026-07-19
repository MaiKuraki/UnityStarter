using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using CycloneGames.GameplayTags.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

[assembly: InternalsVisibleTo("CycloneGames.GameplayTags.Tests.Editor")]

namespace CycloneGames.GameplayTags.Unity.Editor
{
   // Editor-owned project catalog repository. This source is never compiled into Player assemblies.
   internal sealed class FileGameplayTagSource : IGameplayTagSource, IDeleteTagHandler
   {
      internal const long MaxFileSizeBytes = 8L * 1024L * 1024L;
      internal const int MaxSourceFileCount = 256;
      internal const int MaxDirectoryEntryCount = 1024;
      internal const int MaxTagsPerSource = GameplayTagUtility.MaxRegisteredTagCount;
      internal const int MaxCommentLength = BuildTagBinaryFormat.MaxDescriptionLength;

      private const string TagsPropertyName = "tags";
      private const string DescriptionPropertyName = "description";
      private const string FlagsPropertyName = "flags";
      private const int MaxJsonDepth = 8;

      private static readonly UTF8Encoding s_StrictUtf8 = new(false, true);
      private static readonly GameplayTagFlags s_KnownFlags = GameplayTagFlags.HideInEditor;

      private readonly struct TagInFile
      {
         public readonly string Name;
         public readonly string Description;
         public readonly GameplayTagFlags Flags;

         public TagInFile(string name, string description, GameplayTagFlags flags)
         {
            Name = name;
            Description = description;
            Flags = flags;
         }
      }

      private readonly struct SourceFileIdentity : IEquatable<SourceFileIdentity>
      {
         public static readonly SourceFileIdentity Missing = new(false, 0, string.Empty);

         public readonly bool Exists;
         public readonly long Length;
         public readonly string Sha256;

         public SourceFileIdentity(bool exists, long length, string sha256)
         {
            Exists = exists;
            Length = length;
            Sha256 = sha256 ?? string.Empty;
         }

         public bool Equals(SourceFileIdentity other)
         {
            return Exists == other.Exists &&
                   Length == other.Length &&
                   string.Equals(Sha256, other.Sha256, StringComparison.Ordinal);
         }
      }

      private sealed class SourceDocument
      {
         public readonly SortedDictionary<string, TagInFile> Tags = new(StringComparer.Ordinal);
      }

      private sealed class StrictJsonTextReader : JsonTextReader
      {
         public StrictJsonTextReader(TextReader reader) : base(reader)
         { }

         public override bool Read()
         {
            bool hasToken = base.Read();
            if (hasToken && TokenType == JsonToken.Comment)
               throw new InvalidDataException("Gameplay tag source JSON comments are not supported.");
            return hasToken;
         }
      }

      public static string DirectoryPath => Path.GetFullPath(GameplayTagRuntimePlatform.GetProjectTagSettingsDirectory());
      public string Name { get; }
      public string FilePath { get; }
      public bool IsReadOnly => false;
      internal Exception LastLoadException { get; private set; }

      private SourceDocument m_Document;
      private SourceFileIdentity m_LoadedIdentity;
      private bool m_HasLoadedIdentity;

      public FileGameplayTagSource(string filePath)
      {
         FilePath = ValidateSourcePath(filePath);
         Name = Path.GetFileName(FilePath);
      }

      public bool TryLoad()
      {
         m_Document = null;
         m_LoadedIdentity = default;
         m_HasLoadedIdentity = false;
         LastLoadException = null;
         try
         {
            if (File.Exists(FilePath))
            {
               m_Document = LoadDocument(out m_LoadedIdentity);
            }
            else
            {
               m_Document = new SourceDocument();
               m_LoadedIdentity = SourceFileIdentity.Missing;
            }
            m_HasLoadedIdentity = true;
            return true;
         }
         catch (Exception exception)
         {
            LastLoadException = exception;
            GameplayTagLogger.LogError($"Failed to load gameplay tags from '{Name}': {exception.Message}");
            return false;
         }
      }

      public static IEnumerable<FileGameplayTagSource> GetAllFileSources()
      {
         string directoryPath = DirectoryPath;
         if (!Directory.Exists(directoryPath))
            yield break;

         ThrowIfRecoveryArtifactsExist(directoryPath);

         List<string> files = new();
         foreach (string file in Directory.EnumerateFiles(directoryPath, "*.json", SearchOption.TopDirectoryOnly))
         {
            if (files.Count == MaxSourceFileCount)
               throw new InvalidDataException($"Gameplay tag source count exceeds {MaxSourceFileCount}.");
            files.Add(file);
         }

         files.Sort(StringComparer.Ordinal);
         for (int i = 0; i < files.Count; i++)
         {
            FileGameplayTagSource source = new(files[i]);
            if (!source.TryLoad())
            {
               throw new InvalidDataException(
                  $"Gameplay tag source '{source.Name}' could not be loaded.",
                  source.LastLoadException);
            }
            yield return source;
         }
      }

      public void RegisterTags(GameplayTagRegistrationContext context)
      {
         if (context == null)
            throw new ArgumentNullException(nameof(context));
         EnsureLoaded();
         foreach (TagInFile tag in m_Document.Tags.Values)
            context.RegisterTag(tag.Name, tag.Description, tag.Flags, this);
      }

      public void AddTag(string tagName, string description)
      {
         EnsureLoaded();
         GameplayTagUtility.ValidateName(tagName);
         ValidateDescription(description);
         if (m_Document.Tags.ContainsKey(tagName))
            throw new InvalidOperationException($"Tag '{tagName}' is already registered in '{Name}'.");

         SourceDocument candidate = CloneDocument(m_Document);
         candidate.Tags.Add(tagName, new TagInFile(tagName, description ?? string.Empty, GameplayTagFlags.None));
         SaveFile(candidate);
         m_Document = candidate;
      }

      public void DeleteTag(string tagName)
      {
         EnsureLoaded();
         GameplayTagUtility.ValidateName(tagName);
         if (!m_Document.Tags.ContainsKey(tagName))
            return;

         SourceDocument candidate = CloneDocument(m_Document);
         candidate.Tags.Remove(tagName);
         SaveFile(candidate);
         m_Document = candidate;
      }

      internal static string ValidateSourcePath(string filePath)
      {
         if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("Gameplay tag source path cannot be empty.", nameof(filePath));

         string fullPath = Path.GetFullPath(filePath);
         if (!string.Equals(Path.GetExtension(fullPath), ".json", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Gameplay tag source must use the .json extension.", nameof(filePath));

         string settingsRoot = DirectoryPath;
         if (!IsPathInsideDirectory(fullPath, settingsRoot))
            throw new UnauthorizedAccessException("Gameplay tag source path must stay inside the configured settings directory.");

         string projectRoot = Path.GetFullPath(Path.Combine(settingsRoot, "..", ".."));
         RejectReparsePoints(projectRoot, settingsRoot, fullPath);
         return fullPath;
      }

      private SourceDocument LoadDocument(out SourceFileIdentity identity)
      {
         JObject root;
         using (FileStream stream = new(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
         using (ExactLengthReadStream boundedStream = OpenBoundedUtf8Stream(stream))
         using (HashingReadStream hashingStream = new(boundedStream, leaveOpen: true))
         using (StreamReader textReader = new(hashingStream, s_StrictUtf8, false, 4096, true))
         using (StrictJsonTextReader jsonReader = new(textReader)
         {
            CloseInput = false,
            DateParseHandling = DateParseHandling.None,
            MaxDepth = MaxJsonDepth
         })
         {
            JToken token = JToken.Load(jsonReader, new JsonLoadSettings
            {
               CommentHandling = CommentHandling.Ignore,
               DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
               LineInfoHandling = LineInfoHandling.Load
            });
            if (jsonReader.Read())
               throw new InvalidDataException($"Gameplay tag source '{Name}' contains trailing JSON content.");
            root = token as JObject ?? throw new InvalidDataException($"Gameplay tag source '{Name}' must have a JSON object root.");
            identity = new SourceFileIdentity(true, stream.Length, hashingStream.CompleteHashHex());
         }

         ValidateExactProperties(root, TagsPropertyName);

         JObject tagsObject = root[TagsPropertyName] as JObject
            ?? throw new InvalidDataException($"Gameplay tag source '{Name}' must contain a '{TagsPropertyName}' object.");
         if (tagsObject.Count > MaxTagsPerSource)
            throw new InvalidDataException($"Gameplay tag source '{Name}' exceeds {MaxTagsPerSource} tags.");

         SourceDocument document = new();
         foreach (JProperty property in tagsObject.Properties())
         {
            GameplayTagUtility.ValidateName(property.Name);
            JObject tagObject = property.Value as JObject
               ?? throw new InvalidDataException($"Tag '{property.Name}' in '{Name}' must contain a JSON object.");
            ValidateExactProperties(tagObject, DescriptionPropertyName, FlagsPropertyName);

            string description = string.Empty;
            JToken descriptionToken = tagObject[DescriptionPropertyName];
            if (descriptionToken != null)
            {
               if (descriptionToken.Type != JTokenType.String)
                  throw new InvalidDataException($"Tag '{property.Name}' description must be a string.");
               description = descriptionToken.Value<string>() ?? string.Empty;
            }
            ValidateDescription(description);

            GameplayTagFlags flags = GameplayTagFlags.None;
            JToken flagsToken = tagObject[FlagsPropertyName];
            if (flagsToken != null)
            {
               if (flagsToken.Type != JTokenType.Integer)
                  throw new InvalidDataException($"Tag '{property.Name}' flags must be an integer.");
               long rawFlags = flagsToken.Value<long>();
               if (rawFlags < 0 || rawFlags > int.MaxValue || (((GameplayTagFlags)rawFlags) & ~s_KnownFlags) != 0)
                  throw new InvalidDataException($"Tag '{property.Name}' contains unsupported flags.");
               flags = (GameplayTagFlags)rawFlags;
            }

            document.Tags.Add(property.Name, new TagInFile(property.Name, description, flags));
         }

         return document;
      }

      private ExactLengthReadStream OpenBoundedUtf8Stream(FileStream stream)
      {
         long length = stream.Length;
         if (length > MaxFileSizeBytes)
            throw new InvalidDataException($"Gameplay tag source '{Name}' exceeds {MaxFileSizeBytes} bytes.");
         Utf8FileSafety.RejectByteOrderMark(stream, FilePath);
         return new ExactLengthReadStream(stream, length, leaveOpen: true);
      }

      private void SaveFile(SourceDocument document)
      {
         if (!m_HasLoadedIdentity)
            throw new InvalidOperationException($"Gameplay tag source '{Name}' has not been loaded.");

         string directory = Path.GetDirectoryName(FilePath)
            ?? throw new InvalidOperationException("Gameplay tag source path has no parent directory.");
         Directory.CreateDirectory(directory);
         ValidateSourcePath(FilePath);

         JObject tags = new();
         foreach (TagInFile tag in document.Tags.Values)
         {
            JObject tagObject = new();
            if (!string.IsNullOrEmpty(tag.Description))
               tagObject[DescriptionPropertyName] = tag.Description;
            if (tag.Flags != GameplayTagFlags.None)
               tagObject[FlagsPropertyName] = (int)tag.Flags;
            tags.Add(tag.Name, tagObject);
         }

         JObject root = new()
         {
            [TagsPropertyName] = tags
         };
         string content = root.ToString(Formatting.Indented);
         if (s_StrictUtf8.GetByteCount(content) > MaxFileSizeBytes)
            throw new InvalidDataException($"Gameplay tag source '{Name}' exceeds {MaxFileSizeBytes} bytes.");

         string temporaryPath = Path.Combine(directory, $".{Path.GetFileName(FilePath)}.{Guid.NewGuid():N}.tmp");
         string backupPath = Path.Combine(directory, $".{Path.GetFileName(FilePath)}.{Guid.NewGuid():N}.bak");
         try
         {
            using (FileStream stream = new(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (StreamWriter writer = new(stream, s_StrictUtf8, 4096, true))
            {
               writer.Write(content);
               writer.Flush();
               stream.Flush(flushToDisk: true);
            }

            ValidateSourcePath(FilePath);
            SourceFileIdentity candidateIdentity = ComputeFileIdentity(temporaryPath);
            SourceFileIdentity currentIdentity = ComputeFileIdentity(FilePath);
            if (!m_LoadedIdentity.Equals(currentIdentity))
            {
               throw new InvalidOperationException(
                  $"Gameplay tag source '{Name}' changed after it was loaded. Reload it before saving.");
            }

            if (currentIdentity.Exists)
               ReplaceExistingFile(temporaryPath, backupPath);
            else
               File.Move(temporaryPath, FilePath);

            m_LoadedIdentity = candidateIdentity;
         }
         finally
         {
            if (File.Exists(temporaryPath))
               File.Delete(temporaryPath);
         }
      }

      internal void ReplaceExistingFile(string temporaryPath, string backupPath)
      {
         bool targetWasReplaced = false;
         try
         {
            File.Replace(temporaryPath, FilePath, backupPath);
            targetWasReplaced = true;

            SourceFileIdentity replacedIdentity = ComputeFileIdentity(backupPath);
            if (!m_LoadedIdentity.Equals(replacedIdentity))
            {
               throw new InvalidOperationException(
                  $"Gameplay tag source '{Name}' changed while it was being saved. The replaced content was preserved for manual reconciliation.");
            }

            File.Delete(backupPath);
            targetWasReplaced = false;
         }
         catch (Exception operationException)
         {
            if (targetWasReplaced && File.Exists(backupPath))
            {
               throw new IOException(
                  $"Gameplay tag source save detected a conflict after atomic replacement. " +
                  $"The current target was left untouched and the replaced content was preserved at '{backupPath}'. " +
                  "Reconcile both files, then remove the recovery copy and reload the catalog.",
                  operationException);
            }
            throw;
         }
      }

      internal static void ThrowIfRecoveryArtifactsExist(string directoryPath)
      {
         int entryCount = 0;
         foreach (string path in Directory.EnumerateFiles(directoryPath, "*", SearchOption.TopDirectoryOnly))
         {
            entryCount++;
            if (entryCount > MaxDirectoryEntryCount)
            {
               throw new InvalidDataException(
                  $"Gameplay tag settings directory exceeds {MaxDirectoryEntryCount} files.");
            }

            if (IsRecoveryArtifactName(Path.GetFileName(path)))
            {
               throw new InvalidDataException(
                  $"Gameplay tag source recovery artifact '{path}' requires manual reconciliation before the catalog can reload.");
            }
         }
      }

      private static bool IsRecoveryArtifactName(string fileName)
      {
         if (string.IsNullOrEmpty(fileName) || fileName[0] != '.')
            return false;

         int jsonMarker = fileName.LastIndexOf(".json.", StringComparison.OrdinalIgnoreCase);
         int suffixSeparator = fileName.LastIndexOf('.');
         if (jsonMarker < 1 || suffixSeparator <= jsonMarker + 6)
            return false;

         string suffix = fileName.Substring(suffixSeparator);
         if (!string.Equals(suffix, ".tmp", StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(suffix, ".bak", StringComparison.OrdinalIgnoreCase))
         {
            return false;
         }

         int identifierStart = jsonMarker + ".json.".Length;
         if (suffixSeparator - identifierStart != 32)
            return false;
         for (int i = identifierStart; i < suffixSeparator; i++)
         {
            char value = fileName[i];
            bool isHex = (value >= '0' && value <= '9') ||
                         (value >= 'a' && value <= 'f') ||
                         (value >= 'A' && value <= 'F');
            if (!isHex)
               return false;
         }
         return true;
      }

      private static SourceFileIdentity ComputeFileIdentity(string path)
      {
         try
         {
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            long length = stream.Length;
            if (length > MaxFileSizeBytes)
               throw new InvalidDataException($"Gameplay tag source file exceeds {MaxFileSizeBytes} bytes.");

            using ExactLengthReadStream boundedStream = new(stream, length, leaveOpen: true);
            using SHA256 sha256 = SHA256.Create();
            byte[] hash = sha256.ComputeHash(boundedStream);
            return new SourceFileIdentity(true, length, ToHex(hash));
         }
         catch (FileNotFoundException)
         {
            return SourceFileIdentity.Missing;
         }
         catch (DirectoryNotFoundException)
         {
            return SourceFileIdentity.Missing;
         }
      }

      private static string ToHex(byte[] bytes)
      {
         StringBuilder builder = new(bytes.Length * 2);
         for (int i = 0; i < bytes.Length; i++)
            builder.Append(bytes[i].ToString("x2"));
         return builder.ToString();
      }

      private void EnsureLoaded()
      {
         if (m_Document == null)
            throw new InvalidOperationException($"Gameplay tag source '{Name}' has not been loaded.");
      }

      private static SourceDocument CloneDocument(SourceDocument source)
      {
         SourceDocument clone = new();
         foreach (KeyValuePair<string, TagInFile> pair in source.Tags)
            clone.Tags.Add(pair.Key, pair.Value);
         return clone;
      }

      private static void ValidateExactProperties(JObject value, params string[] allowedNames)
      {
         foreach (JProperty property in value.Properties())
         {
            bool allowed = false;
            for (int i = 0; i < allowedNames.Length; i++)
            {
               if (string.Equals(property.Name, allowedNames[i], StringComparison.Ordinal))
               {
                  allowed = true;
                  break;
               }
            }
            if (!allowed)
               throw new InvalidDataException($"Unsupported JSON property '{property.Name}'.");
         }
      }

      private static bool IsPathInsideDirectory(string filePath, string directoryPath)
      {
         string fullFilePath = Path.GetFullPath(filePath);
         string fullDirectoryPath = Path.GetFullPath(directoryPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
         StringComparison comparison = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
         return fullFilePath.StartsWith(fullDirectoryPath, comparison);
      }

      private static void RejectReparsePoints(string projectRoot, string settingsRoot, string fullPath)
      {
         string fullProjectRoot = Path.GetFullPath(projectRoot);
         string current = Path.GetDirectoryName(fullPath);
         while (!string.IsNullOrEmpty(current))
         {
            if (Directory.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
               throw new UnauthorizedAccessException("Gameplay tag source paths cannot traverse symbolic links or junctions.");

            if (PathsEqual(current, fullProjectRoot))
               break;
            string parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) || PathsEqual(parent, current))
               throw new UnauthorizedAccessException($"Gameplay tag settings root '{settingsRoot}' is outside the configured project root.");
            current = parent;
         }

         if (File.Exists(fullPath) && (File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
            throw new UnauthorizedAccessException("Gameplay tag source files cannot be symbolic links.");
      }

      private static bool PathsEqual(string left, string right)
      {
         StringComparison comparison = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
         return string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            comparison);
      }

      private static void ValidateDescription(string description)
      {
         if (description != null && description.Length > MaxCommentLength)
            throw new InvalidDataException($"Gameplay tag description length cannot exceed {MaxCommentLength} UTF-16 code units.");
      }
   }

   internal static class Utf8FileSafety
   {
      internal static void RejectByteOrderMark(FileStream stream, string path)
      {
         if (stream == null)
            throw new ArgumentNullException(nameof(stream));

         long originalPosition = stream.Position;
         Span<byte> prefix = stackalloc byte[4];
         int count = stream.Read(prefix);
         stream.Position = originalPosition;

         bool hasBom = count >= 3 && prefix[0] == 0xEF && prefix[1] == 0xBB && prefix[2] == 0xBF;
         hasBom |= count >= 2 && ((prefix[0] == 0xFF && prefix[1] == 0xFE) ||
                                 (prefix[0] == 0xFE && prefix[1] == 0xFF));
         hasBom |= count >= 4 && ((prefix[0] == 0x00 && prefix[1] == 0x00 && prefix[2] == 0xFE && prefix[3] == 0xFF) ||
                                 (prefix[0] == 0xFF && prefix[1] == 0xFE && prefix[2] == 0x00 && prefix[3] == 0x00));
         if (hasBom)
            throw new InvalidDataException($"File '{path}' must use UTF-8 without a byte-order mark.");
      }
   }

   internal sealed class ExactLengthReadStream : Stream
   {
      private readonly Stream m_Inner;
      private readonly bool m_LeaveOpen;
      private long m_Remaining;

      internal ExactLengthReadStream(Stream inner, long expectedLength, bool leaveOpen)
      {
         m_Inner = inner ?? throw new ArgumentNullException(nameof(inner));
         if (!inner.CanRead)
            throw new ArgumentException("The wrapped stream must be readable.", nameof(inner));
         if (expectedLength < 0)
            throw new ArgumentOutOfRangeException(nameof(expectedLength));
         m_Remaining = expectedLength;
         m_LeaveOpen = leaveOpen;
      }

      public override bool CanRead => true;
      public override bool CanSeek => false;
      public override bool CanWrite => false;
      public override long Length => throw new NotSupportedException();
      public override long Position
      {
         get => throw new NotSupportedException();
         set => throw new NotSupportedException();
      }

      public override int Read(byte[] buffer, int offset, int count)
      {
         if (count == 0)
            return 0;
         if (m_Remaining == 0)
            return EnsureNoTrailingData();

         int allowed = (int)Math.Min(count, m_Remaining);
         int read = m_Inner.Read(buffer, offset, allowed);
         if (read == 0)
            throw new InvalidDataException("The file length changed while it was being read.");
         m_Remaining -= read;
         return read;
      }

      public override int ReadByte()
      {
         if (m_Remaining == 0)
         {
            EnsureNoTrailingData();
            return -1;
         }

         int value = m_Inner.ReadByte();
         if (value < 0)
            throw new InvalidDataException("The file length changed while it was being read.");
         m_Remaining--;
         return value;
      }

      private int EnsureNoTrailingData()
      {
         if (m_Inner.ReadByte() >= 0)
            throw new InvalidDataException("The file length changed while it was being read.");
         return 0;
      }

      protected override void Dispose(bool disposing)
      {
         if (disposing && !m_LeaveOpen)
            m_Inner.Dispose();
         base.Dispose(disposing);
      }

      public override void Flush() => throw new NotSupportedException();
      public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
      public override void SetLength(long value) => throw new NotSupportedException();
      public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
   }

   internal sealed class HashingReadStream : Stream
   {
      private readonly Stream m_Inner;
      private readonly bool m_LeaveOpen;
      private readonly IncrementalHash m_Hash;
      private bool m_ReachedEnd;
      private bool m_HashCompleted;

      internal HashingReadStream(Stream inner, bool leaveOpen)
      {
         m_Inner = inner ?? throw new ArgumentNullException(nameof(inner));
         if (!inner.CanRead)
            throw new ArgumentException("The wrapped stream must be readable.", nameof(inner));
         m_LeaveOpen = leaveOpen;
         m_Hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
      }

      public override bool CanRead => true;
      public override bool CanSeek => false;
      public override bool CanWrite => false;
      public override long Length => throw new NotSupportedException();
      public override long Position
      {
         get => throw new NotSupportedException();
         set => throw new NotSupportedException();
      }

      public override int Read(byte[] buffer, int offset, int count)
      {
         ThrowIfHashCompleted();
         int read = m_Inner.Read(buffer, offset, count);
         if (read == 0)
            m_ReachedEnd = true;
         else
            m_Hash.AppendData(buffer, offset, read);
         return read;
      }

      public override int ReadByte()
      {
         ThrowIfHashCompleted();
         int value = m_Inner.ReadByte();
         if (value < 0)
         {
            m_ReachedEnd = true;
         }
         else
         {
            Span<byte> singleByte = stackalloc byte[1];
            singleByte[0] = (byte)value;
            m_Hash.AppendData(singleByte);
         }
         return value;
      }

      internal string CompleteHashHex()
      {
         ThrowIfHashCompleted();
         if (!m_ReachedEnd)
         {
            byte[] buffer = new byte[1024];
            while (Read(buffer, 0, buffer.Length) != 0)
            { }
         }

         m_HashCompleted = true;
         byte[] hash = m_Hash.GetHashAndReset();
         StringBuilder builder = new(hash.Length * 2);
         for (int i = 0; i < hash.Length; i++)
            builder.Append(hash[i].ToString("x2"));
         return builder.ToString();
      }

      private void ThrowIfHashCompleted()
      {
         if (m_HashCompleted)
            throw new InvalidOperationException("The source content hash has already been completed.");
      }

      protected override void Dispose(bool disposing)
      {
         if (disposing)
         {
            m_Hash.Dispose();
            if (!m_LeaveOpen)
               m_Inner.Dispose();
         }
         base.Dispose(disposing);
      }

      public override void Flush() => throw new NotSupportedException();
      public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
      public override void SetLength(long value) => throw new NotSupportedException();
      public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
   }
}
