# StoreChecking API

Backend .NET 9 + PostgreSQL chạy trên NAS, thay dần Supabase.

Repo này **tách riêng** khỏi app Angular và deploy riêng. Hiện mới làm **một tính năng:
Lịch làm** (`work_days`, `work_month_notes`). App Angular vẫn đang dùng Supabase —
chưa có gì bị đổi, chưa có gì hỏng.

## Vì sao dùng lại token của Supabase

API này **không tự làm đăng nhập**. Nó xác thực chính cái JWT mà app Angular đã có sau
khi đăng nhập Supabase, bằng khoá công khai Supabase công bố ở
`/auth/v1/.well-known/jwks.json` (thuật toán ES256).

Nghĩa là:

- Không phải làm màn hình đăng nhập thứ hai, không phải quản lý mật khẩu
- Lúc ghép vào Angular chỉ cần **thêm header** `Authorization: Bearer <access_token>`
- Sau này muốn bỏ hẳn Supabase thì đổi `Auth:SupabaseUrl` sang chỗ khác, phần còn lại giữ nguyên

`user_id` **luôn lấy từ claim `sub` của token**, không bao giờ nhận từ body hay query.

## Thay cho RLS

Bên Supabase, database tự chặn: quên điều kiện lọc thì Postgres vẫn không trả dữ liệu
người khác. Ở đây không có lưới đó, nên dùng **global query filter của EF Core**
(`AppDbContext`): mọi truy vấn tự thêm `WHERE user_id = <người đang đăng nhập>`, kể cả
khi endpoint quên viết.

Khi thêm bảng mới, **nhớ thêm `HasQueryFilter`** cho bảng đó.

## Chạy ở máy nhà

```bash
cd src/StoreChecking.Api
dotnet run
```

Cần đặt biến môi trường trước (hoặc dùng `dotnet user-secrets`):

| Biến | Là gì |
|---|---|
| `ConnectionStrings__Postgres` | Chuỗi kết nối Postgres |
| `Auth__SupabaseUrl` | `https://xxxx.supabase.co` — chỉ để lấy khoá công khai |
| `Cors__Origins` | Các origin được phép, ngăn bằng dấu phẩy |

## Chạy trên NAS

1. Chép cả thư mục này vào NAS, ví dụ `/volume1/docker/api/`
2. Chép `.env.example` thành `.env`, điền mật khẩu DB và `SUPABASE_URL`
3. Container Manager → Project → chọn thư mục → **Build**
4. Kiểm tra: `http://IP-NAS:8140/health` → `{"ok":true,"db":true}`

Postgres tự chạy `db/*.sql` **lần đầu** khi thư mục `pgdata` còn trống. Sau đó nó bỏ qua,
nên muốn đổi schema thì phải tự chạy SQL, hoặc xoá `pgdata` (mất hết dữ liệu).

Cổng **8140** chọn để không đụng `solar-web` (8130) và MQTT (1883).

Container DB **không mở cổng ra ngoài** — chỉ container `api` nói chuyện với nó.

## API

Mọi endpoint dưới `/api/` đều cần header `Authorization: Bearer <token>`.

| Method | Đường dẫn | Việc |
|---|---|---|
| GET | `/health` | Sống chưa, DB nối được chưa (không cần token) |
| GET | `/api/me` | Token có hợp lệ không, user id là ai |
| GET | `/api/work-calendar/days?from=&to=` | Ô ngày trong khoảng (YYYY-MM-DD) |
| PUT | `/api/work-calendar/days/{day}` | Ghi ô ngày. Không ghi chú và không màu → **xoá dòng** |
| GET | `/api/work-calendar/notes?period=` | Ghi chú tháng (period = ngày 1 của tháng) |
| POST | `/api/work-calendar/notes` | Thêm một dòng ghi chú trống |
| PUT | `/api/work-calendar/notes/{id}` | Sửa nội dung một dòng |
| DELETE | `/api/work-calendar/notes/{id}` | Xoá một dòng |

## Ghép vào Angular sau

Chưa làm. Khi làm thì trong `work-calendar.component.ts` đổi các lời gọi
`supabase.listWorkDays(...)` sang `fetch` tới API này, kèm token lấy từ
`supabase.session()?.access_token`.

Dữ liệu cũ trên Supabase **không tự chuyển sang** — cần một lần copy nếu muốn giữ.

## Đã kiểm chứng / chưa

**Đã:** build sạch (0 warning, 0 error); API khởi động được; `/health` trả lời; gọi không
token hoặc token bịa đều bị chặn 401; CORS cho đúng origin đã khai và từ chối origin lạ;
issuer khai trong code **khớp chính xác** với issuer Supabase công bố.

**Chưa:** chưa chạy được với database thật (máy chưa bật Docker), nên phần EF ánh xạ bảng
và các truy vấn **chưa ai chạy thử**. Cũng chưa thử với một token thật đang đăng nhập.
