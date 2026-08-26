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
- **Không đụng `.RootElement` bên trong câu LINQ.** Cột `jsonb` ánh xạ sang `JsonDocument`;
  viết `x.Data.RootElement` trong `Select(...)` làm EF đòi đọc cột thành `JsonElement?` rồi
  ngã ngay lúc dựng truy vấn: *"No coercion operator is defined between types 'JsonDocument'
  and 'JsonElement?'"*. Cách đúng: chiếu cột ra trước (`.Select(x => new { x.Id, x.Data })`)
  rồi lấy `RootElement` trong bộ nhớ. Dựng object thường (ngoài LINQ) thì vẫn dùng được.
- **Thư mục bind mount phải có sẵn trên NAS.** Container Manager của Synology không tự
  tạo như Docker trên Linux — thiếu là container chết với `Bind mount failed`. Thư mục dữ
  liệu nào cũng phải ship kèm một file `.gitkeep`.
- **KHÔNG build .NET trên NAS.** Đã bỏ hẳn — xem mục Deploy. Đo thật: lúc đang build,
  `nas-uploader` trả trang mất 2-6 giây; build xong còn 0.2-1.2 giây — chậm gấp ~10 lần.
  Ảnh hưởng tới cả upload video từ điện thoại. Các container chạy nền (Postgres, .NET,
  Tailscale) thì KHÔNG phải vấn đề. Muốn dứt điểm thì build image ở máy rồi đẩy sang.
- **Container Manager: Stop KHÔNG xoá container.** Muốn xoá image phải xoá container
  trước (tab Container → Delete), nếu không sẽ báo "currently used by".
- **Tailscale phải bật `AllowFunnel`.** Không bật thì tên miền `.ts.net` chỉ phân giải
  trong tailnet ra IP nội bộ `100.x`, và trình duyệt CẤM trang HTTPS công khai (app trên
  Vercel) gọi vào mạng nội bộ — request bị chặn trước khi kịp gửi đi, báo
  `ERR_BLOCKED_BY_CLIENT`. Đây chính là lý do `nas-uploader` chạy được suốt (nó bật Funnel)
  còn `storechecking` thì không. Cách phân biệt: `dns.google/resolve?name=<host>&type=A`
  có trả IP công khai hay không.
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
- [x] Test tầng EF và cách ly dữ liệu — 23/23 đạt
- [x] Nạp `db/002-english.sql` vào Postgres trên NAS (chạy tay, DB đã có dữ liệu)
- [x] **Deploy lên NAS + Tailscale HTTPS** — `https://storechecking.tail631d54.ts.net`
      trả 401 ở mọi route tiếng Anh (route có, chỉ thiếu token)
- [x] **Chuyển dữ liệu cũ từ Supabase sang** — 17 từ + 1 câu, đếm trước/sau khớp nhau.
      Dữ liệu gốc trên Supabase vẫn còn nguyên, chưa xoá.
- [x] Angular trỏ sang API mới (`english-api.service.ts`)
- [x] **Bật Funnel + sửa lỗi 500 ở `GET /words`** — endpoint này chưa từng chạy được:
      `x.Data.RootElement` nằm trong câu LINQ làm EF ngã lúc dựng truy vấn
- [ ] Bỏ `listEnglishWords` / `addEnglishWord` / `*SavedSentence*` khỏi `supabase.service.ts`
      — đã không còn chỗ nào gọi, chỉ là code chết ← còn lại đúng việc này

**URL API:** `https://storechecking.tail631d54.ts.net`, có Funnel nên gọi được từ mạng bất
kỳ, thiết bị **không cần bật Tailscale**. Chứng chỉ Let's Encrypt thật.

Phần **sinh câu bằng AI** (`/api/english`, `/api/speaking`) **giữ nguyên trên Vercel** —
nó không đụng database, và `GEMINI_API_KEY` đang ở đó rồi. Không có lý do gì phải chuyển.

---

## Deploy — build ở PC, NAS tự cập nhật

