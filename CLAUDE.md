# Migrate từ Supabase sang .NET — sổ theo dõi

File này ghi lại tiến độ chuyển backend từ **Supabase** sang **.NET 9 + PostgreSQL tự dựng
trên NAS**. Xong giai đoạn nào thì tick giai đoạn đó.

Repo Angular: `C:\Packing\InventoryAndTrackingProfit` (vẫn đang chạy Supabase).
Repo này: backend mới, deploy riêng, **không đụng gì vào app đang chạy**.

---

## Quy ước

- **Comment trong code viết bằng TIẾNG ANH.** Áp dụng cho `.cs`, `.sql`, `Dockerfile`,
  `docker-compose.yml`. Chuỗi hiển thị cho người dùng (nhãn Swagger, thông báo lỗi khởi
  động) thì vẫn tiếng Việt.
- Tài liệu (`README.md`, file này) và commit message: tiếng Việt.
- **Bảng mới thì phải thêm `HasQueryFilter`** trong `AppDbContext`. Đây là thứ thay cho RLS
  của Supabase — quên là lộ dữ liệu giữa các user, và không có lưới nào đỡ.
- `user_id` **luôn** lấy từ claim `sub` của token. Không bao giờ nhận từ body hay query.
- Tắt tiến trình API trước khi build bằng Visual Studio, không thì nó khoá file `.exe`.
- **Thư mục bind mount phải có sẵn trên NAS.** Container Manager của Synology không tự
  tạo như Docker trên Linux — thiếu là container chết với `Bind mount failed`. Thư mục dữ
  liệu nào cũng phải ship kèm một file `.gitkeep`.
- **Đặt tên trên NAS:** mỗi app một thư mục trong `/volume1/docker/` đặt theo tên app
  (`solar`, `nas-uploader`, `storechecking`), và container đặt tiền tố theo tên app
  (`solar-web`, `storechecking-api`). Tên trống trơn kiểu `api` sẽ đụng ngay khi có app
  thứ hai.

---

## Nguyên tắc chuyển

1. **Từng tính năng một.** Không viết lại 81 hàm cùng lúc.
2. **Chạy song song.** Tính năng nào chưa chuyển thì vẫn dùng Supabase như cũ.
3. **Nhỏ và ít giá trị trước.** Dữ liệu kho hàng và chi tiêu chuyển sau cùng.
4. **Xong hẳn rồi mới sang cái tiếp.** Xong = đã deploy, Angular đã trỏ sang, dữ liệu cũ
   đã chuyển, và bỏ được lời gọi Supabase của tính năng đó.

## Chuyển dữ liệu

Hai bên đều là PostgreSQL nên `pg_dump` sang thẳng được.

**Chuyển đúng lúc CẮT SANG, không chuyển trước.** Chuyển sớm thì Supabase vẫn nhận ghi
mới, hai bên lệch nhau, và tới lúc cắt phải làm lại từ đầu.

- Chỉ dump **dữ liệu** (`--data-only`), KHÔNG dump schema — schema bên NAS đã bỏ
  `auth.uid()` và RLS nên dump nguyên sẽ lỗi vì tham chiếu `auth.users`.
- `user_id` khớp sẵn giữa hai bên vì API .NET dùng lại chính JWT của Supabase, claim `sub`
  là cùng một UUID. Không phải ánh xạ lại gì.
- View và trigger không có dữ liệu để chuyển — phải viết lại trong `db/*.sql`.
- Ảnh trên Supabase Storage KHÔNG nằm trong Postgres, phải tải riêng.

---

## Giai đoạn 0 — Nền tảng ✅ DONE

- [x] Repo riêng, tách khỏi app Angular
- [x] .NET 9 Minimal API + EF Core + Npgsql
- [x] Xác thực bằng chính JWT của Supabase (JWKS ES256) — không làm đăng nhập thứ hai
- [x] Global query filter thay cho RLS
- [x] `docker-compose.yml` cho NAS (Postgres + API, cổng 8140)
- [x] Swagger có nút Authorize để test tay
- [x] Chặn lỗi cấu hình ngay lúc khởi động
- [x] Đẩy lên GitHub

**Đã kiểm chứng:** 20/20 test chạy thật với Postgres 16 — EF ánh xạ đúng, `DateOnly`
không lệch múi giờ +07, và cách ly dữ liệu hoạt động (user B không đọc được dòng của A
kể cả khi biết Id).

---

## Giai đoạn 1 — Lịch làm ⏸️ DỪNG, ở lại Supabase

Hai bảng: `work_days`, `work_month_notes`.

