using System.Buffers.Binary;
using System.Text;

namespace TikTokIntegration.Protocol;

public enum WireType
{
    Varint = 0,
    Fixed64 = 1,
    LengthDelimited = 2,
    StartGroup = 3,
    EndGroup = 4,
    Fixed32 = 5
}

/// <summary>
/// Minimal protobuf wire-format reader for decoding TikTok webcast messages.
/// Only the wire primitives are implemented; message decoding lives in <see cref="WebcastDecoder"/>.
/// </summary>
public ref struct ProtoReader
{
    private readonly ReadOnlySpan<byte> _data;
    private int _pos;

    public ProtoReader(ReadOnlySpan<byte> data)
    {
        _data = data;
        _pos = 0;
    }

    public bool TryReadTag(out int fieldNumber, out WireType wireType)
    {
        fieldNumber = 0;
        wireType = WireType.Varint;

        if (_pos >= _data.Length)
        {
            return false;
        }

        var tag = ReadVarint();
        fieldNumber = (int)(tag >> 3);
        wireType = (WireType)(tag & 0x7);
        return fieldNumber > 0;
    }

    public ulong ReadVarint()
    {
        ulong result = 0;
        var shift = 0;

        while (_pos < _data.Length)
        {
            var b = _data[_pos++];
            if (shift < 64)
            {
                result |= (ulong)(b & 0x7F) << shift;
            }
            if ((b & 0x80) == 0)
            {
                return result;
            }
            shift += 7;
        }

        throw new InvalidDataException("Truncated varint in protobuf payload");
    }

    public ReadOnlySpan<byte> ReadLengthDelimited()
    {
        var length = (int)ReadVarint();
        if (length < 0 || _pos + length > _data.Length)
        {
            throw new InvalidDataException("Truncated length-delimited field in protobuf payload");
        }

        var span = _data.Slice(_pos, length);
        _pos += length;
        return span;
    }

    public string ReadString() => Encoding.UTF8.GetString(ReadLengthDelimited());

    public void Skip(WireType wireType)
    {
        switch (wireType)
        {
            case WireType.Varint:
                ReadVarint();
                break;
            case WireType.Fixed64:
                _pos += 8;
                break;
            case WireType.LengthDelimited:
                ReadLengthDelimited();
                break;
            case WireType.Fixed32:
                _pos += 4;
                break;
            default:
                throw new InvalidDataException($"Unsupported wire type {wireType}");
        }

        if (_pos > _data.Length)
        {
            throw new InvalidDataException("Truncated field in protobuf payload");
        }
    }
}

/// <summary>
/// Minimal protobuf writer. Only used for the websocket acknowledgment frames.
/// </summary>
public static class ProtoWriter
{
    public static void WriteVarint(List<byte> buffer, ulong value)
    {
        while (value >= 0x80)
        {
            buffer.Add((byte)(value | 0x80));
            value >>= 7;
        }
        buffer.Add((byte)value);
    }

    public static void WriteTag(List<byte> buffer, int fieldNumber, WireType wireType)
    {
        WriteVarint(buffer, (ulong)((fieldNumber << 3) | (int)wireType));
    }

    public static void WriteString(List<byte> buffer, int fieldNumber, string value)
    {
        WriteTag(buffer, fieldNumber, WireType.LengthDelimited);
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteVarint(buffer, (ulong)bytes.Length);
        buffer.AddRange(bytes);
    }

    public static void WriteVarintField(List<byte> buffer, int fieldNumber, ulong value)
    {
        WriteTag(buffer, fieldNumber, WireType.Varint);
        WriteVarint(buffer, value);
    }
}
