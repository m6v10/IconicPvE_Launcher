namespace IconicLauncher.Core.Services;

public static class ModListBuilder
{
    public static string BuildModArgument(IEnumerable<string> installPaths) =>
        "-mod=" + string.Join(";", installPaths.Where(p => !string.IsNullOrWhiteSpace(p)));
}