- [x] Schema `db/001-work-calendar.sql`
- [x] 6 endpoint + `/health` + `/api/me`
- [x] Test tầng EF và cách ly dữ liệu
- [x] **Deploy lên NAS** — `http://192.168.1.76:8140/health` trả `{"ok":true,"db":true}`
- ~~Angular trỏ sang API mới~~ — KHÔNG làm nữa, Lịch làm ở lại Supabase

API lịch làm vẫn chạy trên NAS và vẫn hoạt động, chỉ là Angular không gọi tới. Giữ lại
làm nền, không xoá.

**Lưu ý:** hành vi "ô không ghi chú và không màu thì XOÁ dòng" là hành vi phá dữ liệu —
cần test kỹ ở bước Swagger.

---

## Giai đoạn 2 — Tiếng Anh 🔄 ĐANG LÀM

Hai bảng: `english_words`, `speaking_saved`.

**Đây là tính năng DUY NHẤT còn chuyển sang .NET.** Lý do: dữ liệu tích dần theo năm vì
luyện hằng ngày, và nó không liên quan gì tới khách hàng nên hỏng cũng không ảnh hưởng ai.

- [x] Schema `db/002-english.sql`
- [x] 6 endpoint (từ vựng + câu đã lưu), có phân trang và tìm kiếm
- [ ] **Test tầng EF và cách ly dữ liệu** ← đang ở đây (cần bật Docker Desktop)
- [ ] Nạp `db/002-english.sql` vào Postgres trên NAS (DB đã có dữ liệu nên KHÔNG tự chạy)
- [ ] Test qua Swagger với token thật
- [ ] Chuyển dữ liệu cũ từ Supabase sang
- [ ] Angular trỏ sang API mới (`english.component.ts`, `speaking.component.ts`)
- [ ] Bỏ `listEnglishWords` / `addEnglishWord` / `*SavedSentence*` khỏi `supabase.service.ts`

Phần **sinh câu bằng AI** (`/api/english`, `/api/speaking`) **giữ nguyên trên Vercel** —
nó không đụng database, và `GEMINI_API_KEY` đang ở đó rồi. Không có lý do gì phải chuyển.

---

## ❌ ĐÃ QUYẾT: các tính năng khác KHÔNG chuyển

Quyết định ngày 2026-08-25. Kho hàng, chi tiêu, đơn hàng, marketing, video, ghi chú
**ở lại Supabase vĩnh viễn**.

Lý do:

- Chuyển không mang lại tính năng mới nào — chỉ là viết lại 81 hàm để có đúng thứ đang có
- Không có nỗi đau nào cần chữa: không đụng trần free tier, không vấn đề chi phí
- Đưa về NAS thì mạng nhà thành điểm chết duy nhất; hiện app chạy bất kể nhà thế nào
- **Trang đặt hàng `/order` là trang công khai, khách quét QR để dùng** — khách không thể
  cài Tailscale, và Vercel không thể nằm trong tailnet. Chuyển `orders` về NAS là giết
  tính năng có khách thật đang dùng
- Mất RLS: bên Supabase database tự chặn rò rỉ; bên .NET là quy ước do người nhớ
- Chủ repo đã có sẵn cơ chế sao lưu dữ liệu

Nguyên tắc thay thế: **đặt dữ liệu ở nơi nó thuộc về.** Thứ gắn với nhà (solar, video,
file lớn) thì ở NAS. Thứ cần chạy mọi lúc mọi nơi thì ở cloud.

---

## Cách truy cập API từ ngoài — CHƯA QUYẾT

Hiện API chỉ gọi được trong mạng LAN. Muốn dùng tiếng Anh khi ra ngoài thì phải chọn một:

- **Tailscale** — điện thoại phải bật app mới gọi được
- **Cloudflare Tunnel** — không cần cài gì trên điện thoại, không mở port, miễn phí.
  ⚠️ Điều khoản Cloudflare cấm dùng bản miễn phí để phục vụ video/file lớn, nên nếu chọn
  hướng này thì **chỉ đẩy API qua đó, video vẫn đi Tailscale như hiện tại**
- **DDNS Synology + Let's Encrypt** — phải mở port 443 ra internet

---

## Việc chưa xong, không thuộc giai đoạn nào

- [ ] **Lỗi JWT "issued at future"** bên Supabase vẫn chưa tìm ra nguyên nhân. API .NET
      đã đặt `ClockSkew = 60s` nên tính năng nào chuyển sang đây là hết lỗi — nhưng gốc
      rễ thì vẫn chưa rõ.
- [ ] Angular còn **3 migration Supabase chưa chạy**: `migration-work-calendar.sql`,
      `migration-work-month-notes.sql`, `migration-speaking-saved.sql`. Vì Lịch làm nay
      ở lại Supabase, **cả ba đều vẫn cần chạy**.
