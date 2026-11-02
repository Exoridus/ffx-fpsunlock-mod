using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

internal static class NativeFieldHost
{
    internal static void Prepare(string root, string output, Action<string, string[], string> run)
    {
        const string pin = "7e85839b2efb67124d1760598c9f220cb559a41a";
        string cache = Path.Combine(root, ".workspace", "fahrenheit");
        if (!Directory.Exists(cache))
            run("git", ["clone", "--no-checkout", "https://github.com/fahrenheit-crew/fahrenheit.git", cache], root);
        string state = Path.Combine(root, ".workspace", "native-field-host");
        Directory.CreateDirectory(state);
        string source = Path.Combine(state, "source");
        if (Directory.Exists(source) && !Path.GetFullPath(source).StartsWith(Path.GetFullPath(state) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Host source must remain in its dedicated workspace.");
        string archive = Path.Combine(state, "alpha12.tar");
        run("git", ["-C", cache, "archive", "--format=tar", "--output=" + archive, pin], root);
        if (Directory.Exists(source)) Directory.Delete(source, true);
        Directory.CreateDirectory(source);
        TarFile.ExtractToDirectory(archive, source, overwriteFiles: false);
        run("git", ["init", "--quiet"], source);
        string patch = Path.Combine(root, "build", "fahrenheit", "additional-image-events.patch");
        string helper = Path.Combine(root, "build", "fahrenheit", "AdditionalImagePhase.cs");
        run("git", ["apply", "--check", patch], source);
        run("git", ["apply", patch], source);
        File.Copy(helper, Path.Combine(source, "src", "core", "events", "game_loop", "AdditionalImagePhase.cs"));
        run("dotnet", ["build", "src/runtime/Fahrenheit.Runtime.csproj", "-c", "Release", "--verbosity", "quiet", "-p:SolutionDir=" + source + Path.DirectorySeparatorChar], source);
        string payload = Path.Combine(state, "overlay");
        if (!Path.GetFullPath(payload).StartsWith(Path.GetFullPath(state) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Host overlay must remain in its dedicated workspace.");
        if (Directory.Exists(payload)) Directory.Delete(payload, true);
        Directory.CreateDirectory(Path.Combine(payload, "bin"));
        File.Copy(Path.Combine(source, "artifacts", "build", "Fahrenheit", "Release", "net10.0", "fh.dll"), Path.Combine(payload, "bin", "fh.dll"));
        File.Copy(Path.Combine(source, "artifacts", "build", "Fahrenheit.Runtime", "Release", "net10.0", "win-x86", "fhr.dll"), Path.Combine(payload, "bin", "fhr.dll"));
        var hashes = Directory.GetFiles(payload, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            .ToDictionary(path => Path.GetRelativePath(payload, path).Replace('\\', '/'), Hash);
        var provenance = new { workflow = "mod-field-pack", built_at_utc = DateTimeOffset.UtcNow,
            public_distribution = false, fahrenheit_base_ref = pin, patch_sha256 = Hash(patch), helper_sha256 = Hash(helper),
            payload_sha256 = hashes, native_execution = "not_run", scope = "Private additional-image event phase overlay; not an official Fahrenheit release." };
        string zip = Path.Combine(output, "fahrenheit-alpha12-native-field-overlay.zip");
        string temporary = Path.Combine(state, "overlay.zip");
        if (File.Exists(temporary)) File.Delete(temporary);
        ZipFile.CreateFromDirectory(payload, temporary);
        File.Copy(temporary, zip, overwrite: true);
        File.WriteAllText(zip + ".provenance.json", JsonSerializer.Serialize(provenance, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        File.WriteAllText(zip + ".sha256", Hash(zip) + "  " + Path.GetFileName(zip) + "\n");
        File.Copy(Path.Combine(root, "docs", "native-field-playtest.md"), Path.Combine(output, "native-field-playtest.md"), overwrite: true);
        Console.WriteLine($"FIELD HOST OVERLAY: {zip}. Not deployed; native field acceptance remains pending.");
    }

    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
