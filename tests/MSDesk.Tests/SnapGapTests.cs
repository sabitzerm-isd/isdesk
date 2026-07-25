using static MSDesk.Interop.GridSnapBehavior;

namespace MSDesk.Tests;

/// <summary>
/// Einrasten mit definiertem Zwischenraum: Beim Verschieben soll ein Bereich in
/// genau dem eingestellten Abstand neben oder unter dem Nachbarn einrasten —
/// waagerecht wie senkrecht. So entstehen ueberall gleiche Abstaende, ohne von
/// Hand auszumessen.
/// </summary>
public class SnapGapTests
{
    private const int Grid = 10, Snap = 8, Reach = 8;
    private const int Gap = 23;   // ~6 mm bei 96 dpi

    /// Nachbar liegt links, Hoehe ueberlappt (gilt damit als benachbart).
    private static readonly Box Nachbar = new(100, 100, 400, 300);

    [Fact]
    public void RastetRechtsNebenDemNachbarnMitAbstandEin()
    {
        // Grob rechts daneben platziert — die exakte Lage waere 400 + 23 = 423.
        var me = new Box(420, 100, 620, 300);

        var (left, _) = ResolveMove(me, new[] { Nachbar }, Grid, Snap, Reach, Gap);

        Assert.Equal(Nachbar.R + Gap, left);
    }

    [Fact]
    public void RastetLinksNebenDemNachbarnMitAbstandEin()
    {
        // Mein rechter Rand soll mit Abstand links vom Nachbarn sitzen:
        // 100 - 23 - 200 = -123
        var me = new Box(-120, 100, 80, 300);

        var (left, _) = ResolveMove(me, new[] { Nachbar }, Grid, Snap, Reach, Gap);

        Assert.Equal(Nachbar.L - 200 - Gap, left);
    }

    [Fact]
    public void RastetUnterDemNachbarnMitAbstandEin()
    {
        // Senkrecht: Oberkante mit Abstand unter der Unterkante des Nachbarn.
        var me = new Box(100, 320, 400, 500);

        var (_, top) = ResolveMove(me, new[] { Nachbar }, Grid, Snap, Reach, Gap);

        Assert.Equal(Nachbar.B + Gap, top);
    }

    [Fact]
    public void RastetUeberDemNachbarnMitAbstandEin()
    {
        // Unterkante mit Abstand ueber der Oberkante: 100 - 23 - 180 = -103
        var me = new Box(100, -100, 400, 80);

        var (_, top) = ResolveMove(me, new[] { Nachbar }, Grid, Snap, Reach, Gap);

        Assert.Equal(Nachbar.T - 180 - Gap, top);
    }

    [Fact]
    public void OhneAbstand_RastetWeiterhinBuendigEin()
    {
        // Gap = 0 → Bereiche stossen aneinander (bisheriges Verhalten).
        var me = new Box(403, 100, 603, 300);

        var (left, _) = ResolveMove(me, new[] { Nachbar }, Grid, Snap, Reach, 0);

        Assert.Equal(Nachbar.R, left);
    }

    [Fact]
    public void KantenAusrichtenBleibtErhalten()
    {
        // Linksbuendig unter dem Nachbarn: X soll auf dessen linke Kante rasten.
        var me = new Box(103, 320, 303, 500);

        var (left, _) = ResolveMove(me, new[] { Nachbar }, Grid, Snap, Reach, Gap);

        Assert.Equal(Nachbar.L, left);
    }

    [Fact]
    public void UntereinanderMitGleichemAbstand_ErgibtGleichmaessigeReihe()
    {
        // Drei Bereiche untereinander: jeder rastet mit demselben Abstand ein.
        var oben = new Box(100, 100, 400, 200);
        var mitte = ResolveMove(new Box(100, 220, 400, 320), new[] { oben },
                                Grid, Snap, Reach, Gap);
        var mitteBox = new Box(100, mitte.Top, 400, mitte.Top + 100);

        var unten = ResolveMove(new Box(100, mitteBox.B + 20, 400, mitteBox.B + 120),
                                new[] { oben, mitteBox }, Grid, Snap, Reach, Gap);

        var abstand1 = mitteBox.T - oben.B;
        var abstand2 = unten.Top - mitteBox.B;

        Assert.Equal(Gap, abstand1);
        Assert.Equal(Gap, abstand2);
    }

    [Fact]
    public void EntfernterNachbar_RastetNicht()
    {
        // Zu weit weg (mehr als der Fangbereich) → keine Wirkung, nur Raster.
        var me = new Box(600, 100, 800, 300);

        var (left, _) = ResolveMove(me, new[] { Nachbar }, Grid, Snap, Reach, Gap);

        Assert.Equal(600, left); // liegt bereits auf dem Raster
    }
}
