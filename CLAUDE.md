# Migrate từ Supabase sang .NET — sổ theo dõi

Chuyển backend từ **Supabase** sang **.NET 9 + PostgreSQL tự dựng trên NAS**.

**Hai repo, phải có cả hai mới làm việc được:**

| | Đường dẫn cũ | Remote |
|---|---|---|
| Backend (repo này) | `D:\Dev\StoreCheckingApi\StoreCheckingApi` | `hoangan9999/StoreCheckingApi` |
| Angular | `D:\Dev\StoreChecking` | `hoangan9999/InventoryAndTrackingProfit` |

---

## ⚠️ CHỖ CHẠY ĐÃ ĐỔI — 2026-08-30

**Không còn chạy trên NAS.** Cụm này nay chạy trên **laptop cá nhân**
(`C:\Packing\StoreCheckingApi`, Docker Desktop).

Lý do: NAS là **DS124, 1GB RAM hàn chết**. Chạy PostgreSQL + API .NET + hai Tailscale +
watchtower + solar + nas-uploader trên đó đã kéo sập cả máy **ba lần trong một tiếng**.
Lần nặng nhất mất luôn giao diện DSM — ping vẫn thông mà không dịch vụ nào trả lời. Và
trước đó Container Manager còn tự dừng một đêm. Đây không phải chuyện chỉnh số cho khéo:
chỗ đó không đủ chỗ. NAS nay chỉ còn giữ `nas-uploader`.

`hoangancloud` trong tailnet **chính là con NAS đó** (đã đối chiếu: cùng trả về một nội
dung ở cổng 5000 qua cả LAN lẫn tailnet). Không có máy Linux nào khác luôn bật.

**Địa chỉ không đổi:** vẫn `storechecking.tail631d54.ts.net`, vì container Tailscale giữ
nguyên hostname `storechecking`, chỉ chạy ở máy khác. App trên Vercel không phải sửa gì.

### Dựng lại từ đầu trên một máy mới

1. Xoá node `storechecking` cũ trong Tailscale admin — không xoá thì node mới bị đặt tên
   `storechecking-1` và địa chỉ đổi.
2. Tạo auth key mới, chép `.env.example` thành `.env` rồi điền.
3. `.\tools\restore-and-start.ps1` — nạp `backup.sql` vào database RỖNG rồi dựng cả cụm.
   Phải rỗng vì `db/*.sql` chạy trước sẽ tạo sẵn bảng và `CREATE TABLE` trong dump sẽ đổ.
   **Không thể chép thẳng thư mục dữ liệu Postgres** giữa NAS (ARM64) và máy này (x86).

### Bảo đảm "máy bật thì backend chạy"

- Docker Desktop: `autoStart = true`, RAM cấp cho VM nâng 2GB → 6GB
  (`%APPDATA%\Docker\settings.json`).
- `tools/keep-backend-up.ps1 -Loop` canh mỗi 5 phút: Docker chết thì bật lại, container
  thiếu thì `docker compose up -d`. Chạy qua lối tắt trong thư mục Startup
  (`storechecking-backend.vbs`) — **tác vụ theo lịch của Windows cần quyền quản trị nên
  không dùng được**. Log ở `%LOCALAPPDATA%\storechecking-keepalive.log`.
- Cần thiết vì Docker Desktop đã tự tắt **ba lần** trong một buổi chiều. Riêng
  `restart: unless-stopped` không cứu được: Docker chết thì không còn ai bật lại.

---

## 📌 ĐANG Ở ĐÂU — cập nhật 2026-08-29

**Cuộc migrate đã XONG.** Backend, dữ liệu, Angular đều đã cắt sang; code Supabase cũ đã
gỡ. Không còn việc dở dang nào, chỉ còn mấy thứ dọn dẹp ở cuối mục này.

| Việc | Trạng thái |
|---|---|
| Deploy tự động (Actions → GHCR → watchtower) | ✅ một lệnh `.\tools\deploy.ps1` |
| Test hợp đồng | ✅ 123 test, chạy trên CI với Postgres thật |
| Cổng chặn: test đỏ thì không deploy | ✅ `build` job `needs: test` |
| Clean Architecture 4 project | ✅ `a83ee20` |
| Query filter tự động theo `IOwnedByUser` | ✅ + 2 test canh gác |
| Schema 22 bảng/view | ✅ `db/001`–`008`, API tự nạp lúc khởi động |
| Chuyển dữ liệu 17 bảng | ✅ 2026-08-29, ~474 dòng, khớp hai bên |
| BE: Lịch làm, Tiếng Anh, Ghi chú, Đóng gói, Chi tiêu, Kho hàng, Sao lưu | ✅ 56 endpoint |
| Angular trỏ sang API mới | ✅ `d76100e`, chủ repo đã test tay hết |
| Gỡ 57 hàm Supabase cũ | ✅ `supabase.service.ts` 851 → 262 dòng |

### Cái gì chạy ở đâu, sau khi xong

| Ở NAS (.NET + Postgres) | Ở lại Supabase |
|---|---|
| Lịch làm, Tiếng Anh, Ghi chú, Đóng gói, Chi tiêu, Kho hàng, Sao lưu | Đăng nhập (3 hàm), Marketing (17), Đơn hàng (3) |

