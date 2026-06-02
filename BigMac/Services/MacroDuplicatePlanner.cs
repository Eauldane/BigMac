using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BigMac.Services;

public static class MacroDuplicatePlanner
{
    public const int IndividualBookId = 0;
    public const int SharedBookId = 1;
    public const int MaxMacroSlotsPerBook = 100;

    public static string CreateDuplicateName(string? sourceName, IEnumerable<string> usedNames)
    {
        var baseName = StripDuplicateSuffix(sourceName);
        var existingNames = new HashSet<string>(
            usedNames
                .Where(static name => !string.IsNullOrWhiteSpace(name))
                .Select(static name => name.Trim()),
            StringComparer.OrdinalIgnoreCase);

        var candidate = $"{baseName} Copy";
        if (!existingNames.Contains(candidate))
        {
            return candidate;
        }

        for (var index = 2; index < int.MaxValue; index++)
        {
            candidate = $"{baseName} Copy {index.ToString(CultureInfo.InvariantCulture)}";
            if (!existingNames.Contains(candidate))
            {
                return candidate;
            }
        }

        return $"{baseName} Copy";
    }

    public static IEnumerable<string> EnumerateCommandCandidates(string? sourceCommand, int maxCommandLength)
    {
        var (baseBody, firstSuffix) = ParseCommandBase(sourceCommand);
        maxCommandLength = Math.Max(maxCommandLength, 3);

        for (var suffix = firstSuffix; suffix < int.MaxValue; suffix++)
        {
            var suffixText = suffix.ToString(CultureInfo.InvariantCulture);
            var maxBodyLength = Math.Max(maxCommandLength - 1 - suffixText.Length, 1);
            var candidateBody = baseBody.Length > maxBodyLength
                ? baseBody[..maxBodyLength]
                : baseBody;
            yield return "/" + candidateBody + suffixText;
        }
    }

    public static bool TryFindDuplicateMacroLocation(
        int sourceBookId,
        int sourceSlot,
        IEnumerable<(int MacroBookId, int Slot)> usedLocations,
        out int macroBookId,
        out int macroSlot)
    {
        sourceBookId = NormalizeBookId(sourceBookId);
        sourceSlot = Math.Clamp(sourceSlot, 1, MaxMacroSlotsPerBook);

        var occupiedLocations = new HashSet<int>();
        foreach (var (usedBookId, usedSlot) in usedLocations)
        {
            occupiedLocations.Add(ToLocationKey(NormalizeBookId(usedBookId), Math.Clamp(usedSlot, 1, MaxMacroSlotsPerBook)));
        }

        for (var offset = 1; offset < MaxMacroSlotsPerBook; offset++)
        {
            var candidateSlot = WrapSlot(sourceSlot + offset);
            if (occupiedLocations.Contains(ToLocationKey(sourceBookId, candidateSlot)))
            {
                continue;
            }

            macroBookId = sourceBookId;
            macroSlot = candidateSlot;
            return true;
        }

        var fallbackBookId = sourceBookId == IndividualBookId ? SharedBookId : IndividualBookId;
        for (var offset = 0; offset < MaxMacroSlotsPerBook; offset++)
        {
            var candidateSlot = WrapSlot(sourceSlot + offset);
            if (occupiedLocations.Contains(ToLocationKey(fallbackBookId, candidateSlot)))
            {
                continue;
            }

            macroBookId = fallbackBookId;
            macroSlot = candidateSlot;
            return true;
        }

        macroBookId = sourceBookId;
        macroSlot = sourceSlot;
        return false;
    }

    private static string StripDuplicateSuffix(string? sourceName)
    {
        var trimmed = sourceName?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return "New Macro";
        }

        var copyIndex = trimmed.LastIndexOf(" Copy", StringComparison.OrdinalIgnoreCase);
        if (copyIndex <= 0)
        {
            return trimmed;
        }

        var suffix = trimmed[(copyIndex + " Copy".Length)..];
        if (suffix.Length == 0)
        {
            return trimmed[..copyIndex].TrimEnd();
        }

        if (suffix[0] == ' ' && suffix.Length > 1 && suffix[1..].All(char.IsDigit))
        {
            return trimmed[..copyIndex].TrimEnd();
        }

        return trimmed;
    }

    private static (string BaseBody, int FirstSuffix) ParseCommandBase(string? sourceCommand)
    {
        var trimmed = sourceCommand?.Trim() ?? string.Empty;
        if (trimmed.StartsWith('/'))
        {
            trimmed = trimmed[1..];
        }

        var normalizedBody = string.Concat(trimmed.Where(static character => !char.IsWhiteSpace(character))).ToLowerInvariant();
        if (normalizedBody.Length == 0)
        {
            return ("newmacro", 2);
        }

        var digitIndex = normalizedBody.Length;
        while (digitIndex > 0 && char.IsDigit(normalizedBody[digitIndex - 1]))
        {
            digitIndex--;
        }

        if (digitIndex == normalizedBody.Length)
        {
            return (normalizedBody, 2);
        }

        var baseBody = normalizedBody[..digitIndex];
        if (baseBody.Length == 0)
        {
            baseBody = "macro";
        }

        var suffixText = normalizedBody[digitIndex..];
        if (int.TryParse(suffixText, NumberStyles.None, CultureInfo.InvariantCulture, out var suffix))
        {
            return (baseBody, Math.Max(suffix + 1, 2));
        }

        return (baseBody, 2);
    }

    private static int NormalizeBookId(int macroBookId)
        => macroBookId == SharedBookId ? SharedBookId : IndividualBookId;

    private static int WrapSlot(int slot)
        => ((slot - 1) % MaxMacroSlotsPerBook + MaxMacroSlotsPerBook) % MaxMacroSlotsPerBook + 1;

    private static int ToLocationKey(int bookId, int slot)
        => bookId * 1000 + slot;
}
