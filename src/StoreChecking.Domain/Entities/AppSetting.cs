using StoreChecking.Domain.Abstractions;

namespace StoreChecking.Domain.Entities;

/// <summary>
/// One switch a person can flip from the app.
///
/// <para>Key and value rather than a column per setting: adding a new option becomes one
/// row instead of a migration that changes the table's shape.</para>
/// </summary>
public class AppSetting : IOwnedByUser
{
    /// <summary>Owner. Comes from the token's `sub` claim, NEVER from the client.</summary>
    public Guid UserId { get; set; }

    /// <summary>Dotted name, e.g. <c>video.autoPost</c>.</summary>
    public string Key { get; set; } = "";

    public string Value { get; set; } = "";

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Names of the settings in use, so no caller has to spell one out.</summary>
public static class SettingKeys
{
    /// <summary>Đăng video lên Fanpage ngay khi dựng xong.</summary>
    public const string VideoAutoPost = "video.autoPost";
}