Ba nhóm ở lại đều có lý do riêng, ghi ngay trên đầu class `SupabaseService` bên repo
Angular. Tóm tắt: Supabase vẫn là **nơi cấp danh tính** (API .NET xác thực chính token đó,
không có tài khoản riêng); Marketing có **cron Vercel 19:00** không ai đăng nhập; `/order`
là **trang công khai** khách quét QR, phải sống kể cả khi nhà mất điện.

### Phía Angular tổ chức thế nào

Mỗi module một service trong `src/app/core/`, tên `*-api.service.ts`, tất cả dùng chung
`api-client.service.ts` (token, timeout 30s, trạng thái kết nối, `CachedList`).

`work-calendar-api` · `english-api` · `notes-api` · `packing-api` · `expenses-api` ·
`inventory-api` · `backup-api`

Chữ ký hàm giữ y nguyên bản Supabase cũ, nên component chỉ đổi chỗ `inject`. Mỗi service
có lớp mapper riêng vì API trả **camelCase** còn model Angular dùng **snake_case**.

Một chỗ cố ý không dùng thứ tự của server: `getAllStock` sắp lại ở client bằng
`localeCompare`, vì API bật `InvariantGlobalization` nên không biết cách sắp chữ tiếng Việt.

### Việc còn nợ

- [ ] **Đổi 4 thứ đã lộ trong lịch sử chat** — nên làm sớm:
      token GHCR (đang có quyền `repo` cho MỌI repo riêng tư, chỉ cần `read:packages`),
      mật khẩu database Supabase, mật khẩu Postgres trên NAS (`DB_PASSWORD` trong `.env`),
      và `TS_AUTHKEY`.
- [ ] **Xoá dữ liệu cũ trên Supabase** — vẫn còn nguyên, cố ý. Chỉ xoá khi đã yên tâm hẳn.
- [ ] **Sao lưu không có 5 bảng**: `notes`, `work_days`, `work_month_notes`,
      `english_words`, `speaking_saved` chưa từng nằm trong bản sao lưu, kể cả thời
      Supabase. Thêm vào là đổi nội dung file — cần quyết định riêng.
- [ ] **Code chết bên Angular**: `updateQueueItem`, `listPostTargets`, `setPosted` trong
      `supabase.service.ts` không còn ai gọi. Thuộc Marketing nên chưa gỡ.

### Chưa bao giờ kiểm chứng

Toàn bộ 123 test chạy trên CI với Postgres thật, nhưng **máy dev không có Postgres cũng
không có Docker** (máy công ty). Nên `dotnet test` ở máy sẽ báo **skip hết** — đó là bình
thường, không phải hỏng. Muốn chạy thật thì đặt biến `TEST_POSTGRES` trỏ tới một Postgres
bất kỳ.


### ✅ Chuyển dữ liệu — XONG 2026-08-29

Chép một lần cho cả 17 bảng (trừ `orders`, ở lại Supabase vĩnh viễn). Số dòng khớp hai
bên, tổng ~474 dòng. **Chủ repo đã ngừng dùng product từ 2026-08-29**, nên dữ liệu chép
sang là bản cuối — không còn chuyện hai bên lệch nhau, không phải chép lại.

Dữ liệu trên Supabase **chưa xoá**, cứ để tới khi app chạy ổn một thời gian.

**Cách đã dùng** — SSH vào NAS, script `tools/migrate-all-from-supabase.sh`. Bốn thứ vướng
dọc đường, ghi lại vì cái nào cũng sẽ gặp lại:

1. **Supabase chạy PostgreSQL 17.6, container NAS là 16.** `pg_dump` từ chối dump server
   mới hơn chính nó. `psql` thì không kén, nên phần đếm vẫn chạy — chỉ dump chết. Cách gỡ:
   chạy `pg_dump` trong ảnh `postgres:17-alpine` tạm thời, rồi đổ vào psql của container 16.
2. **`SET transaction_timeout` mới có từ PG17**, bản 16 không hiểu, nên phần mở đầu bản dump
   làm `ON_ERROR_STOP` huỷ cả lượt. Cắt đúng dòng đó bằng `sed` là xong.
3. **Thiếu `set -o pipefail` thì script báo OK dối.** Trạng thái của `pg_dump | psql` lấy
   theo psql, mà psql nhận đầu vào rỗng vẫn thành công — 15 bảng báo `OK ... -> 0 dòng`
   trong khi không chép được gì. Chỉ có bước đối chiếu số dòng ở cuối cứu được.
4. **Không nối được Supabase bằng direct connection** (IPv6-only). Phải dùng Session pooler
   `aws-0-ap-southeast-2` — xem mục Quy ước.

**Bài học lớn nhất: dựng lại schema từ `supabase/*.sql` là chép tay, và chép tay thì sót.**
`db/007` thiếu bốn thứ trong `migration-shipping-damage.sql`, đã bù bằng `db/008`:

