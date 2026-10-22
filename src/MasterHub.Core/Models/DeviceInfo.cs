namespace MasterHub.Core.Models;

public enum MasterHubModuleType
{
    Unknown,
    BaseStation,    // MHBS01 (0x01F6)
    KeyDeck15,      // MHKD15 / M4315 (0x01FE)
    Fader5,         // MHFD05 / M0_Slider (0x0200)
    RollerDial,     // MHRR02 / M0_Wheel/Knob (0x0204)
    ScreenDisplay,  // M131 / M131E (0x131E / 0x1312)
    Knob3,          // M003K / MHKB03AA22 (0x0202) - 3 pokrętła z podświetleniem RGB
    DialDisplay     // M131E / MHDD01AA22 (0x0206) - 1 pokrętło z okrągłym ekranem IPS 240x240
}

public class DeviceInfo
{
    public MasterHubModuleType ModuleType { get; set; }
    public ushort VendorId { get; set; } = 0x2516;
    public ushort ProductId { get; set; }
    public string SerialNumber { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsConnected { get; set; }

    public override string ToString() => $"{Name} ({ModuleType}) [SN: {SerialNumber}]";
}
