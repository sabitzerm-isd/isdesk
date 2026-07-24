using System.IO;

namespace ISDesk.Services;

/// Entpackt Dateien im Firefox-Format "mozLz4" — so werden u. a. die
/// Lesezeichen-Sicherungen (bookmarkbackups\*.jsonlz4) abgelegt.
/// Aufbau: Magic "mozLz40\0" + 4 Byte Ziel-Laenge (little endian) + LZ4-Block.
/// Der LZ4-Block-Dekomprimierer ist bewusst selbst implementiert, damit ISDesk
/// ohne zusaetzliches NuGet-Paket auskommt (das Format ist sehr einfach).
public static class MozLz4
{
    private static readonly byte[] MagicBytes = { (byte)'m', (byte)'o', (byte)'z', (byte)'L',
                                                  (byte)'z', (byte)'4', (byte)'0', 0 };

    public const int HeaderLength = 12; // Magic (8) + Laenge (4)

    /// True, wenn die Datei mit dem mozLz4-Magic beginnt.
    public static bool HasMagic(ReadOnlySpan<byte> file)
    {
        if (file.Length < MagicBytes.Length) return false;
        for (var i = 0; i < MagicBytes.Length; i++)
            if (file[i] != MagicBytes[i]) return false;
        return true;
    }

    /// Entpackt eine komplette mozLz4-Datei (Magic + Laenge + LZ4-Block).
    public static byte[] Decompress(ReadOnlySpan<byte> file)
    {
        if (!HasMagic(file))
            throw new InvalidDataException("Keine mozLz4-Datei (Magic fehlt).");
        if (file.Length < HeaderLength)
            throw new InvalidDataException("mozLz4-Datei ist unvollstaendig.");

        var size = file[8] | (file[9] << 8) | (file[10] << 16) | (file[11] << 24);
        if (size < 0) throw new InvalidDataException("Ungueltige Laengenangabe.");
        return DecodeBlock(file[HeaderLength..], size);
    }

    /// Dekomprimiert einen rohen LZ4-Block.
    /// Aufbau je Sequenz: Token-Byte (obere 4 Bit = Literal-Laenge,
    /// untere 4 Bit = Match-Laenge minus 4), danach ggf. Laengen-Zusatzbytes,
    /// die Literale selbst, dann ein 2-Byte-Rueckwaertsversatz und ggf. weitere
    /// Laengen-Zusatzbytes fuer den Match. Der letzte Block endet nach Literalen.
    public static byte[] DecodeBlock(ReadOnlySpan<byte> source, int uncompressedSize)
    {
        if (uncompressedSize < 0) throw new ArgumentOutOfRangeException(nameof(uncompressedSize));

        var target = new byte[uncompressedSize];
        int ip = 0, op = 0;

        while (ip < source.Length)
        {
            int token = source[ip++];

            // 1. Literale unveraendert uebernehmen
            var literals = token >> 4;
            if (literals == 15) literals += ReadExtraLength(source, ref ip);
            if (literals > 0)
            {
                if (ip + literals > source.Length || op + literals > target.Length)
                    throw new InvalidDataException("LZ4-Block beschaedigt (Literale).");
                source.Slice(ip, literals).CopyTo(target.AsSpan(op));
                ip += literals;
                op += literals;
            }

            // Die letzte Sequenz besteht nur aus Literalen.
            if (ip >= source.Length) break;

            // 2. Rueckwaerts-Referenz (Versatz 2 Byte, little endian)
            if (ip + 1 >= source.Length)
                throw new InvalidDataException("LZ4-Block beschaedigt (Versatz).");
            var offset = source[ip] | (source[ip + 1] << 8);
            ip += 2;
            if (offset <= 0 || offset > op)
                throw new InvalidDataException("LZ4-Block beschaedigt (Versatz ausserhalb).");

            var matchLength = token & 0x0F;
            if (matchLength == 15) matchLength += ReadExtraLength(source, ref ip);
            matchLength += 4; // Mindestlaenge eines Matches
            if (op + matchLength > target.Length)
                throw new InvalidDataException("LZ4-Block beschaedigt (Match zu lang).");

            // Byteweise kopieren — Quelle und Ziel duerfen sich ueberlappen.
            var from = op - offset;
            for (var i = 0; i < matchLength; i++)
                target[op++] = target[from++];
        }

        if (op != target.Length)
            throw new InvalidDataException($"LZ4-Block unvollstaendig ({op} von {target.Length} Bytes).");
        return target;
    }

    /// Laengen-Zusatzbytes: 255 bedeutet "weiterlesen", das erste kleinere Byte beendet.
    private static int ReadExtraLength(ReadOnlySpan<byte> source, ref int ip)
    {
        var extra = 0;
        while (true)
        {
            if (ip >= source.Length)
                throw new InvalidDataException("LZ4-Block beschaedigt (Laengenangabe).");
            var b = source[ip++];
            extra += b;
            if (b != 255) return extra;
        }
    }
}