| Sót gì | Lộ ra thế nào |
|---|---|
| `sales.shipping_fee` | chép dữ liệu gãy — to tiếng, dễ thấy |
| Trigger `check_damage()` | **im lặng** — ghi hàng hư vượt tồn mà DB không chặn |
| View `revenue` không trừ phí ship | **im lặng** — doanh thu bị thổi phồng ở mọi báo cáo |
| View thiếu `damaged_qty` | **im lặng** — hàng đã hỏng vẫn tính là còn trên kệ |

Ba cái im lặng nguy hiểm hơn cái làm gãy. Chúng chỉ lộ ra vì phải đọc lại cả file khi đi
tìm cột thứ nhất. **Trước khi viết Domain/Application cho một module, đọc lại TOÀN BỘ file
`supabase/migration-*.sql` liên quan** — đừng chỉ grep `create table`.

**Sửa file schema đã áp dụng thì API không khởi động được.** `SchemaMigrator` ghi checksum
từng file; đổi nội dung file đã chạy là nó từ chối chạy. Sự thật mới luôn đi vào file mới.

### ✅ Sự cố API 502 — đã tìm ra nguyên nhân, 2026-08-27

NAS mất điện đột ngột. Sau đó API trả 502 suốt nhiều giờ trong khi Container Manager báo
mọi container đều "Up".

**Nguyên nhân:** PostgreSQL cần thời gian phục hồi sau lần tắt không sạch, và API khởi
động đúng vào cửa sổ đó. `SchemaMigrator` mở kết nối, thất bại, **ném lỗi** — mà nó chạy
*trước* `app.Run()`, nên Kestrel không bao giờ nghe cổng. Tiến trình chết, container chết,
Docker restart lại rơi vào đúng tình huống cũ.

**Cách gỡ đã dùng, ghi lại vì rất hiệu quả:**

1. Phân biệt tầng hỏng bằng dạng lỗi HTTPS:
   - TLS hỏng hẳn (`curl` lỗi 35, `time_appconnect=0`) → Tailscale
   - **502 kèm chứng chỉ hợp lệ** → Tailscale tốt, backend không trả lời
2. Gọi API từ container khác trên cùng mạng compose, để loại trừ chuyện mạng docker.
   Trong `storechecking-db` (alpine có busybox wget, chú ý `-T` chứ không phải `--timeout`):
   `wget -O- -T 10 http://api:8080/health`
   → `Connecting to api:8080 (172.20.0.4:8080)` rồi `Connection refused` = DNS và mạng đều
   tốt, chỉ là **không có gì nghe cổng 8080**. Loại hẳn giả thuyết lệch mạng.
3. Chạy tay một tiến trình thứ hai trong container `storechecking-api` để thấy log khởi
   động — Container Manager không hiện log, và tab Log hay báo "No logs available":
   `dotnet StoreChecking.Api.dll`
   Terminal của Container Manager **không cuộn được**, nên cắt đầu ra:
   `sh -c "dotnet StoreChecking.Api.dll 2>&1 | head -25"`
4. Đọc thứ tự hàm trong stack trace để biết lỗi ở đâu. Chết trong
   `PostgresDatabaseInfo.LoadPostgresInfo` là **sau khi xác thực đã xong** — nên mật khẩu
   không sai, database mới là thứ chưa phục vụ được.

**Đã sửa hai lỗi thật:**

- `pg_advisory_lock` chờ vô hạn → đổi sang `pg_try_advisory_lock`, chờ tối đa 60 giây.
- Không thử lại khi database chưa sẵn sàng → nay thử lại tối đa 120 giây, có log mỗi lần.
  `depends_on: service_healthy` của compose không đủ: nó chỉ có tác dụng khi compose dựng
  cả stack, và `pg_isready` xanh trước khi Postgres đủ khoẻ để trả lời truy vấn kiểu dữ
  liệu mà Npgsql chạy ở kết nối đầu tiên.

**Kết quả đo được:** `{"ok":true,"db":true,"version":"2f53f8e"}`, và
`Schema: 0 file mới nạp, 7 file tổng cộng` — **toàn bộ 22 bảng/view đã có trên NAS.**

**Hai việc vô ích, đừng lặp lại:**

- `http://192.168.1.76:8140/health` chỉ có nghĩa khi máy ở **cùng mạng nhà**. Vào DSM qua
  QuickConnect vẫn là ở ngoài mạng.
- `SchemaMigrator` không sai ở phần chạy SQL: log CI của `1909a50` cho thấy nó nạp trót
  lọt cả 7 file, kể cả `007-inventory.sql` với hàm plpgsql dấu `$$`, rồi 44 test xanh.

### Vòng lặp mỗi module — giữ lại làm mẫu

Cả sáu module đã đi qua đúng vòng này. Ghi lại vì nếu sau này thêm module mới thì làm y vậy:

1. **Đọc TRỌN file `supabase/migration-*.sql` liên quan**, không grep `create table` rồi
   thôi. Đây là bài học đắt nhất của cả cuộc migrate — xem mục Chuyển dữ liệu.
2. Đọc trọn phần hàm tương ứng trong `supabase.service.ts` **và** cách component gọi nó.
   Hàm nào không còn ai gọi thì đừng viết endpoint cho nó.
