using ISDesk.Interop;
using Box = ISDesk.Interop.GridSnapBehavior.Box;

namespace ISDesk.Tests;

/// Raster und Kanten-Einrasten (die reine Rechnung hinter WM_MOVING/WM_SIZING).
public class GridSnapBehaviorTests
{
    private const int Grid = 20, Snap = 12, Reach = 80;

    private static readonly IReadOnlyList<Box> Keiner = Array.Empty<Box>();

    [Fact]
    public void OhneNachbarn_RastetAufDasRaster()
    {
        var me = new Box(107, 213, 507, 473); // 400 x 260

        var (left, top) = GridSnapBehavior.ResolveMove(me, Keiner, Grid, Snap, Reach);

        Assert.Equal(100, left);
        Assert.Equal(220, top);
    }

    [Fact]
    public void LinkeKanteFaengtSichAnDerRechtenKanteDesNachbarn()
    {
        var neighbour = new Box(100, 100, 500, 360);
        var me = new Box(507, 104, 907, 364); // 7 px neben dem Nachbarn

        var (left, top) = GridSnapBehavior.ResolveMove(me, new[] { neighbour }, Grid, Snap, Reach);

        Assert.Equal(500, left);  // lueckenlos angedockt (Raster haette 500 auch getroffen)
        Assert.Equal(100, top);   // oben buendig (statt Raster-100? beides 100 → egal)
    }

    [Fact]
    public void KantenEinrastenSchlaegtDasRaster()
    {
        // Nachbar endet bei 503 — das Raster wuerde auf 500 ziehen, das
        // Kanten-Einrasten gewinnt und dockt exakt an 503 an.
        var neighbour = new Box(103, 100, 503, 360);
        var me = new Box(508, 100, 908, 360);

        var (left, _) = GridSnapBehavior.ResolveMove(me, new[] { neighbour }, Grid, Snap, Reach);

        Assert.Equal(503, left);
    }

    [Fact]
    public void BuendigesAusrichtenLinksAnLinks()
    {
        // Bereich soll UNTER den Nachbarn — linke Kanten sollen fluchten.
        var neighbour = new Box(300, 100, 700, 360);
        var me = new Box(306, 366, 606, 566); // 6 px daneben, direkt darunter

        var (left, top) = GridSnapBehavior.ResolveMove(me, new[] { neighbour }, Grid, Snap, Reach);

        Assert.Equal(300, left); // linksbuendig
        Assert.Equal(360, top);  // Oberkante an Unterkante des Nachbarn
    }

    [Fact]
    public void RechtsbuendigesAusrichten()
    {
        var neighbour = new Box(300, 100, 700, 360);
        var me = new Box(404, 400, 704, 600); // rechte Kante 704, Ziel 700

        var (left, _) = GridSnapBehavior.ResolveMove(me, new[] { neighbour }, Grid, Snap, Reach);

        Assert.Equal(400, left); // 400 + 300 Breite = 700 → rechtsbuendig
    }

    [Fact]
    public void WeitEntfernteBereicheZiehenNicht()
    {
        // Nachbar liegt 500 px weiter unten → seine Kanten gelten nicht mehr.
        var neighbour = new Box(503, 900, 903, 1160);
        var me = new Box(515, 100, 915, 360); // 12 px von 503 entfernt

        var (left, _) = GridSnapBehavior.ResolveMove(me, new[] { neighbour }, Grid, Snap, Reach);

        Assert.Equal(520, left); // nur Raster — nicht 503

        // Derselbe Nachbar direkt daneben faengt die Kante dagegen ein.
        var near = new Box(503, 100, 903, 360);
        var (leftNear, _) = GridSnapBehavior.ResolveMove(me, new[] { near }, Grid, Snap, Reach);
        Assert.Equal(503, leftNear);
    }

