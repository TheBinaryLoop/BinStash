// Copyright (C) 2025-2026  Lukas Eßmann
// 
//     This program is free software: you can redistribute it and/or modify
//     it under the terms of the GNU Affero General Public License as published
//     by the Free Software Foundation, either version 3 of the License, or
//     (at your option) any later version.
// 
//     This program is distributed in the hope that it will be useful,
//     but WITHOUT ANY WARRANTY; without even the implied warranty of
//     MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//     GNU Affero General Public License for more details.
// 
//     You should have received a copy of the GNU Affero General Public License
//     along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System.Diagnostics.CodeAnalysis;

namespace BinStash.Contracts.Release;

/// <summary>
/// The build target of a release variant — the thing that tells two payloads published under one
/// release version apart.
/// </summary>
/// <remarks>
/// <para>
/// A target key is an <em>opaque string</em>, not a parsed structure. BinStash stores whatever a
/// publisher builds, and that is not always a platform: <c>linux-x64</c> and <c>win-arm64</c> are
/// targets, but so are <c>debug_cuda</c>, <c>fips</c> and <c>customer.acme</c>. Any schema narrow
/// enough to validate the first pair would reject the rest, so identity stays a string and
/// structured matching lives in a separate, optional attribute bag — see
/// <c>ReleaseVariant.TargetAttributes</c>.
/// </para>
/// <para>
/// Opaque does not mean unconstrained. A target key reaches file names, URL path segments and a
/// unique index, so it is canonicalized to one spelling and validated to a shape that survives all
/// three: lowercase ASCII alphanumerics joined by single <c>-</c>, <c>_</c>, <c>.</c> or <c>/</c>
/// separators. That makes <c>Linux-x64</c> and <c>linux-x64</c> one target rather than two, and
/// keeps <c>../etc/passwd</c> from ever being one.
/// </para>
/// </remarks>
public static class ReleaseTarget
{
    /// <summary>
    /// The target key of a release that names no target. Publishing without a target and
    /// publishing one target called "default" are deliberately the same thing, so that a tenant
    /// who never wants targets never has to know the concept exists.
    /// </summary>
    public const string Default = "default";

    /// <summary>
    /// The longest a canonical target key may be. Mirrored by the <c>TargetKey</c> column width;
    /// changing it needs a migration.
    /// </summary>
    public const int MaxLength = 128;

    /// <summary>
    /// Whether <paramref name="targetKey"/> denotes the default target — that is, no target at
    /// all, or the literal default key in any casing.
    /// </summary>
    public static bool IsDefault([NotNullWhen(false)] string? targetKey)
        => string.IsNullOrWhiteSpace(targetKey)
           || targetKey.AsSpan().Trim().Equals(Default, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Canonicalizes <paramref name="targetKey"/>, throwing if it is not a usable target.
    /// </summary>
    /// <exception cref="ArgumentException">The target key is malformed or too long.</exception>
    public static string Canonicalize(string? targetKey)
    {
        if (!TryCanonicalize(targetKey, out var canonical, out var error))
            throw new ArgumentException(error, nameof(targetKey));

        return canonical;
    }

    /// <summary>
    /// Canonicalizes <paramref name="targetKey"/> without throwing. An unset target is not an
    /// error: it canonicalizes to <see cref="Default"/>.
    /// </summary>
    /// <returns><c>true</c> if the target is usable, otherwise <c>false</c> with a reason in
    /// <paramref name="error"/> fit to show a user.</returns>
    public static bool TryCanonicalize(string? targetKey, out string canonical, [NotNullWhen(false)] out string? error)
    {
        if (string.IsNullOrWhiteSpace(targetKey))
        {
            canonical = Default;
            error = null;
            return true;
        }

        // Backslashes are folded rather than rejected so that a Windows build matrix passing a
        // path-shaped target lands on the same key as everyone else.
        var normalized = targetKey.Trim().ToLowerInvariant().Replace('\\', '/');

        if (normalized.Length > MaxLength)
        {
            canonical = string.Empty;
            error = $"A build target is at most {MaxLength} characters; this one is {normalized.Length}.";
            return false;
        }

        if (!IsWellFormed(normalized))
        {
            canonical = string.Empty;
            error = $"'{targetKey.Trim()}' is not a valid build target. A target is lowercase letters and " +
                    "digits joined by single '-', '_', '.' or '/' separators, for example 'linux-x64'.";
            return false;
        }

        canonical = normalized;
        error = null;
        return true;
    }

    /// <summary>
    /// Alphanumeric runs joined by single separators — no leading, trailing or doubled separator,
    /// and nothing outside ASCII. Hand-rolled rather than a regex to keep the contracts assembly
    /// free of one, since the AOT-published CLI validates targets too.
    /// </summary>
    private static bool IsWellFormed(string normalized)
    {
        // Starting "after a separator" is what forbids a leading one.
        var afterSeparator = true;

        foreach (var c in normalized)
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                afterSeparator = false;
            }
            else if (c is '-' or '_' or '.' or '/' && !afterSeparator)
            {
                afterSeparator = true;
            }
            else
            {
                return false;
            }
        }

        // A trailing separator leaves the flag set, and an empty run is impossible here.
        return !afterSeparator;
    }
}
