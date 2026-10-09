namespace XGrid.Cpu;

/// <summary>GDTR/IDTR contents.</summary>
public sealed class DescriptorTableRegister
{
    /// <summary>Gets or sets the table base address.</summary>
    public uint Base { get; set; }

    /// <summary>Gets or sets the table limit in bytes.</summary>
    public ushort Limit { get; set; }
}
