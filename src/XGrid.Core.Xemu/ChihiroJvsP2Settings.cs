using System;

namespace XGrid.Core.Xemu;

/// <summary>
/// Keyboard, mouse and aim bindings for the second player on a Chihiro gun
/// cabinet. Values are SDL scancodes, or 1000 plus SDL's button number for
/// mouse buttons (1001 is left, 1003 is right); 0 is unbound. The second
/// gamepad's right stick also aims, and its buttons drive the gun even when
/// these bindings are empty.
/// </summary>
public sealed class ChihiroJvsP2Settings
{
    /// <summary>Gets or sets player 2's Start button.</summary>
    public int Start { get; set; } = 88;

    /// <summary>Gets or sets player 2's Coin input.</summary>
    public int Coin { get; set; } = 90;

    /// <summary>Gets or sets player 2's card-insert key.</summary>
    public int CardIn { get; set; } = 25;

    /// <summary>Gets or sets the aim key that moves the crosshair left.</summary>
    public int AimLeft { get; set; } = 80;

    /// <summary>Gets or sets the aim key that moves the crosshair right.</summary>
    public int AimRight { get; set; } = 79;

    /// <summary>Gets or sets the aim key that moves the crosshair up.</summary>
    public int AimUp { get; set; } = 82;

    /// <summary>Gets or sets the aim key that moves the crosshair down.</summary>
    public int AimDown { get; set; } = 81;

    /// <summary>
    /// Gets or sets the crosshair speed in percent. 100 crosses the picture
    /// in about 0.75 seconds with a full stick or held aim key.
    /// </summary>
    public int AimSpeed { get; set; } = 100;

    /// <summary>Gets or sets The House of the Dead III's trigger.</summary>
    public int Hotd3Trigger { get; set; }

    /// <summary>Gets or sets The House of the Dead III's body/action button.</summary>
    public int Hotd3Body { get; set; }

    /// <summary>Gets or sets Virtua Cop 3's trigger.</summary>
    public int Vc3Trigger { get; set; }

    /// <summary>Gets or sets Virtua Cop 3's body/action button.</summary>
    public int Vc3Body { get; set; }

    /// <summary>Gets or sets Virtua Cop 3's pedal.</summary>
    public int Vc3Pedal { get; set; }

    /// <summary>Gets or sets Virtua Cop 3's reload button.</summary>
    public int Vc3Reload { get; set; }

    /// <summary>Gets or sets Ghost Squad's trigger.</summary>
    public int GsTrigger { get; set; }

    /// <summary>Gets or sets Ghost Squad's body/action button.</summary>
    public int GsBody { get; set; }

    /// <summary>Gets or sets Ghost Squad's weapon-change button.</summary>
    public int GsChange { get; set; }

    /// <summary>Gets or sets Ghost Squad's reload button.</summary>
    public int GsReload { get; set; }

    /// <summary>Creates an independent copy of these bindings.</summary>
    /// <returns>The copy.</returns>
    public ChihiroJvsP2Settings Clone()
    {
        return (ChihiroJvsP2Settings)MemberwiseClone();
    }

    /// <summary>Returns the bindings as a flat list of values.</summary>
    /// <returns>Every binding in declaration order.</returns>
    public int[] ToArray()
    {
        return
        [
            Start,
            Coin,
            CardIn,
            AimLeft,
            AimRight,
            AimUp,
            AimDown,
            Hotd3Trigger,
            Hotd3Body,
            Vc3Trigger,
            Vc3Body,
            Vc3Pedal,
            Vc3Reload,
            GsTrigger,
            GsBody,
            GsChange,
            GsReload,
        ];
    }

    /// <summary>Repairs values outside the keyboard and mouse ranges.</summary>
    public void Normalize()
    {
        Start = Clamp(Start);
        Coin = Clamp(Coin);
        CardIn = Clamp(CardIn);
        AimLeft = Clamp(AimLeft);
        AimRight = Clamp(AimRight);
        AimUp = Clamp(AimUp);
        AimDown = Clamp(AimDown);
        Hotd3Trigger = Clamp(Hotd3Trigger);
        Hotd3Body = Clamp(Hotd3Body);
        Vc3Trigger = Clamp(Vc3Trigger);
        Vc3Body = Clamp(Vc3Body);
        Vc3Pedal = Clamp(Vc3Pedal);
        Vc3Reload = Clamp(Vc3Reload);
        GsTrigger = Clamp(GsTrigger);
        GsBody = Clamp(GsBody);
        GsChange = Clamp(GsChange);
        GsReload = Clamp(GsReload);
        AimSpeed = Math.Clamp(AimSpeed, 0, 400);
    }

    private static int Clamp(int value)
    {
        return ChihiroJvsSettings.IsValid(value) ? value : 0;
    }
}