3. Schema: file mới `db/00N-*.sql`, **không sửa file đã áp dụng**.
4. Domain → Application → Infrastructure → Api, theo bảng phân tầng ở mục Kiến trúc.
5. Test hợp đồng cho từng endpoint + test cách ly hai user cho **từng bảng và từng view**.
6. Deploy `.\tools\deploy.ps1` — schema đi theo code, tự nạp.
7. Angular: thêm `*-api.service.ts` mới, giữ nguyên chữ ký hàm cũ, đổi chỗ `inject` ở
   component. Chỉ gỡ bản Supabase sau khi đã dùng thật một thời gian.

### 🔒 Marketing — QUYẾT ĐỊNH 2026-08-29: ở lại Supabase, KHÔNG chuyển

Cùng lý do với `orders`: có một tiến trình máy-gọi-máy phụ thuộc vào nó.

`vercel.json` khai `crons: [{ path: "/api/cron-post", schedule: "0 12 * * *" }]` — 12:00
UTC, tức **19:00 giờ Việt Nam mỗi ngày**. `api/cron-post.js` lấy món cũ nhất trong
`post_queue`, cho AI viết caption rồi đăng lên Fanpage, đọc/ghi bằng
`SUPABASE_SERVICE_ROLE_KEY`. Nút "Đăng ngay" trong app gọi chính endpoint đó.

Chuyển `post_queue` sang NAS thì cron gãy vào 19:00 hôm sau, vì API .NET chỉ nhận JWT
người dùng còn cron không có ai đăng nhập. Ba hướng đã cân nhắc — API key tĩnh cho máy,
cho cron tự đăng nhập bằng tài khoản dịch vụ, hoặc để bảng lại — và chọn hướng cuối:
**không chuyển gì trong module Marketing.**

Một nhận định trong code nay đã CŨ, đừng tin lại: `marketing.component.ts` ghi *"cron chạy
trên Vercel không vào được NAS sau Tailscale"*. Đúng lúc viết, sai từ khi bật Funnel —
`storechecking.tail631d54.ts.net` nay là địa chỉ công khai có chứng chỉ thật. Nếu sau này
đổi ý thì rào cản còn lại chỉ là xác thực, không phải đường mạng.

### ✅ Nghiệp vụ nằm trong database — đã bê sang, giữ cả hai lớp

Hai trigger plpgsql chặn tồn kho âm, nay chạy trên NAS y như trên Supabase:

- `check_stock()` trên `sales` — không cho bán vượt `nhập − đã bán − đã hỏng`
- `check_damage()` trên `product_damages` — không cho ghi hư vượt tồn khả dụng

Tầng Application kiểm **trước** để câu báo lỗi đọc được và có tên sản phẩm; trigger vẫn giữ
làm chốt cuối cho trường hợp hai đơn cùng mua nốt món cuối, cả hai đều qua được vòng kiểm.
`UnitOfWork` bắt `PostgresException` mã `P0001` rồi đổi thành `ValidationException`, nên lỗi
trigger cũng ra 400 kèm nguyên câu tiếng Việt của nó chứ không phải 500 kèm stack trace.

---

## Quy ước

- **Comment trong code viết bằng TIẾNG ANH.** Áp dụng cho `.cs`, `.sql`, `Dockerfile`,
  `docker-compose.yml`. Chuỗi hiển thị cho người dùng (nhãn Swagger, thông báo lỗi khởi
  động) thì vẫn tiếng Việt.
- Tài liệu (`README.md`, file này) và commit message: tiếng Việt.
- **Entity mới thì cho implement `IOwnedByUser`** — thế là đủ. `AppDbContext` duyệt model
  và tự gắn `e => e.UserId == CurrentUserId` cho mọi entity mang interface đó. Đây là thứ
  thay cho RLS của Supabase. Không còn dòng `HasQueryFilter` nào phải nhớ, nên cũng không
  còn dòng nào để quên; hai test canh gác bắt trường hợp entity không implement interface.
- **Repository không bao giờ gọi `IgnoreQueryFilters()`.** Đó là cách duy nhất phá được
  lớp bảo vệ trên. Nó không xuất hiện ở đâu trong `Infrastructure/Persistence/Repositories`.
- `user_id` **luôn** lấy từ claim `sub` của token. Không bao giờ nhận từ body hay query.
- Tắt tiến trình API trước khi build bằng Visual Studio, không thì nó khoá file `.exe`.
- **Không đụng `.RootElement` bên trong câu LINQ.** Cột `jsonb` ánh xạ sang `JsonDocument`;
  viết `x.Data.RootElement` trong `Select(...)` làm EF đòi đọc cột thành `JsonElement?` rồi
  ngã ngay lúc dựng truy vấn: *"No coercion operator is defined between types 'JsonDocument'
  and 'JsonElement?'"*. Cách đúng: chiếu cột ra trước (`.Select(x => new { x.Id, x.Data })`)
  rồi lấy `RootElement` trong bộ nhớ. Dựng object thường (ngoài LINQ) thì vẫn dùng được.