    [Fact]
    public void GroesseZiehen_RechteKanteRastetAmNachbarnEin()
    {
        const int WMSZ_RIGHT = 2;
        var neighbour = new Box(603, 100, 1000, 360);
        var me = new Box(100, 100, 597, 360);

        var result = GridSnapBehavior.ResolveResize(me, WMSZ_RIGHT, new[] { neighbour },
            Grid, Snap, Reach, 180, 120);

        Assert.Equal(603, result.R); // exakt an die Kante des Nachbarn
        Assert.Equal(100, result.L); // unveraendert
        Assert.Equal(100, result.T);
        Assert.Equal(360, result.B);
    }

    [Fact]
    public void GroesseZiehen_OhneNachbarnAufsRasterUndNieUnterMindestgroesse()
    {
        const int WMSZ_BOTTOMRIGHT = 8;
        var me = new Box(100, 100, 147, 133);

        var result = GridSnapBehavior.ResolveResize(me, WMSZ_BOTTOMRIGHT, Keiner,
            Grid, Snap, Reach, 180, 120);

        Assert.Equal(280, result.R); // Mindestbreite 180
        Assert.Equal(220, result.B); // Mindesthoehe 120
    }

    [Theory]
    [InlineData(0, 20, 0)]
    [InlineData(9, 20, 0)]
    [InlineData(11, 20, 20)]
    [InlineData(-9, 20, 0)]
    [InlineData(-11, 20, -20)]
    [InlineData(37, 10, 40)]
    public void RasterRundetZurNaechstenMarke(int value, int grid, int expected)
        => Assert.Equal(expected, GridSnapBehavior.SnapToGrid(value, grid));

    [Fact]
    public void SnapEdge_NimmtDenNaechstenKandidaten()
    {
        Assert.Equal(505, GridSnapBehavior.SnapEdge(500, new[] { 520, 505, 490 }, 12));
        Assert.Null(GridSnapBehavior.SnapEdge(500, new[] { 520, 480 }, 12));
    }
}

public class GridSnapSizeMatchTests
{
    private static readonly IReadOnlyList<GridSnapBehavior.Box> Empty = new List<GridSnapBehavior.Box>();

    [Fact]
    public void RechteKante_rastet_auf_Breite_des_Nachbarn()
    {
        // Nachbar rechts daneben, 400 breit; meiner startet bei 100 und wird auf ~495 gezogen
        var neighbour = new GridSnapBehavior.Box(600, 0, 1000, 200); // Breite 400
        var me = new GridSnapBehavior.Box(100, 0, 495, 200);         // aktuell 395 breit

        var result = GridSnapBehavior.ResolveResize(me, edge: 2 /* WMSZ_RIGHT */,
            new[] { neighbour }, gridPx: 20, snapPx: 8, reachPx: 8, minWidth: 180, minHeight: 120);

        // 100 + 400 = 500 liegt 5 px entfernt -> muss einrasten (gleiche Breite wie Nachbar)
        Assert.Equal(500, result.R);
        Assert.Equal(400, result.Width);
    }

    [Fact]
    public void Untere_Kante_rastet_auf_Hoehe_des_Nachbarn()
    {
        var neighbour = new GridSnapBehavior.Box(0, 300, 200, 500); // Hoehe 200
        var me = new GridSnapBehavior.Box(0, 0, 200, 195);          // aktuell 195 hoch

        var result = GridSnapBehavior.ResolveResize(me, edge: 6 /* WMSZ_BOTTOM */,
            new[] { neighbour }, gridPx: 20, snapPx: 8, reachPx: 8, minWidth: 180, minHeight: 120);

        Assert.Equal(200, result.Height);
    }

    [Fact]
    public void Ohne_Nachbarn_gilt_weiterhin_das_Raster()
    {
        var me = new GridSnapBehavior.Box(0, 0, 393, 200);

        var result = GridSnapBehavior.ResolveResize(me, edge: 2, Empty,
            gridPx: 20, snapPx: 8, reachPx: 8, minWidth: 180, minHeight: 120);

        Assert.Equal(400, result.R); // 393 -> naechstes Vielfaches von 20
    }
}
