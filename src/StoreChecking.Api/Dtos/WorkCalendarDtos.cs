namespace StoreChecking.Api.Dtos;

/// <summary>Một ô ngày trả về cho client. Giữ đúng tên field mà Angular đang dùng.</summary>
public record WorkDayDto(Guid Id, string Day, string Note, string? Color);

/// <summary>Nội dung ghi vào một ô ngày.</summary>
public record SaveWorkDayRequest(string? Note, string? Color);

/// <summary>Một dòng ghi chú tháng trả về cho client.</summary>
public record MonthNoteDto(Guid Id, string Period, string Content, int Sort);

/// <summary>Tạo một dòng ghi chú tháng mới (nội dung để trống, sửa sau).</summary>
public record CreateMonthNoteRequest(string Period, int Sort);

/// <summary>Sửa nội dung một dòng ghi chú tháng.</summary>
public record UpdateMonthNoteRequest(string Content);