- **`bin/` và `obj/` trong `.dockerignore` PHẢI viết `**/bin/`.** Không có `**/` thì chỉ
  khớp thư mục ở gốc, `src/*/obj` vẫn lọt vào build context — mang theo
  `project.assets.json` trỏ đường dẫn Visual Studio, và ảnh build đổ với *"Unable to find
  fallback package folder 'C:\Program Files (x86)\...'"*. CI không bao giờ dính vì máy CI
  không có `obj` sẵn; chỉ máy đã build tại chỗ mới gặp — tức đúng máy nay là chỗ chạy chính.
- ~~**Thư mục bind mount phải có sẵn trên NAS.**~~ Không còn áp dụng: `pgdata` và `ts-state`
  đã chuyển sang named volume. Bind mount là thứ Postgres không chịu được trên Windows.
  Ghi chú cũ: Container Manager của Synology không tự
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
- **Nối tới Supabase phải qua Session pooler, không phải direct connection.**
  `db.<ref>.supabase.co` nay chỉ phân giải ra IPv6, mà container trên NAS không có IPv6 —
  `psql` báo `Address not available`, nghe như server chết chứ không như lỗi địa chỉ. Lấy
  chuỗi ở khối **Session pooler** trong hộp Connect: host `aws-0-<vùng>.pooler.supabase.com`
  cổng 5432, và user mang theo mã project (`postgres.<ref>`, không phải `postgres` trơn).
  Transaction pooler ở cổng 6543 thì không phục vụ được `pg_dump`.
- **Ô Search của Container Manager không tìm được ảnh trên GHCR** — nó báo *"Unable to
  connect to the registry"*, nghe như lỗi mạng nhưng không phải: GHCR không mở API tìm
  kiếm. Không cần Search, vì tên ảnh đã ghi đủ trong `docker-compose.yml`; cứ Build là
  Docker kéo thẳng theo tên.
- **Đặt tên trên NAS:** mỗi app một thư mục trong `/volume1/docker/` đặt theo tên app
  (`solar`, `nas-uploader`, `storechecking`), và container đặt tiền tố theo tên app
  (`solar-web`, `storechecking-api`). Tên trống trơn kiểu `api` sẽ đụng ngay khi có app
  thứ hai.

---

## Kiến trúc — Clean Architecture, 4 project

✅ **Xong 2026-08-27, commit `a83ee20`.** 33/33 test hợp đồng xanh mà **không sửa một dòng
test nào** — hợp đồng HTTP với app Angular còn nguyên vẹn qua cả cuộc viết lại. Đã deploy,
NAS đang chạy bản này.

Tái cấu trúc ngày 2026-08-27, **trước** khi chuyển 17 bảng còn lại. Lý do làm bây giờ:
rót 79 hàm vào một project phẳng rồi mới tách thì đắt gấp nhiều lần.

```
StoreChecking.Domain          thực thể, không phụ thuộc gì
      ↑
StoreChecking.Application     service, DTO, interface (repository, UoW, ICurrentUser)
      ↑
StoreChecking.Infrastructure  AppDbContext, cấu hình EF, repository, UnitOfWork
      ↑
StoreChecking.Api             Controller, Program.cs, đọc token
```

Mũi tên là chiều **phụ thuộc**, đọc từ dưới lên. `Domain` không biết ai; `Application`
chỉ biết `Domain`; `Api` không hề tham chiếu EF Core.

| Tầng | Chứa gì | KHÔNG được có gì |
|---|---|---|
| Domain | `WorkDay`, `EnglishWord`… và `IOwnedByUser` | mọi thư viện ngoài BCL |
| Application | `EnglishService`, DTO, `IEnglishWordRepository`, `IUnitOfWork` | EF Core, ASP.NET |
| Infrastructure | `AppDbContext`, `*Configuration`, `*Repository`, `DependencyInjection` | HttpContext |
| Api | Controller, `HttpCurrentUser`, cấu hình JWT/CORS/Swagger | truy vấn EF |

### Thêm một module mới cần đụng đâu

1. `Domain/Entities/` — thực thể, cho implement `IOwnedByUser`.
2. `Application/<Module>/` — DTO + service, và interface repository ở `Abstractions/`.
3. `Infrastructure/Persistence/Configurations/` — ánh xạ cột (tên cột viết tay, phải khớp
   `db/*.sql`); `Repositories/` — cài đặt.
4. `Infrastructure/DependencyInjection.cs` — đăng ký repository và service.
5. `Api/Controllers/` — controller mỏng, chỉ kiểm tra hình dạng request rồi gọi service.
6. `tests/` — test hợp đồng cho từng endpoint + test cách ly hai user cho từng bảng.

### Vài lựa chọn có chủ ý

- **Application Service thuần, không CQRS/MediatR.** 12 endpoint hiện tại và ~79 hàm sắp
  tới đều là CRUD; một Command + một Handler cho mỗi cái là ~160 file để không đổi lấy
  điều gì. Nếu sau này có use case thật sự phức tạp thì thêm riêng cho nó.
- **Repository trả về entity đã nạp, không trả `IQueryable`.** Trả `IQueryable` thì tầng
  Application vẫn viết truy vấn EF, chỉ là qua một cái tên khác. Kèm lợi ích: không thể
  vấp lại lỗi `.RootElement` trong LINQ, vì không còn projection nào ở tầng trên.
