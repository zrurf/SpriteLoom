namespace SpriteLoom.Pipeline;

public static class PassParameters
{
    public static string GetString(
        IReadOnlyDictionary<string, object?> parameters,
        string key,
        string defaultValue = "")
    {
        return parameters.TryGetValue(key, out var value) && value is string text
            ? text
            : defaultValue;
    }

    public static int GetInt(
        IReadOnlyDictionary<string, object?> parameters,
        string key,
        int defaultValue)
    {
        return parameters.TryGetValue(key, out var value) && value is int number
            ? number
            : defaultValue;
    }

    public static bool GetBool(
        IReadOnlyDictionary<string, object?> parameters,
        string key,
        bool defaultValue)
    {
        return parameters.TryGetValue(key, out var value) && value is bool flag
            ? flag
            : defaultValue;
    }

    public static T? GetValue<T>(
        IReadOnlyDictionary<string, object?> parameters,
        string key)
        where T : class
    {
        return parameters.TryGetValue(key, out var value) ? value as T : null;
    }

    public static IReadOnlyList<string> GetStringList(
        IReadOnlyDictionary<string, object?> parameters,
        string key)
    {
        return parameters.TryGetValue(key, out var value) && value is IReadOnlyList<string> items
            ? items
            : [];
    }
}
