using MasterHub.Core.Devices;

namespace MasterHub.Core.Tests;

/// <summary>T-17 (a): the arithmetic of the deck brightness keys.</summary>
public class DeckBrightnessTests
{
    [Theory]
    [InlineData(50, 10, 60)]
    [InlineData(95, 10, 100)]   // never above 100
    [InlineData(100, 10, 100)]
    [InlineData(0, 10, 11)]     // a stored 0 is lifted to the minimum first
    public void Up_StopsAtTheTop(int current, int step, int expected) =>
        Assert.Equal(expected, DeckBrightness.Next(current, BrightnessCommand.Up, step));

    [Theory]
    [InlineData(50, 10, 40)]
    [InlineData(5, 10, 1)]
    [InlineData(1, 10, 1)]    // never below 1: 0 would switch the screen's controller off
    [InlineData(0, 10, 1)]
    public void Down_NeverGoesBelowOne(int current, int step, int expected) =>
        Assert.Equal(expected, DeckBrightness.Next(current, BrightnessCommand.Down, step));

    [Theory]
    [InlineData(100, 75)]
    [InlineData(80, 75)]    // from between two levels: the next one below
    [InlineData(75, 50)]
    [InlineData(50, 25)]
    [InlineData(25, 10)]
    [InlineData(10, 100)]   // at the lowest level it starts again at the top
    [InlineData(3, 100)]
    public void Cycle_StepsDownThroughTheLevels_AndWraps(int current, int expected) =>
        Assert.Equal(expected, DeckBrightness.Next(current, BrightnessCommand.Cycle));

    [Fact]
    public void RepeatedCycling_VisitsEveryLevelAndComesBack()
    {
        int b = 100;
        var seen = new List<int>();
        for (int i = 0; i < DeckBrightness.CycleLevels.Length; i++) { b = DeckBrightness.Next(b, BrightnessCommand.Cycle); seen.Add(b); }

        Assert.Equal(new[] { 75, 50, 25, 10, 100 }, seen);
    }

    [Theory]
    [InlineData("", 10)]
    [InlineData(null, 10)]
    [InlineData("5", 5)]
    [InlineData("20%", 20)]
    [InlineData(" 15 ", 15)]
    [InlineData("0", 10)]     // out of range: the default
    [InlineData("99", 10)]
    [InlineData("abc", 10)]
    public void TheStep_ComesFromTheParameter(string? parameter, int expected) =>
        Assert.Equal(expected, DeckBrightness.ParseStep(parameter));

    [Theory]
    [InlineData(-50)]
    [InlineData(0)]
    [InlineData(300)]
    public void WhateverGoesIn_TheResultStaysInRange(int current)
    {
        foreach (var command in Enum.GetValues<BrightnessCommand>())
        {
            int result = DeckBrightness.Next(current, command, 999);
            Assert.InRange(result, DeckBrightness.Min, DeckBrightness.Max);
        }
    }
}
