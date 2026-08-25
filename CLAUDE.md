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

## Giai đoạn 1 — Lịch làm 🔄 ĐANG LÀM

Hai bảng: `work_days`, `work_month_notes`.

- [x] Schema `db/001-work-calendar.sql`
- [x] 6 endpoint + `/health` + `/api/me`
- [x] Test tầng EF và cách ly dữ liệu
- [ ] **Test 6 endpoint qua Swagger với token thật** ← đang ở đây
- [ ] Deploy lên NAS, `/health` trả `db:true`
- [ ] Angular trỏ sang API mới (`work-calendar.component.ts`)
- [ ] Chuyển dữ liệu cũ từ Supabase sang (nếu có)
- [ ] Bỏ `listWorkDays` / `saveWorkDay` / `*MonthNote*` khỏi `supabase.service.ts`

**Lưu ý:** hành vi "ô không ghi chú và không màu thì XOÁ dòng" là hành vi phá dữ liệu —
cần test kỹ ở bước Swagger.

---

## Giai đoạn 2 — Hạ tầng deploy ⬜ CHƯA

Làm khi đã chắc hướng .NET đi tiếp.

- [ ] Build image ở máy, đẩy lên `ghcr.io` (NAS yếu, build tại chỗ dễ hết RAM)
- [ ] Đổi `docker-compose.yml` từ `build: .` sang `image: ghcr.io/...`
- [ ] Xác nhận kiến trúc CPU của NAS (x86_64 hay ARM) để build đúng
- [ ] Sao lưu định kỳ `pgdata` — Supabase tự lo việc này, tự dựng thì không

---

## Giai đoạn 3 — Các tính năng nhỏ ⬜ CHƯA

Làm trước vì ít dữ liệu, hỏng cũng không mất gì.

- [ ] **Luyện nói** — `speaking_saved`
- [ ] **Tiếng Anh** — `english_words`
- [ ] **Ghi chú** — `notes`

---

## Giai đoạn 4 — Tính năng vừa ⬜ CHƯA

- [ ] **Đơn hàng** — `orders` (có cả trang đặt hàng công khai, không cần đăng nhập)
- [ ] **Đóng gói / Video** — `packing_videos`

---

## Giai đoạn 5 — Chi tiêu ⬜ CHƯA

- [ ] `expense_categories`, `expenses`, `monthly_income`
- [ ] 2 view: `v_expense_month_category`, `v_expense_month_total`

---

## Giai đoạn 6 — Kho hàng ⬜ CHƯA

Dữ liệu giá trị nhất, để sau cùng.

- [ ] `batches`, `products`, `sales`, `product_damages`
- [ ] 2 view: `batch_summary`, `product_stock`
- [ ] Trigger chặn bán quá tồn (bên Supabase đang là trigger trong DB)

---

## Giai đoạn 7 — Marketing ⬜ CHƯA

Phức tạp nhất vì có lưu file.

- [ ] `marketing`, `marketing_groups`, `marketing_posts`, `marketing_post_targets`,
      `post_queue`
- [ ] **Ảnh** — hiện dùng Supabase Storage. Chuyển sang lưu trên NAS; `nas-uploader` đã
      làm sẵn việc nhận upload và phục vụ file, tận dụng lại

---

## Giai đoạn 8 — Bỏ hẳn Supabase ⬜ CHƯA

Chỉ làm khi tất cả giai đoạn trên đã xong và chạy ổn định vài tuần.

- [ ] Tự làm đăng nhập (ASP.NET Core Identity + JWT)
- [ ] Đổi `Auth:SupabaseUrl` sang issuer của mình
- [ ] Xoá `supabase.service.ts` và gói `@supabase/supabase-js` khỏi Angular
- [ ] Xuất toàn bộ dữ liệu Supabase lần cuối rồi đóng project

---

## Việc chưa xong, không thuộc giai đoạn nào

- [ ] **Lỗi JWT "issued at future"** bên Supabase vẫn chưa tìm ra nguyên nhân. API .NET
      đã đặt `ClockSkew = 60s` nên tính năng nào chuyển sang đây là hết lỗi — nhưng gốc
      rễ thì vẫn chưa rõ.
- [ ] Angular còn **3 migration Supabase chưa chạy**: `migration-work-calendar.sql`,
      `migration-work-month-notes.sql`, `migration-speaking-saved.sql`. Nếu Lịch làm
      chuyển hẳn sang .NET thì hai cái đầu thành thừa.
