namespace Wade.LnkParser;

internal static class ResourceStringParser
{
    public static (string? FilePath, int? ResourceId) ParseResourceReference(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return (null, null);
        }

        // Resource references are in the format: @path,-resourceId
        // Example: @%SystemRoot%\system32\Shell32.dll,-22579
        if (!input.StartsWith('@'))
        {
            return (null, null);
        }

        var parts = input.Substring(1).Split(',');
        if (parts.Length != 2)
        {
            return (null, null);
        }

        var filePath = parts[0].Trim();
        if (int.TryParse(parts[1].Trim(), out var resourceId))
        {
            return (filePath, resourceId);
        }

        return (filePath, null);
    }

    public static string ExpandEnvironmentVariables(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        return Environment.ExpandEnvironmentVariables(input);
    }
}
