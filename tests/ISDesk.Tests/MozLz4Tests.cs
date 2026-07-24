using System.IO;
using System.Text;
using ISDesk.Services;

namespace ISDesk.Tests;

/// Tests fuer den selbst gebauten LZ4-Block-Dekomprimierer (Firefox-Lesezeichen).
/// Die Eingaben sind von Hand nach der LZ4-Block-Spezifikation gebaut:
/// Token (obere 4 Bit Literal-Laenge, untere 4 Bit Match-Laenge minus 4),
/// danach Literale, dann 2-Byte-Rueckwaertsversatz.
public class MozLz4Tests
{
    [Fact]
    public void DecodeBlock_NurLiterale_GibtDenKlartextZurueck()
    {
        // Token 0x50 = 5 Literale, kein Match; danach "Hallo".
        var block = new byte[] { 0x50, (byte)'H', (byte)'a', (byte)'l', (byte)'l', (byte)'o' };

        var result = MozLz4.DecodeBlock(block, 5);

        Assert.Equal("Hallo", Encoding.ASCII.GetString(result));
    }

    [Fact]
    public void DecodeBlock_UeberlappenderMatch_WiederholtDasMuster()
    {
        // Token 0x35 = 3 Literale ("abc") + Match-Laenge 5+4 = 9, Versatz 3.
        // Erwartet: "abc" + 9 Bytes aus der eigenen Ausgabe = "abcabcabcabc".
        var block = new byte[] { 0x35, (byte)'a', (byte)'b', (byte)'c', 0x03, 0x00 };

        var result = MozLz4.DecodeBlock(block, 12);

        Assert.Equal("abcabcabcabc", Encoding.ASCII.GetString(result));
    }

    [Fact]
    public void DecodeBlock_LangeLiteralLaenge_LiestZusatzbytes()
    {
        // Literal-Laenge 20: Token 0xF0 (15) + Zusatzbyte 0x05.
        var text = "12345678901234567890"; // 20 Zeichen
        var block = new byte[] { 0xF0, 0x05 }
            .Concat(Encoding.ASCII.GetBytes(text)).ToArray();

        var result = MozLz4.DecodeBlock(block, 20);

        Assert.Equal(text, Encoding.ASCII.GetString(result));
    }

    [Fact]
    public void DecodeBlock_MatchGefolgtVonLiteralen_LiestBeides()
    {
        // 1. Sequenz: 4 Literale "ISDe", Match 4+0? -> Match-Laenge 4, Versatz 4
        //    ergibt "ISDe" + "ISDe" = "ISDeISDe"
        // 2. Sequenz: nur 2 Literale "sk"
        var block = new byte[] { 0x40, (byte)'I', (byte)'S', (byte)'D', (byte)'e', 0x04, 0x00,
                                 0x20, (byte)'s', (byte)'k' };

        var result = MozLz4.DecodeBlock(block, 10);

        Assert.Equal("ISDeISDesk", Encoding.ASCII.GetString(result));
    }

    [Fact]
    public void Decompress_MitMozLz4Kopf_EntpacktDieNutzdaten()
    {
        var block = new byte[] { 0x35, (byte)'a', (byte)'b', (byte)'c', 0x03, 0x00 };
        var file = new List<byte>();
        file.AddRange(Encoding.ASCII.GetBytes("mozLz40"));
        file.Add(0);
        file.AddRange(BitConverter.GetBytes(12)); // Ziel-Laenge, little endian
        file.AddRange(block);

        Assert.True(MozLz4.HasMagic(file.ToArray()));
        Assert.Equal("abcabcabcabc", Encoding.ASCII.GetString(MozLz4.Decompress(file.ToArray())));
    }

    [Fact]
    public void Decompress_OhneMagic_Meldet()
    {
        var file = Encoding.ASCII.GetBytes("{\"children\":[]}");
        Assert.False(MozLz4.HasMagic(file));
        Assert.Throws<InvalidDataException>(() => MozLz4.Decompress(file));
    }

    [Fact]
    public void DecodeBlock_BeschaedigterVersatz_WirftStattFalschZuEntpacken()
    {
        // Versatz 9 zeigt vor den Anfang der Ausgabe.
        var block = new byte[] { 0x15, (byte)'a', 0x09, 0x00 };
        Assert.Throws<InvalidDataException>(() => MozLz4.DecodeBlock(block, 10));
    }
}
