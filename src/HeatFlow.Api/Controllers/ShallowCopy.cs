using System.Reflection;

namespace HeatFlow.Api.Controllers;

/// <summary>
/// Płytka kopia obiektu konfiguracji przez refleksję. Kontrolery modyfikują kopię, żeby
/// audyt mógł porównać starą i nową wersję (oryginał bywa śledzony przez EF).
/// </summary>
internal static class ShallowCopy
{
    public static T Of<T>(T src) where T : new()
    {
        var dest = new T();
        foreach (var prop in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanRead && p.CanWrite))
            prop.SetValue(dest, prop.GetValue(src));
        return dest;
    }
}
