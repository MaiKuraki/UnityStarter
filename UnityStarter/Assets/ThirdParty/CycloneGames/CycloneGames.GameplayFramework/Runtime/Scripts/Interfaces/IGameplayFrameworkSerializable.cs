namespace CycloneGames.GameplayFramework.Runtime
{
    /// <summary>
    /// Contract for serializing/deserializing gameplay framework objects.
    /// Implementations write/read state for save systems, networking, and persistence.
    /// </summary>
    public interface IGameplayFrameworkSerializable
    {
        /// <summary>
        /// Write current state to data writer.
        /// Used for save systems, network replication, or state snapshots.
        /// </summary>
        void Serialize(IDataWriter writer);

        /// <summary>
        /// Read state from data reader.
        /// Used for loading saves, network updates, or state restoration.
        /// </summary>
        void Deserialize(IDataReader reader);
    }

    /// <summary>
    /// Contract for writing serialized data.
    /// Implementation can be JSON, binary, network stream, etc.
    /// </summary>
    public interface IDataWriter
    {
        void WriteString(string key, string value);
        void WriteInt(string key, int value);
        void WriteFloat(string key, float value);
        void WriteBool(string key, bool value);
        void WriteDouble(string key, double value);
    }

    /// <summary>
    /// Contract for reading serialized data.
    /// Implementation must match the writer format.
    /// </summary>
    public interface IDataReader
    {
        string ReadString(string key);
        int ReadInt(string key);
        float ReadFloat(string key);
        bool ReadBool(string key);
        double ReadDouble(string key);
    }
}
