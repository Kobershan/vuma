using System.Collections.Frozen;

namespace VumaRetail.Domain.Identity;

/// <summary>Passwords known to be unsafe for Vuma installations.</summary>
public static class PasswordBlocklist
{
    private static readonly FrozenSet<string> Blocked = new[]
    {
        "Admin" + "@Vuma2026!", "Password@123!", "Welcome@123!", "Changeme@1234!",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns whether a password is explicitly disallowed.</summary>
    public static bool IsBlocked(string password) => Blocked.Contains(password);
}