- **Repository không lưu, `IUnitOfWork` mới lưu.** Nhờ vậy một use case đổi nhiều thứ thì
  chúng cùng vào hoặc cùng không vào.
- **`ValidationException` cho input sai, `null`/`false` cho không tìm thấy.** Middleware
  ở `Program.cs` đổi exception thành `400 { error }`; controller đổi `null` thành 404.
  Service nhờ đó không cần biết gì về HTTP.
- **`/health` đi qua `IDatabaseHealth`** chứ không cầm thẳng `AppDbContext`, để project
  `Api` không phải tham chiếu EF Core.

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

## Giai đoạn 1 — Lịch làm ✅ XONG

Hai bảng: `work_days`, `work_month_notes`. 6 endpoint, `db/001-work-calendar.sql`.

Từng có lúc quyết định "ở lại Supabase" (2026-08-25) rồi bị đảo lại; nay Angular đã trỏ
sang qua `work-calendar-api.service.ts`.

**Lưu ý còn nguyên giá trị:** hành vi "ô không ghi chú và không màu thì XOÁ dòng" là hành
vi phá dữ liệu. Có test hợp đồng ghim nó ở cả hai chiều — xoá đúng lúc cần xoá, và KHÔNG
lưu dòng rỗng.

---

## Giai đoạn 2 — Tiếng Anh ✅ XONG

Hai bảng: `english_words`, `speaking_saved`. 6 endpoint, `db/002-english.sql`.

Module đầu tiên chạy hết vòng, và để lại hai bài học vẫn còn dùng:

- **Lỗi 500 ở `GET /words` sống suốt đời endpoint đó** vì `x.Data.RootElement` nằm trong
  câu LINQ — xem mục Quy ước. Nay có test ghim.
- **Phân trang phải có khoá phụ.** `created_at` một mình không đủ: các dòng ghi trong cùng
  transaction trùng nhau đúng từng micro giây, và hoà nhau thì thứ tự không xác định, làm
  trang 2 lặp lại dòng của trang 1. Thêm `Id` là hết.

Phần **sinh câu bằng AI** (`/api/english`, `/api/speaking`) **giữ trên Vercel** — không đụng
database, `GEMINI_API_KEY` đã ở đó.

**URL API:** `https://storechecking.tail631d54.ts.net`, có Funnel nên gọi được từ mạng bất
kỳ. Chứng chỉ Let's Encrypt thật.

---

## Deploy — GitHub build, NAS tự cập nhật

✅ **Chạy thật từ 2026-08-27.** NAS đã bỏ ảnh tự build, nay chạy ảnh kéo từ GHCR. Đã đi
trọn một vòng không đụng tay vào NAS: commit `ced840e` → Actions build → watchtower kéo →
`XONG. NAS đang chạy bản ced840e, database nối được: True`.

Trong vòng đó script báo "máy chủ chưa trả lời" 16 lần liền. **API không hề chết** — đo
lại mới ra: `curl` mở kết nối mới mỗi lần dò, và **bắt tay TLS qua Funnel mất 0,9 đến hơn
20 giây**, vượt ngưỡng chờ 20 giây cũ. Tách theo giai đoạn: DNS 8ms, TCP 0,11s, **TLS
0,9-8,1s**, bản thân API chỉ 0,3-0,4s. Đã nâng ngưỡng trong `deploy.ps1` lên 45 giây.

**App Angular không dính chuyện này**: trình duyệt giữ kết nối, chỉ lời gọi đầu tiên trả
giá bắt tay, các lời gọi sau ~0,3s. Đo bằng cách tái dùng kết nối: 1,44s cho lần đầu rồi
0,30 / 0,36 / 0,30 / 0,39s.

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

Phải chạy trong **PowerShell**. Gõ dòng đó ở Command Prompt thì cmd không chạy `.ps1`,
nó đưa cho chương trình liên kết với đuôi file rồi trả lại dấu nhắc — **không báo lỗi
gì cả**, trông y như đã chạy xong. Lỡ đang ở cmd thì: `powershell -File .\tools\deploy.ps1`.

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

## ↩️ ĐẢO CHIỀU 2026-08-27: chuyển TẤT CẢ sang .NET, trừ đơn hàng

Quyết định ngày 2026-08-25 (giữ mọi thứ ở Supabase) **đã bị thay thế**. Nay: kho hàng,
chi tiêu, marketing, video, ghi chú — **tất cả chuyển sang .NET trên NAS**. Chỉ `orders`
ở lại Supabase.

Lý do đổi ý: muốn gom về một nơi để **dễ bảo trì lâu dài**, chứ không phải vì Supabase
có vấn đề gì. Đó cũng là lý do làm Clean Architecture — với ~81 hàm thì kiến trúc trả
giá xứng đáng; với 12 endpoint như trước thì không.

### Lý do cũ nào đã được xử lý

- **Trang `/order` công khai** — xử lý bằng cách để `orders` ở lại Supabase. Khách quét QR
  vẫn dùng được, không phụ thuộc mạng nhà.
