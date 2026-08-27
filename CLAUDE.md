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
- **NAS chạy ARM64, không phải x86.** Nó đòi registry đúng `linux/arm64/v8`; ảnh chỉ có
  amd64 sẽ chết với `no matching manifest for linux/arm64/v8`. Trước đây không lộ ra vì
  NAS **tự build** — build tại chỗ thì luôn ra đúng kiến trúc của chính nó. Bỏ build trên
  NAS là mất tấm lưới đó, nên `Dockerfile` phải biên dịch chéo và workflow phải build cả
  `linux/amd64,linux/arm64`.
- **Ô Search của Container Manager không tìm được ảnh trên GHCR** — nó báo *"Unable to
  connect to the registry"*, nghe như lỗi mạng nhưng không phải: GHCR không mở API tìm
  kiếm. Không cần Search, vì tên ảnh đã ghi đủ trong `docker-compose.yml`; cứ Build là
  Docker kéo thẳng theo tên.
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

## Deploy — GitHub build, NAS tự cập nhật

**Không máy nào ở nhà build .NET nữa.**

- NAS không build: ăn CPU 5-15 phút và kéo mọi thứ khác ì theo, kể cả upload video từ
  điện thoại.
- PC cũng không build: đây là **máy công ty**, mà Docker Desktop trên máy công ty là bản
  có phí. Nên PC không cần cài Docker gì hết.

Nay **GitHub Actions build**, GHCR giữ ảnh, watchtower trên NAS tự kéo về rồi khởi động
lại API. Workflow: `.github/workflows/build-and-push.yml`, chạy mỗi lần push lên `master`.

Deploy = một lệnh trên PC:

```
.\tools\deploy.ps1
```

Script đẩy commit lên GitHub (đó là thứ khởi động build), rồi **chờ tới khi `/health`
trên NAS báo đúng mã commit vừa đẩy** mới báo xong. "Đã deploy" là thứ đo được chứ không
phải hy vọng.

**Hệ quả của việc bỏ build ở PC: không deploy được thứ chưa commit.** GitHub chỉ build
được cái đã đẩy lên, nên script từ chối chạy khi cây làm việc còn bẩn. Không còn bản
`-dirty` như trước.

Mã commit rút gọn **cố định 7 ký tự** ở cả hai đầu (`${GITHUB_SHA:0:7}` trong workflow,
`git rev-parse --short=7` trong script). Độ dài mặc định của `--short` tăng dần theo kích
thước repo — để mặc thì một ngày nào đó hai bên lệch nhau và script chờ mãi một phiên bản
không bao giờ tồn tại.

### Cài một lần

1. **GitHub** — vào tab Actions của repo, bật Actions nếu bị tắt. Không cần tạo secret
   nào: workflow đẩy ảnh bằng `GITHUB_TOKEN` sẵn có.
2. **PC** — `git push`, hoặc `.\tools\deploy.ps1 -NoWait`. Xem build chạy ở tab Actions.
   Xong thì ảnh nằm ở https://github.com/hoangan9999?tab=packages (riêng tư).
3. **PC** — tạo GitHub token (Settings → Developer settings → Personal access tokens →
   Tokens (classic)) chỉ cần quyền **`read:packages`** — token này để NAS *kéo* ảnh, không
   cần quyền ghi.
4. **NAS** — tạo `/volume1/docker/storechecking/watchtower/config.json` theo mẫu
   `watchtower/config.example.json`. Chuỗi `auth` là base64 của `tênGitHub:token`, sinh
   trên PC bằng:
   `[Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("hoangan9999:TOKEN"))`
   File này chứa token thật — `.gitignore` đã chặn, đừng bao giờ commit.
   Phải tạo TRƯỚC khi chạy project: thiếu file thì Docker tự tạo một thư mục cùng tên và
   watchtower lặng lẽ không đăng nhập được.
5. **NAS** — đưa ảnh về máy lần đầu (watchtower chỉ cập nhật container đã có, không tạo
   mới): Container Manager → Registry → Settings → thêm `https://ghcr.io`, đăng nhập bằng
   token ở bước 3.
   Không có Docker trên PC nên **không còn đường `docker save` + copy file tar**. Nếu
   Container Manager không kéo nổi ảnh riêng tư thì bật SSH trên NAS rồi làm thẳng ở đó:
   ```
   sudo docker login ghcr.io -u hoangan9999
   sudo docker pull ghcr.io/hoangan9999/storechecking-api:latest
   ```
6. **NAS** — copy `docker-compose.yml` mới lên, tạo sẵn thư mục `watchtower/`,
   Project → Build. Container `storechecking-api` cũ phải **xoá** ở tab Container (Stop
   không xoá) thì mới bỏ được ảnh NAS tự build trước đây.

Xong bước 6 là hết đụng vào NAS. Từ đó: sửa code → commit → `.\tools\deploy.ps1` → xong.

Kiểm chứng: `curl https://storechecking.tail631d54.ts.net/health` phải có trường
`version` đúng bằng mã commit. Còn trả `{"ok":true,"db":true}` trống trơn nghĩa là NAS
vẫn chạy ảnh cũ nó tự build.

### Quay lại bản cũ

Mỗi lần build để lại một nhãn theo mã commit. Không có Docker ở PC nên lùi bản làm trên
GitHub: tab **Actions** → chọn lần chạy của commit muốn quay lại → **Re-run all jobs**.
Nó build lại đúng commit đó và gắn `latest`, watchtower kéo về trong một phút.

Muốn lùi hẳn cả lịch sử thì `git revert` rồi `.\tools\deploy.ps1` như bình thường.

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