**NAS không build .NET nữa.** Build trên NAS ăn CPU 5-15 phút và kéo mọi thứ khác ì theo,
kể cả upload video từ điện thoại. Nay PC build, GHCR giữ ảnh, watchtower trên NAS tự kéo
về rồi khởi động lại API.

Deploy = một lệnh trên PC:

```
.\tools\deploy.ps1
```

Script build ảnh, gắn hai nhãn (`latest` và mã commit), đẩy lên GHCR, rồi **chờ tới khi
`/health` trên NAS báo đúng mã commit vừa đẩy** mới báo xong. "Đã deploy" là thứ đo được
chứ không phải hy vọng. Ảnh riêng tư, chỉ mình kéo được.

### Cài một lần

1. **PC** — tạo GitHub token (Settings → Developer settings → Personal access tokens →
   Tokens (classic)) có quyền `write:packages` + `read:packages`, rồi `docker login ghcr.io`.
2. **PC** — chạy `.\tools\deploy.ps1 -NoVerify` để đẩy ảnh đầu tiên lên GHCR.
3. **NAS** — tạo `/volume1/docker/storechecking/watchtower/config.json` theo mẫu
   `watchtower/config.example.json`. Chuỗi `auth` là base64 của `tênGitHub:token`, sinh
   trên PC bằng:
   `[Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("hoangan9999:TOKEN"))`
   File này chứa token thật — `.gitignore` đã chặn, đừng bao giờ commit.
   Phải tạo TRƯỚC khi chạy project: thiếu file thì Docker tự tạo một thư mục cùng tên và
   watchtower lặng lẽ không đăng nhập được.
4. **NAS** — đưa ảnh về máy lần đầu (watchtower chỉ cập nhật container đã có, không tạo mới):
   Container Manager → Registry → Settings → thêm `https://ghcr.io` kèm đăng nhập.
   Nếu Container Manager không kéo nổi ảnh riêng tư thì dùng cách chắc chắn được — trên PC:
   `docker save ghcr.io/hoangan9999/storechecking-api:latest -o storechecking-api.tar`
   rồi copy file tar lên NAS, Container Manager → Image → Add → Add From File.
5. **NAS** — copy `docker-compose.yml` mới lên, Project → Build.

Xong bước 5 là hết đụng vào NAS. Từ đó: sửa code → `.\tools\deploy.ps1` → xong.

### Quay lại bản cũ

Mỗi lần deploy để lại một nhãn theo mã commit nên lùi được:

```
docker pull ghcr.io/hoangan9999/storechecking-api:<commit>
docker tag  ghcr.io/hoangan9999/storechecking-api:<commit> ghcr.io/hoangan9999/storechecking-api:latest
docker push ghcr.io/hoangan9999/storechecking-api:latest
```

### Thư mục trên NAS cần gì

Chỉ còn `docker-compose.yml`, `.env`, `db/`, `pgdata/`, `ts-config/`, `ts-state/`,
`watchtower/`. **`src/` và `Dockerfile` không còn cần trên NAS** vì NAS không build nữa.

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

## Cách truy cập API từ ngoài — TAILSCALE + FUNNEL

`https://storechecking.tail631d54.ts.net`, có bật **Funnel**.

Ban đầu định để tailnet-only cho kín, nhưng KHÔNG KHẢ THI: app phục vụ từ Vercel là trang
công khai, mà trình duyệt cấm trang công khai gọi vào mạng nội bộ. Bật Funnel là cách duy
nhất để app trên Vercel gọi được API trên NAS.

Hệ quả kèm theo: **thiết bị không cần bật Tailscale nữa** — địa chỉ đã công khai. Bảo vệ
nằm ở JWT: mọi endpoint dữ liệu đều đòi token Supabase hợp lệ, chỉ `/health` là mở.

Hai hướng còn lại nếu sau này muốn bỏ ràng buộc phải bật Tailscale:

- **Tailscale + Funnel** (đang dùng) — công khai, điện thoại KHÔNG cần bật app
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