- **Mất RLS** — đang được thay bằng bộ test hợp đồng: mỗi bảng có test chứng minh user B
  không đọc/sửa/xoá được dòng của A, kể cả khi biết Id. Cộng thêm một test quét toàn bộ
  entity trong `AppDbContext` và **đỏ nếu có bảng nào thiếu `HasQueryFilter`** — biến quy
  ước phải-nhớ thành thứ máy tự bắt.

### Lý do cũ vẫn còn nguyên, chấp nhận đánh đổi

- **Nhà thành điểm chết duy nhất.** *Mất điện thì không lo* — có pin lưu trữ của hệ solar,
  NAS tự chạy lại sau ít phút. Còn lại vẫn đúng: đứt cáp/mất Internet, hỏng ổ cứng, hoặc
  Tailscale trục trặc là mất tất cả trừ trang đặt hàng. Đã biết và chấp nhận.
- **Viết lại 79 hàm không mang lại tính năng mới nào.** Giá trị nằm ở chỗ dễ bảo trì về
  sau, không nằm ở thứ người dùng nhìn thấy.

### Vẫn giữ

- `orders` **ở lại Supabase vĩnh viễn** — không bàn lại.
- Phần sinh câu bằng AI (`/api/english`, `/api/speaking`) **giữ trên Vercel** — không đụng
  database, `GEMINI_API_KEY` đã ở đó.
- Solar, video, file lớn vẫn ở NAS như cũ.

---

## Bản đồ migrate — 22 bảng/view, 79 hàm

Khảo sát ngày 2026-08-27 từ `D:\Dev\StoreChecking` (repo Angular, remote
`InventoryAndTrackingProfit`). Nguồn: `src/app/core/supabase.service.ts` (851 dòng) và
`supabase/*.sql`.

| Module | Bảng / view | Số hàm | Trạng thái |
|---|---|---|---|
| Lịch làm | `work_days`, `work_month_notes` | 5 | ✅ xong |
| Tiếng Anh | `english_words`, `speaking_saved` | 6 | ✅ xong |
| Ghi chú | `notes` | 4 | ✅ xong |
| Đóng gói | `packing_videos` | 7 | ✅ xong (2 hàm chết không port) |
| Chi tiêu | `expense_categories`, `expenses`, `monthly_income`, `v_expense_month_total`, `v_expense_month_category` | 11 | ✅ xong |
| Marketing | `marketing_groups`, `marketing_posts`, `marketing_post_targets`, `post_queue` | 16 | 🔒 **ở lại Supabase** — cron Vercel phụ thuộc |
| Kho hàng | `batches`, `products`, `sales`, `product_damages`, `product_stock`, `batch_summary` | 20 | ✅ xong |
| Đơn hàng | `orders` | 3 | 🔒 **ở lại Supabase vĩnh viễn** |

Thứ tự đã đi: **Ghi chú → Đóng gói → Chi tiêu → Kho hàng.** Nhỏ và ít giá trị trước, Kho
hàng sau cùng vì dữ liệu quý nhất và nhiều thứ khó nhất. Marketing bị bỏ ra giữa chừng khi
phát hiện cron Vercel phụ thuộc vào `post_queue`.

Thêm một endpoint không có trong bản đồ ban đầu: **`GET /api/backup`**, thay
`dumpAllData()`. Nó đọc 8 bảng bằng `row_to_json` và là **chỗ duy nhất trong repo viết SQL
tay, không đi qua owner filter của EF** — lý do và cách canh ghi ngay trong
`BackupRepository`.

### Ba thứ KHÔNG chỉ là "viết lại hàm"

**1. Trigger `check_stock()` là nghiệp vụ nằm trong database.** Nó chặn bán vượt tồn:
`còn lại = nhập − đã bán − đã hỏng`. Trên Supabase, database tự chặn nên lỗi ở app cũng
không bán âm được. Trigger viết bằng plpgsql thuần, **không đụng `auth.uid()`**, nên bê
nguyên sang NAS được. Nên làm cả hai: giữ trigger làm chốt chặn cuối, và kiểm ở tầng
Application để báo lỗi cho tử tế. Cùng lý lẽ với `HasQueryFilter` — đừng để một lớp duy
nhất gánh.

**2. Ảnh marketing nằm ở Supabase Storage, KHÔNG nằm trong Postgres.** Bucket `marketing`,
dùng bởi `uploadMarketingImage`. `pg_dump` không mang nó theo. Phải tải riêng rồi đẩy sang
NAS — đã có sẵn đường: `nas.service.ts` với endpoint `/upload`, đang dùng cho video đóng
gói. Kèm theo: `post_queue.image_url` đang chứa URL công khai của Supabase, chuyển xong
phải viết lại toàn bộ URL trong bảng đó.

**3. `api/cron-post.js` trên Vercel đọc/ghi `post_queue` bằng `SUPABASE_SERVICE_ROLE_KEY`,
bỏ qua RLS.** Đây là **máy gọi máy**, không có người đăng nhập. Khi `post_queue` chuyển
sang NAS, cron đó phải gọi API .NET — mà API .NET hiện chỉ chấp nhận JWT người dùng của
Supabase. **Cần thêm một đường xác thực mới cho máy** (API key hoặc token dịch vụ). Đây là
lỗ hổng thiết kế thật, phải giải trước khi chuyển module Marketing.

