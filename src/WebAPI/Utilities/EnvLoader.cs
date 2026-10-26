namespace WebAPI.Utilities;

public static class EnvLoader
{
    public static void Load()
    {
        var candidateDirs = new[]
        {
            Directory.GetCurrentDirectory(),
            Path.Combine(Directory.GetCurrentDirectory(), "..", ".."),
            AppContext.BaseDirectory
        };

        foreach (var dir in candidateDirs)
        {
            var envFile = Path.Combine(dir, ".env");
            if (!File.Exists(envFile)) continue;

            foreach (var line in File.ReadAllLines(envFile))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#')) continue;
                var eqIdx = trimmed.IndexOf('=');
                if (eqIdx <= 0) continue;
                var key = trimmed[..eqIdx].Trim();
                var val = trimmed[(eqIdx + 1)..].Trim().Trim('\'', '"');
                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
                {
                    Environment.SetEnvironmentVariable(key, val);
                }
            }
            break;
        }
    }
}
