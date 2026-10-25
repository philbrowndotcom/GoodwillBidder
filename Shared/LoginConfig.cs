using System.Text.Json;

/// <summary>
/// Loads the shopgoodwill login payload from <c>credentials.json</c> so it never
/// lives in source. The file is looked for in the current directory, then in the
/// app's directory and each parent above it (so the solution root is found when
/// running from bin/). See credentials.example.json for the format.
/// </summary>
public sealed class LoginConfig
{
    public const string FileName = "credentials.json";

    // shopgoodwill encrypts username/password client-side; these are the values
    // captured from the browser's SignIn/Login request body, copied verbatim.
    public string userName { get; set; } = "";
    public string password { get; set; } = "";
    public bool remember { get; set; } = true;
    public string appVersion { get; set; } = "0ad195b13e260b11";
    public string browser { get; set; } = "chrome";

    /// <summary>JSON body for POST /api/SignIn/Login.</summary>
    public string ToLoginBody() => JsonSerializer.Serialize(this);

    public static LoginConfig Load()
    {
        string path = Find() ?? throw new FileNotFoundException(
            $"{FileName} not found. Copy credentials.example.json to {FileName} in the solution folder and fill in your login payload.");

        var cfg = JsonSerializer.Deserialize<LoginConfig>(File.ReadAllText(path))
            ?? throw new InvalidDataException($"{path} is empty or invalid.");
        if (string.IsNullOrWhiteSpace(cfg.userName) || string.IsNullOrWhiteSpace(cfg.password))
            throw new InvalidDataException($"{path} must set userName and password.");
        return cfg;
    }

    private static string? Find()
    {
        string cwd = Path.Combine(Directory.GetCurrentDirectory(), FileName);
        if (File.Exists(cwd)) return cwd;

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, FileName);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