`api/order.js` cũng dùng service key, nhưng nó chỉ chạm `orders` — bảng ở lại Supabase,
nên không ảnh hưởng.

### Việc nhỏ hơn cần nhớ

- Video đóng gói **đã nằm trên NAS rồi**; Supabase chỉ giữ metadata. Chuyển nhẹ nhàng.
- 4 view (`product_stock`, `batch_summary`, `v_expense_month_*`) bê sang được, nhưng phải
  **bỏ `with (security_invoker = true)`** — đó là thứ của RLS, bên NAS không có.
- Mọi bảng đều có `user_id ... default auth.uid() references auth.users(id)`. Bỏ cả
  default lẫn khoá ngoại khi bê sang, y như đã làm ở `db/001` và `db/002`.
- View cũng có `user_id`, nên vẫn đặt `HasQueryFilter` được — và test canh gác sẽ đòi.

---

## Cách truy cập API từ ngoài — TAILSCALE + FUNNEL

`https://storechecking.tail631d54.ts.net`, có bật **Funnel**.

Ban đầu định để tailnet-only cho kín, nhưng KHÔNG KHẢ THI: app phục vụ từ Vercel là trang
công khai, mà trình duyệt cấm trang công khai gọi vào mạng nội bộ. Bật Funnel là cách duy
nhất để app trên Vercel gọi được API trên NAS.

Hệ quả kèm theo: **thiết bị không cần bật Tailscale nữa** — địa chỉ đã công khai. Bảo vệ
nằm ở JWT: mọi endpoint dữ liệu đều đòi token Supabase hợp lệ, chỉ `/health` là mở.

### ⚡ Nhưng BẬT Tailscale trên điện thoại thì nhanh hơn nhiều

Cửa ngõ Funnel nằm ở **Tokyo** — `nas-uploader` và `storechecking` cùng phân giải ra
`103.84.155.153` / `103.84.155.217`, tra ra NetActuate AS36236, Tokyo JP.

Nên khi **tắt** Tailscale, mọi byte đi: điện thoại → đường lên nhà mạng → Tokyo → đường
xuống → NAS. Cái NAS cách điện thoại vài mét mà dữ liệu bay sang Nhật rồi quay về.

Khi **bật** Tailscale, MagicDNS phân giải cùng tên đó ra địa chỉ tailnet `100.x`, hai máy
nối thẳng qua LAN. Cùng một URL, chỉ khác nó trỏ đi đâu.

**Đo được, 2026-08-27:** upload video đóng gói chậm hẳn khi tắt Tailscale, nhanh khi bật —
chủ repo tự thử ra. Với API thì biểu hiện nhẹ hơn nhưng cùng gốc: bắt tay TLS qua Funnel
luôn tốn ~1 giây, chính là mấy vòng đi-về Tokyo.

Kết luận thực dụng:

- **Ở nhà, quay clip đóng gói thì bật Tailscale trên điện thoại.** Đây là thứ quyết định
  tốc độ upload, không phải CPU của NAS hay chuyện đa luồng.
- Funnel vẫn để đó làm đường lui: đi ra ngoài, không có Tailscale, mọi thứ vẫn chạy —
  chỉ chậm hơn.
- Lưu ý câu ở trên cần đọc đúng: "không cần bật Tailscale" nghĩa là **vẫn dùng được**,
  không có nghĩa là **nhanh như nhau**.

Hai hướng còn lại nếu sau này muốn bỏ ràng buộc phải bật Tailscale:

- **Tailscale + Funnel** (đang dùng) — công khai, điện thoại không BẮT BUỘC bật app,
  nhưng bật thì nhanh hơn hẳn (xem mục trên)
- **Cloudflare Tunnel** — không cần cài gì trên điện thoại, không mở port, miễn phí.
  ⚠️ Điều khoản Cloudflare cấm dùng bản miễn phí để phục vụ video/file lớn, nên nếu chọn
  hướng này thì **chỉ đẩy API qua đó, video vẫn đi Tailscale như hiện tại**
- **DDNS Synology + Let's Encrypt** — phải mở port 443 ra internet

---

## Việc chưa xong, không thuộc giai đoạn nào

- [ ] **Lỗi JWT "issued at future"** bên Supabase vẫn chưa tìm ra nguyên nhân. API .NET
      đã đặt `ClockSkew = 60s` nên tính năng nào chuyển sang đây là hết lỗi — nhưng gốc
      rễ thì vẫn chưa rõ.
- [x] ~~Angular còn 3 migration Supabase chưa chạy~~ — **không còn cần**. Cả ba
      (`migration-work-calendar.sql`, `migration-work-month-notes.sql`,
      `migration-speaking-saved.sql`) đều thuộc bảng nay đã sang NAS, và schema bên NAS
      dựng từ `db/*.sql` chứ không từ mấy file đó. Giữ lại trong repo Angular như dấu vết
      lịch sử.
