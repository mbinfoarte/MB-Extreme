using MBExtreme.Core.Drivers;
using Xunit;

namespace MBExtreme.Core.Tests;

public class DriverTiposTests
{
    [Theory]
    [InlineData("Display", "Vídeo")]
    [InlineData("NVIDIA GeForce RTX 4060", "Vídeo")]
    [InlineData("Realtek High Definition Audio", "Som")]
    [InlineData("Intel(R) Wi-Fi 6 AX201", "Wi-Fi")]
    [InlineData("Realtek PCIe GbE Family Controller", "Rede")]
    [InlineData("Bluetooth", "Bluetooth")]
    [InlineData("AMD SMBus", "Chipset e sistema")]
    [InlineData("Qualquer coisa", "Outros")]
    public void Classifica(string text, string expected) => Assert.Equal(expected, DriverKinds.Of(text).Title);
}
