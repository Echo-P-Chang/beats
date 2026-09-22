namespace Beats.Production.Middleware.Configuration;

public static class LocalEnvFile
{
    public static void Load()
    {
        foreach (var candidate in GetCandidatePaths())
        {
            if (File.Exists(candidate))
            {
                Load(candidate);
                return;
            }
        }
    }

    private static void Load(string path)
    {
        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();

            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var separatorIndex = line.IndexOf('=');

            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim().Trim('"');

            if (Environment.GetEnvironmentVariable(key) is null)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }

    private static IEnumerable<string> GetCandidatePaths()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);

            while (directory is not null)
            {
                yield return Path.Combine(directory.FullName, ".env.local");
                yield return Path.Combine(directory.FullName, ".env");
                directory = directory.Parent;
            }
        }
    }
}
