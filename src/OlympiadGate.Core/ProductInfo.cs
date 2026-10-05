using System.Reflection;

namespace OlympiadGate.Core;

public static class ProductInfo
{
    public static string Version
    {
        get
        {
            var info = typeof(ProductInfo).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? "";
            var plus = info.IndexOf('+');
            if (plus >= 0)
                info = info[..plus];
            return string.IsNullOrWhiteSpace(info) ? "0.0.0" : info.Trim();
        }
    }
}
