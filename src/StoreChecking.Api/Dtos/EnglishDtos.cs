using System.Text.Json;

namespace StoreChecking.Api.Dtos;

/// <summary>A saved vocabulary word as returned to the client.</summary>
public record EnglishWordDto(Guid Id, string Word, JsonElement Data, DateTimeOffset CreatedAt);

/// <summary>Save a word together with the AI result generated for it.</summary>
public record SaveEnglishWordRequest(string Word, JsonElement Data);

/// <summary>A sentence kept from speaking practice, as returned to the client.</summary>
public record SavedSentenceDto(Guid Id, string Text, string Note, DateTimeOffset CreatedAt);

/// <summary>Keep one sentence from a speaking session.</summary>
public record SaveSentenceRequest(string Text, string? Note);
