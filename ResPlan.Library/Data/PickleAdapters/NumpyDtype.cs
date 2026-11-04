using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Pickle.Net;

namespace ResPlan.Library.Data.PickleAdapters;

/// <summary>Decodes scalar dtype buffers, without importing NumPy or interpreting executable code.</summary>
internal sealed class NumpyDtype
{
    private readonly char kind;
    private readonly int size;
    private char byteOrder;

    private NumpyDtype(string descriptor)
    {
        if (string.IsNullOrEmpty(descriptor)) throw new UnpicklingException("NumPy dtype is empty.");
        byteOrder = '=';
        if (descriptor[0] is '<' or '>' or '=' or '|')
        {
            byteOrder = descriptor[0];
            descriptor = descriptor[1..];
        }
        descriptor = descriptor switch
        {
            "bool" or "bool_" or "?" => "b1",
            "int8" => "i1", "int16" => "i2", "int32" => "i4", "int64" => "i8",
            "uint8" => "u1", "uint16" => "u2", "uint32" => "u4", "uint64" => "u8",
            "float16" => "f2", "float32" => "f4", "float64" => "f8",
            _ => descriptor
        };
        if (descriptor.Length < 2 || !int.TryParse(descriptor.AsSpan(1), NumberStyles.None,
                CultureInfo.InvariantCulture, out var elements) || elements < 0)
            throw new UnpicklingException($"Unsupported NumPy dtype '{descriptor}'.");
        kind = descriptor[0];
        if (!(kind is 'i' or 'u' && elements is 1 or 2 or 4 or 8) &&
            !(kind == 'f' && elements is 2 or 4 or 8) && !(kind == 'b' && elements == 1) &&
            kind is not ('S' or 'U'))
            throw new UnpicklingException($"Unsupported NumPy dtype '{descriptor}'; only scalar numbers, booleans and strings are supported.");
        try { size = kind == 'U' ? checked(elements * 4) : elements; }
        catch (OverflowException exception) { throw new UnpicklingException("NumPy dtype size is too large.", exception); }
    }

    internal static NumpyDtype Create(PickleTuple arguments, PickleDictionary? keywords)
    {
        ResPlanPickleRegistry.RequireArguments(arguments, keywords, 1, 3, "numpy.dtype");
        if (arguments[0] is not string descriptor)
            throw new UnpicklingException("Structured NumPy dtypes are not supported by the ResPlan reader.");
        return new NumpyDtype(descriptor);
    }

    internal void ApplyState(object? value)
    {
        if (value is not PickleTuple state || state.Count < 5 || state[1] is not string { Length: 1 } order ||
            order[0] is not ('<' or '>' or '=' or '|'))
            throw new UnpicklingException("NumPy dtype has an invalid state or byte order.");
        if (state[2] is not null || state[3] is not null || state[4] is not null)
            throw new UnpicklingException("Structured and subarray NumPy dtypes are not supported by the ResPlan reader.");
        byteOrder = order[0];
    }

    internal static object ReadScalar(PickleTuple arguments, PickleDictionary? keywords)
    {
        ResPlanPickleRegistry.RequireArguments(arguments, keywords, 2, 2, "numpy scalar");
        if (arguments[0] is not NumpyDtype dtype)
            throw new UnpicklingException("NumPy scalar requires a registered dtype.");
        return dtype.Decode(ResPlanPickleRegistry.Bytes(arguments[1], "NumPy scalar"));
    }

    private object Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != size)
            throw new UnpicklingException($"NumPy scalar expected {size} bytes, received {bytes.Length}.");
        var littleEndian = byteOrder == '<' || byteOrder is '=' or '|' && BitConverter.IsLittleEndian;
        if (kind == 'S') return bytes[..TrimNuls(bytes)].ToArray();
        if (kind == 'U')
        {
            var encoding = new UTF32Encoding(!littleEndian, false, true);
            try { return encoding.GetString(bytes).TrimEnd('\0'); }
            catch (DecoderFallbackException exception) { throw new UnpicklingException("NumPy unicode scalar contains invalid UTF-32.", exception); }
        }
        if (kind == 'b') return bytes[0] != 0;

        ulong bits = size switch
        {
            1 => bytes[0],
            2 => littleEndian ? BinaryPrimitives.ReadUInt16LittleEndian(bytes) : BinaryPrimitives.ReadUInt16BigEndian(bytes),
            4 => littleEndian ? BinaryPrimitives.ReadUInt32LittleEndian(bytes) : BinaryPrimitives.ReadUInt32BigEndian(bytes),
            8 => littleEndian ? BinaryPrimitives.ReadUInt64LittleEndian(bytes) : BinaryPrimitives.ReadUInt64BigEndian(bytes),
            _ => throw new UnpicklingException("Unsupported NumPy numeric size.")
        };
        if (kind == 'u') return bits;
        if (kind == 'i') return size switch
        {
            1 => (long)unchecked((sbyte)bits),
            2 => (long)unchecked((short)bits),
            4 => (long)unchecked((int)bits),
            _ => unchecked((long)bits)
        };
        return size switch
        {
            // Box before the switch's common-type inference can widen these
            // values: NumPy scalar string representations depend on precision.
            2 => (object)BitConverter.UInt16BitsToHalf((ushort)bits),
            4 => (object)BitConverter.Int32BitsToSingle(unchecked((int)bits)),
            _ => BitConverter.Int64BitsToDouble(unchecked((long)bits))
        };
    }

    private static int TrimNuls(ReadOnlySpan<byte> bytes)
    {
        var length = bytes.Length;
        while (length > 0 && bytes[length - 1] == 0) length--;
        return length;
    }
}
