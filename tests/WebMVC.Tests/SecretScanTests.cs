using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Xunit;

namespace WebMVC.Tests;

/// <summary>
/// The API signs tokens with an HMAC key. This repository is public, so a key committed here is a
/// key anyone can mint admin tokens with. The value was leaked once (7aad51e) and rotated; this
/// guard exists so it can't come back unnoticed.
/// </summary>
public sealed class SecretScanTests
{
    /// <summary>Files that legitimately reference the signing key without containing its value.</summary>
    private static readonly string[] ScannedFiles =
    [
        "src/WebAPI/appsettings.json",
        "src/WebAPI/appsettings.Development.json",
        "src/WebAPI/appsettings.Production.json",
        "src/WebMVC/appsettings.json",
        "src/WebMVC/appsettings.Development.json",
        ".github/workflows/ci.yml",
    ];

    private static readonly Regex SigningKeyLine = new(
        @"^\s*[""']?Jwt(?:::|__)SigningKey[""']?\s*[:=]\s*(?<value>.*)$",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    [Fact]
    public void No_tracked_config_or_workflow_file_contains_a_jwt_signing_key_value()
    {
        var root = TestPathHelper.FindRepoRoot();
        var problems = new List<string>();

        foreach (var relative in ScannedFiles)
        {
            var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                continue;
            }

            var text = File.ReadAllText(path, Encoding.UTF8);
            foreach (Match match in SigningKeyLine.Matches(text))
            {
                if (HasLiteralValue(match.Groups["value"].Value))
                {
                    problems.Add($"{relative}: Jwt signing key has a literal value");
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    /// <summary>
    /// A value is legitimate when it is empty (config leaves the key to an environment variable or
    /// user-secrets) or when it is a reference resolved at run time — a shell substitution, an
    /// environment placeholder, or a secret-store expression.
    /// </summary>
    private static bool HasLiteralValue(string rawValue)
    {
        var value = rawValue.Trim().TrimEnd(',').Trim().Trim('"', '\'').Trim();
        if (value.Length == 0)
        {
            return false;
        }

        return !(value.StartsWith("$", StringComparison.Ordinal)
                 || value.StartsWith("%", StringComparison.Ordinal)
                 || value.StartsWith("@", StringComparison.Ordinal)
                 || value.Contains("{{", StringComparison.Ordinal)
                 || value.Contains("${", StringComparison.Ordinal));
    }

    [Fact]
    public void The_leaked_signing_key_value_is_not_reintroduced_anywhere_under_src_or_github()
    {
        // The value that shipped in 7aad51e, stored as a hash so this guard never becomes a copy of
        // the secret it is guarding against. Rotation made the original useless, but a paste from an
        // old clone would silently restore an already-public key.
        const string leakedSha256 = "e2505202aac5125ee8c1a719c135178ee44a73c7ff033bfc83763b790c2ba8dd";
        var root = TestPathHelper.FindRepoRoot();
        var hits = new List<string>();

        foreach (var dir in new[] { Path.Combine(root, "src"), Path.Combine(root, ".github") })
        {
            if (!Directory.Exists(dir))
            {
                continue;
            }

            foreach (var path in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                if (path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                {
                    continue;
                }

                // A base64 signing key is what we look for: 44 characters ending in '='.
                foreach (Match candidate in Base64Key.Matches(File.ReadAllText(path, Encoding.UTF8)))
                {
                    if (Sha256Hex(candidate.Value) == leakedSha256)
                    {
                        hits.Add(Path.GetRelativePath(root, path).Replace('\\', '/'));
                    }
                }
            }
        }

        Assert.True(hits.Count == 0, "Leaked signing key found in: " + string.Join(", ", hits));
    }

    private static readonly Regex Base64Key = new(
        @"[A-Za-z0-9+/]{43}=",
        RegexOptions.CultureInvariant);

    private static string Sha256Hex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
