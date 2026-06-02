using System.Linq;
using BigMac.Services;
using Xunit;

namespace BigMac.Tests;

public sealed class MacroDuplicatePlannerTests
{
    [Fact]
    public void CreateDuplicateName_SkipsExistingCopyVariants()
    {
        var duplicateName = MacroDuplicatePlanner.CreateDuplicateName(
            "Burst Heal",
            ["Burst Heal", "Burst Heal Copy", "Burst Heal Copy 2"]);

        Assert.Equal("Burst Heal Copy 3", duplicateName);
    }

    [Fact]
    public void CreateDuplicateName_ReusesOriginalBaseNameForCopiedMacros()
    {
        var duplicateName = MacroDuplicatePlanner.CreateDuplicateName(
            "Burst Heal Copy 2",
            ["Burst Heal", "Burst Heal Copy", "Burst Heal Copy 2"]);

        Assert.Equal("Burst Heal Copy 3", duplicateName);
    }

    [Fact]
    public void EnumerateCommandCandidates_IncrementsTrailingNumber()
    {
        var candidates = MacroDuplicatePlanner
            .EnumerateCommandCandidates("/burst2", maxCommandLength: 64)
            .Take(3)
            .ToArray();

        Assert.Equal(["/burst3", "/burst4", "/burst5"], candidates);
    }

    [Fact]
    public void EnumerateCommandCandidates_TruncatesLongCommandsToFit()
    {
        var sourceCommand = "/" + new string('a', 63);

        var candidate = MacroDuplicatePlanner
            .EnumerateCommandCandidates(sourceCommand, maxCommandLength: 64)
            .First();

        Assert.Equal(64, candidate.Length);
        Assert.EndsWith("2", candidate);
    }

    [Fact]
    public void TryFindDuplicateMacroLocation_PrefersNextFreeSlotInTheSameBook()
    {
        var foundLocation = MacroDuplicatePlanner.TryFindDuplicateMacroLocation(
            MacroDuplicatePlanner.IndividualBookId,
            5,
            [
                (MacroDuplicatePlanner.IndividualBookId, 5),
                (MacroDuplicatePlanner.IndividualBookId, 6),
                (MacroDuplicatePlanner.IndividualBookId, 7),
            ],
            out var macroBookId,
            out var macroSlot);

        Assert.True(foundLocation);
        Assert.Equal(MacroDuplicatePlanner.IndividualBookId, macroBookId);
        Assert.Equal(8, macroSlot);
    }

    [Fact]
    public void TryFindDuplicateMacroLocation_FallsBackToTheOtherBook()
    {
        var usedLocations = Enumerable.Range(1, MacroDuplicatePlanner.MaxMacroSlotsPerBook)
            .Select(static slot => (MacroDuplicatePlanner.IndividualBookId, slot))
            .ToArray();

        var foundLocation = MacroDuplicatePlanner.TryFindDuplicateMacroLocation(
            MacroDuplicatePlanner.IndividualBookId,
            99,
            usedLocations,
            out var macroBookId,
            out var macroSlot);

        Assert.True(foundLocation);
        Assert.Equal(MacroDuplicatePlanner.SharedBookId, macroBookId);
        Assert.Equal(99, macroSlot);
    }

    [Fact]
    public void TryFindDuplicateMacroLocation_ReturnsFalseWhenNoSlotIsAvailable()
    {
        var usedLocations = Enumerable.Range(1, MacroDuplicatePlanner.MaxMacroSlotsPerBook)
            .SelectMany(static slot => new[]
            {
                (MacroDuplicatePlanner.IndividualBookId, slot),
                (MacroDuplicatePlanner.SharedBookId, slot),
            })
            .ToArray();

        var foundLocation = MacroDuplicatePlanner.TryFindDuplicateMacroLocation(
            MacroDuplicatePlanner.IndividualBookId,
            1,
            usedLocations,
            out var macroBookId,
            out var macroSlot);

        Assert.False(foundLocation);
        Assert.Equal(MacroDuplicatePlanner.IndividualBookId, macroBookId);
        Assert.Equal(1, macroSlot);
    }
}
