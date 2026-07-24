using MSDesk.Interop;
using static MSDesk.Interop.GridSnapBehavior;

namespace MSDesk.Tests;

/// Hilfslinien beim Verschieben: sie muessen genau dort liegen, wo eingerastet wird.
public class AlignmentGuideTests
{
    private const int Grid = 10, Snap = 8, Reach = 8;

    [Fact]
    public void KeinNachbar_KeineLinien()
    {
        var me = new Box(100, 100, 300, 250);
        var (_, _, guides) = ResolveMoveWithGuides(me, Array.Empty<Box>(), Grid, Snap, Reach);

        // Reines Raster-Einrasten bezieht sich auf keinen Nachbarn → nichts anzuzeigen.
        Assert.Empty(guides);
    }

    [Fact]
    public void AndockenAnRechteKante_LiefertSenkrechteLinieDort()
    {
        var nachbar = new Box(0, 100, 200, 300);
        var me = new Box(203, 100, 403, 300); // 3 px neben der rechten Kante des Nachbarn

        var (left, _, guides) = ResolveMoveWithGuides(me, new[] { nachbar }, Grid, Snap, Reach);

        Assert.Equal(200, left); // buendig angedockt
        var linie = Assert.Single(guides.Where(g => g.Vertical));
        Assert.Equal(200, linie.Position);
    }

    [Fact]
    public void LinieUeberspanntBeideBereiche()
    {
        var nachbar = new Box(0, 50, 200, 150);
        var me = new Box(203, 100, 403, 400);

        var (_, _, guides) = ResolveMoveWithGuides(me, new[] { nachbar }, Grid, Snap, Reach);

        var linie = Assert.Single(guides.Where(g => g.Vertical));
        Assert.Equal(50, linie.From);   // oberer Rand des Nachbarn
        Assert.Equal(400, linie.To);    // unterer Rand des gezogenen Bereichs
    }

    [Fact]
    public void ObenBuendig_LiefertWaagerechteLinie()
    {
        // Innerhalb der Nachbar-Reichweite (8 px), sonst gilt er nicht als Nachbar.
        var nachbar = new Box(0, 100, 200, 300);
        var me = new Box(206, 104, 406, 304); // 4 px unter der Oberkante des Nachbarn

        var (_, top, guides) = ResolveMoveWithGuides(me, new[] { nachbar }, Grid, Snap, Reach);

        Assert.Equal(100, top);
        var linie = Assert.Single(guides.Where(g => !g.Vertical));
        Assert.Equal(100, linie.Position);
    }

    [Fact]
    public void RastetInBeidenRichtungen_LiefertZweiLinien()
    {
        var nachbar = new Box(0, 100, 200, 300);
        var me = new Box(203, 103, 403, 303); // knapp neben UND knapp unter der Ecke

        var (left, top, guides) = ResolveMoveWithGuides(me, new[] { nachbar }, Grid, Snap, Reach);

        Assert.Equal(200, left);
        Assert.Equal(100, top);
        Assert.Equal(2, guides.Count);
        Assert.Contains(guides, g => g.Vertical);
        Assert.Contains(guides, g => !g.Vertical);
    }

    [Fact]
    public void ErgebnisGleichtDerBisherigenRechnung()
    {
        // Die Linien duerfen das Einrasten selbst nicht veraendern.
        var nachbar = new Box(0, 100, 200, 300);
        var me = new Box(205, 104, 405, 304);

        var ohne = ResolveMove(me, new[] { nachbar }, Grid, Snap, Reach);
        var (left, top, _) = ResolveMoveWithGuides(me, new[] { nachbar }, Grid, Snap, Reach);

        Assert.Equal(ohne.Left, left);
        Assert.Equal(ohne.Top, top);
    }
}
