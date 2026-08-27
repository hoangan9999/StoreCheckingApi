namespace StoreChecking.Application.Common;

/// <summary>
/// A use case refusing input it cannot accept — an empty word, a blank sentence.
/// <para>Thrown rather than returned so the application services stay free of HTTP types:
/// the API layer turns this into <c>400 { "error": "..." }</c> in one place. The message
/// is shown to the user, so it is written in Vietnamese and is part of the HTTP contract
/// the tests pin down.</para>
/// <para>Only for rules the request itself violates. Something that simply is not there
/// is not an error: those return null and become a 404.</para>
/// </summary>
public sealed class ValidationException(string message) : Exception(message);
