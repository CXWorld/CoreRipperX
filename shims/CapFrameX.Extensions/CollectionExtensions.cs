namespace CapFrameX.Extensions;

public static class CollectionExtensions
{
    public static bool IsNullOrEmpty<T>(this IEnumerable<T>? values)
    {
        return values == null || !values.Any();
    }
}
