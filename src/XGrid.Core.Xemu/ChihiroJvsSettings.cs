namespace XGrid.Core.Xemu;

/// <summary>
/// Keyboard and mouse bindings for the Chihiro cabinet inputs. Values are SDL
/// scancodes, or 1000 plus SDL's button number for mouse buttons (1001 is
/// left, 1003 is right); 0 is unbound.
/// </summary>
public sealed class ChihiroJvsSettings
{
    /// <summary>Gets or sets the Start button.</summary>
    public int Start { get; set; } = 40;

    /// <summary>Gets or sets the Service button.</summary>
    public int Service { get; set; } = 15;

    /// <summary>Gets or sets the Coin input.</summary>
    public int Coin { get; set; } = 34;

    /// <summary>Gets or sets the Test button.</summary>
    public int Test { get; set; } = 14;

    /// <summary>Gets or sets the card-insert key.</summary>
    public int CardIn { get; set; } = 6;

    /// <summary>Gets or sets steering left.</summary>
    public int SteerLeft { get; set; } = 80;

    /// <summary>Gets or sets steering right.</summary>
    public int SteerRight { get; set; } = 79;

    /// <summary>Gets or sets the accelerator.</summary>
    public int Gas { get; set; } = 82;

    /// <summary>Gets or sets the brake.</summary>
    public int Brake { get; set; } = 81;

    /// <summary>Gets or sets gear up.</summary>
    public int GearUp { get; set; } = 48;

    /// <summary>Gets or sets gear down.</summary>
    public int GearDown { get; set; } = 47;

    /// <summary>Gets or sets The House of the Dead III's trigger.</summary>
    public int Hotd3Trigger { get; set; } = 1001;

    /// <summary>Gets or sets The House of the Dead III's body/action button.</summary>
    public int Hotd3Body { get; set; } = 1003;

    /// <summary>Gets or sets Virtua Cop 3's trigger.</summary>
    public int Vc3Trigger { get; set; } = 1001;

    /// <summary>Gets or sets Virtua Cop 3's body/action button.</summary>
    public int Vc3Body { get; set; } = 1003;

    /// <summary>Gets or sets Virtua Cop 3's pedal.</summary>
    public int Vc3Pedal { get; set; } = 44;

    /// <summary>Gets or sets Ghost Squad's trigger.</summary>
    public int GsTrigger { get; set; } = 1001;

    /// <summary>Gets or sets Ghost Squad's body/action button.</summary>
    public int GsBody { get; set; } = 1003;

    /// <summary>Gets or sets Ghost Squad's weapon-change button.</summary>
    public int GsChange { get; set; } = 44;

    /// <summary>Creates an independent copy of these bindings.</summary>
    /// <returns>The copy.</returns>
    public ChihiroJvsSettings Clone()
    {
        return (ChihiroJvsSettings)MemberwiseClone();
    }

    /// <summary>Returns the bindings as a flat list of values.</summary>
    /// <returns>Every binding in declaration order.</returns>
    public int[] ToArray()
    {
        return
        [
            Start,
            Service,
            Coin,
            Test,
            CardIn,
            SteerLeft,
            SteerRight,
            Gas,
            Brake,
            GearUp,
            GearDown,
            Hotd3Trigger,
            Hotd3Body,
            Vc3Trigger,
            Vc3Body,
            Vc3Pedal,
            GsTrigger,
            GsBody,
            GsChange,
        ];
    }

    /// <summary>Repairs values outside the keyboard and mouse ranges.</summary>
    public void Normalize()
    {
        Start = Clamp(Start);
        Service = Clamp(Service);
        Coin = Clamp(Coin);
        Test = Clamp(Test);
        CardIn = Clamp(CardIn);
        SteerLeft = Clamp(SteerLeft);
        SteerRight = Clamp(SteerRight);
        Gas = Clamp(Gas);
        Brake = Clamp(Brake);
        GearUp = Clamp(GearUp);
        GearDown = Clamp(GearDown);
        Hotd3Trigger = Clamp(Hotd3Trigger);
        Hotd3Body = Clamp(Hotd3Body);
        Vc3Trigger = Clamp(Vc3Trigger);
        Vc3Body = Clamp(Vc3Body);
        Vc3Pedal = Clamp(Vc3Pedal);
        GsTrigger = Clamp(GsTrigger);
        GsBody = Clamp(GsBody);
        GsChange = Clamp(GsChange);
    }

    /// <summary>Returns true for an unbound, keyboard or mouse binding.</summary>
    /// <param name="value">Candidate binding.</param>
    /// <returns>True when the core can use it.</returns>
    public static bool IsValid(int value)
    {
        return value == 0
               || value is >= 4 and < 512
               || value is >= 1001 and <= 1008;
    }

    private static int Clamp(int value)
    {
        return IsValid(value) ? value : 0;
    }
}
