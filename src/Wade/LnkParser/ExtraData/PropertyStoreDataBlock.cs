namespace Wade.LnkParser.ExtraData;

/// <summary>
/// PropertyStoreDataBlock (signature 0xA0000009)
/// Stores metadata properties in serialized property store format.
/// </summary>
internal class PropertyStoreDataBlock : ExtraDataBlock
{
    public List<PropertyStore> PropertyStores { get; init; } = [];

    private PropertyStoreDataBlock(uint blockSize, uint blockSignature)
        : base(blockSize, blockSignature)
    {
    }

    public static PropertyStoreDataBlock Parse(BinaryReader reader, uint blockSize, uint blockSignature)
    {
        var startPosition = reader.BaseStream.Position;
        var endPosition = startPosition + blockSize - 8; // -8 for size and signature already read

        var propertyStores = new List<PropertyStore>();

        while (reader.BaseStream.Position < endPosition)
        {
            var storageSize = reader.ReadUInt32();
            if (storageSize == 0) break;

            var storeStartPosition = reader.BaseStream.Position;
            var version = reader.ReadUInt32();
            var formatId = new Guid(reader.ReadBytes(16));

            var properties = new List<SerializedPropertyValue>();

            while (reader.BaseStream.Position < storeStartPosition + storageSize - 4)
            {
                var valueSize = reader.ReadUInt32();
                if (valueSize == 0) break;

                var propStartPosition = reader.BaseStream.Position;
                var id = reader.ReadUInt32();

                // Reserved byte
                reader.ReadByte();

                var valueType = reader.ReadUInt16();

                // Read the value based on type
                object? value = null;

                // Calculate remaining bytes in this property value
                // valueSize includes: id(4) + reserved(1) + type(2) + actual value
                if (valueSize < 7)
                {
                    // Invalid value size, skip this property
                    continue;
                }

                var remainingBytes = valueSize - 7; // -4 for id, -1 for reserved, -2 for type

                if (valueType == 0x001F && remainingBytes > 0) // VT_LPWSTR
                {
                    var strBytes = reader.ReadBytes((int)remainingBytes);
                    value = ReadNullTerminatedUnicodeString(strBytes);
                    remainingBytes = 0;
                }
                else if (valueType == 0x0013 && remainingBytes >= 4) // VT_UI4
                {
                    value = reader.ReadUInt32();
                    remainingBytes -= 4;
                }
                else if (valueType == 0x0048 && remainingBytes >= 16) // VT_CLSID
                {
                    value = new Guid(reader.ReadBytes(16));
                    remainingBytes -= 16;
                }
                else if (remainingBytes > 0)
                {
                    // Unknown type or insufficient bytes, read as bytes
                    value = reader.ReadBytes((int)remainingBytes);
                    remainingBytes = 0;
                }

                // Skip any remaining bytes
                if (remainingBytes > 0)
                {
                    reader.ReadBytes((int)remainingBytes);
                }

                properties.Add(new SerializedPropertyValue
                {
                    ValueSize = valueSize,
                    Id = id,
                    ValueType = valueType,
                    Value = value
                });
            }

            propertyStores.Add(new PropertyStore
            {
                StorageSize = storageSize,
                Version = $"0x{version:X8}",
                FormatId = formatId,
                SerializedPropertyValues = properties
            });
        }

        return new PropertyStoreDataBlock(blockSize, blockSignature)
        {
            PropertyStores = propertyStores
        };
    }

    private static string ReadNullTerminatedUnicodeString(byte[] bytes)
    {
        for (int i = 0; i < bytes.Length - 1; i += 2)
        {
            if (bytes[i] == 0 && bytes[i + 1] == 0)
            {
                return System.Text.Encoding.Unicode.GetString(bytes, 0, i);
            }
        }
        return System.Text.Encoding.Unicode.GetString(bytes);
    }
}

internal class PropertyStore
{
    public uint StorageSize { get; init; }
    public string Version { get; init; } = string.Empty;
    public Guid FormatId { get; init; }
    public List<SerializedPropertyValue> SerializedPropertyValues { get; init; } = [];
}

internal class SerializedPropertyValue
{
    public uint ValueSize { get; init; }
    public uint Id { get; init; }
    public ushort ValueType { get; init; }
    public object? Value { get; init; }

    public string GetValueTypeName()
    {
        return ValueType switch
        {
            0x001F => "VT_LPWSTR",
            0x0013 => "VT_UI4",
            0x0048 => "VT_CLSID",
            _ => $"Unknown (0x{ValueType:X4})"
        };
    }
}
