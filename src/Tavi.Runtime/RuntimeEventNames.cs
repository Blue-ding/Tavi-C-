namespace Tavi.Runtime;

internal static class RuntimeEventNames
{
    internal static string ToKebabCase(string value)
    {
        var characters = new List<char>(value.Length + 4);
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            if (index > 0 && char.IsUpper(character))
                characters.Add('-');
            characters.Add(char.ToLowerInvariant(character));
        }
        return new string(characters.ToArray());
    }
}
