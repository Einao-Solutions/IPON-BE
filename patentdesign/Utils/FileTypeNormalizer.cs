using patentdesign.Models;

namespace patentdesign.Utils;

/// <summary>
/// Utility to normalize and validate FileTypes from various input formats
/// Handles inconsistent frontend input for withdrawal and cost endpoints
/// </summary>
public static class FileTypeNormalizer
{
    /// <summary>
    /// Normalizes fileType input to consistent FileTypes enum value
    /// Accepts: Patent/patent/0, Design/design/1, TradeMark/Trademark/trademark/trade mark/trade-mark/tm/2
    /// </summary>
    /// <param name="fileTypeInput">String or numeric representation of file type</param>
    /// <param name="normalizedType">Output: normalized FileTypes enum value</param>
    /// <returns>True if normalization succeeded, false if invalid input</returns>
    public static bool TryNormalizeFileType(string? fileTypeInput, out FileTypes normalizedType)
    {
        normalizedType = FileTypes.Patent; // default

        if (string.IsNullOrWhiteSpace(fileTypeInput))
            return false;

        var input = fileTypeInput.Trim().ToLowerInvariant();

        // Try numeric values first
        if (input == "0" || input == "1" || input == "2")
        {
            if (int.TryParse(input, out int numericValue) && numericValue >= 0 && numericValue <= 2)
            {
                normalizedType = (FileTypes)numericValue;
                return true;
            }
            return false;
        }

        // Normalize spacing and hyphens
        input = input.Replace("-", "").Replace(" ", "");

        // Patent: 0
        if (input == "patent")
        {
            normalizedType = FileTypes.Patent;
            return true;
        }

        // Design: 1
        if (input == "design")
        {
            normalizedType = FileTypes.Design;
            return true;
        }

        // TradeMark: 2 (various formats: trademark, TradeMark, trade mark, trade-mark, tm, TM)
        if (input == "trademark" || input == "tm")
        {
            normalizedType = FileTypes.TradeMark;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Normalizes fileType input, throws exception if invalid
    /// Use this for endpoints where invalid input should fail immediately
    /// </summary>
    public static FileTypes NormalizeFileType(string? fileTypeInput)
    {
        if (TryNormalizeFileType(fileTypeInput, out var normalizedType))
            return normalizedType;

        throw new ArgumentException($"Invalid fileType: '{fileTypeInput}'. Accepted values: Patent/patent/0, Design/design/1, TradeMark/trademark/tm/2");
    }

    /// <summary>
    /// Validates if a numeric fileType value is valid (0, 1, or 2)
    /// </summary>
    public static bool IsValidFileTypeNumeric(int? fileTypeNum)
    {
        return fileTypeNum.HasValue && fileTypeNum.Value >= 0 && fileTypeNum.Value <= 2;
    }
}
