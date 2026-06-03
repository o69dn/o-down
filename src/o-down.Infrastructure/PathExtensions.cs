using o_down.Core.Pipeline;

namespace o_down.Infrastructure;

internal static class PathExtensions
{
    public static string Expand(string path) => Environment.ExpandEnvironmentVariables(path);
}
