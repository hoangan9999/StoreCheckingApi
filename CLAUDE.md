# Migrate từ Supabase sang .NET — sổ theo dõi

File này ghi lại tiến độ chuyển backend từ **Supabase** sang **.NET 9 + PostgreSQL tự dựng
trên NAS**. Xong giai đoạn nào thì tick giai đoạn đó.

Repo Angular: `D:\Dev\StoreChecking` (remote `hoangan9999/InventoryAndTrackingProfit`).
Phần Tiếng Anh đã trỏ sang API này; phần còn lại vẫn gọi Supabase.
Repo này: backend mới, deploy riêng.

---

## 📌 ĐANG Ở ĐÂU — cập nhật 2026-08-27

Nền tảng đã xong, sẵn sàng rót module vào.

| Việc | Trạng thái |
|---|---|
| Deploy tự động (Actions → GHCR → watchtower) | ✅ chạy thật, một lệnh `.\tools\deploy.ps1` |
| Test hợp đồng | ✅ 44 test, chạy trên CI với Postgres thật |
| Cổng chặn: test đỏ thì không deploy | ✅ `build` job `needs: test` |
| Clean Architecture 4 project | ✅ commit `a83ee20` |
| Query filter tự động theo `IOwnedByUser` | ✅ + 2 test canh gác |
| Schema toàn bộ 22 bảng/view | ✅ `db/001`–`007`, API tự nạp lúc khởi động |
| **Module Ghi chú** | 🔄 backend xong, chờ Angular trỏ sang + chuyển dữ liệu |

### 🔴 ĐANG DỪNG Ở ĐÂY — 2026-08-27, làm tiếp khi về nhà

**Việc dở:** chép dữ liệu từ Supabase sang NAS, một lần cho cả 17 bảng.
Schema đã xong hết (`db/001`–`007`, API tự nạp). Chỉ còn đúng bước chép dữ liệu.

Quyết định: **chép một lần cho cả 17 bảng**, không chia theo module. Lý do chủ repo đưa
ra: sản phẩm chỉ một người dùng và sẽ không nhập dữ liệu mới trong lúc migrate, nên không
có chuyện hai bên lệch nhau — thứ vốn là lý lẽ duy nhất chống lại việc chép gộp.

**Chép ở đâu:** trong container `storechecking-db` (ảnh `postgres:16-alpine` nên có sẵn
`psql` và `pg_dump`, không cần Docker). Script: `tools/migrate-all-inside-db.sh`.

**Vướng cái gì:**

1. Terminal của Container Manager quá dở: nút Create là "chạy chương trình gì" chứ không
   phải nơi gõ lệnh, mỗi lần Create lại là một tiến trình mới nên `export` không giữ được
   biến, và khung `bash` cuối cùng thì không gõ được. → Nên bật SSH:
   Control Panel → Terminal & SNMP → Enable SSH service, rồi
   `ssh <user>@192.168.1.76` và `sudo docker exec -it storechecking-db bash`.
2. Không nối được Supabase bằng direct connection (IPv6-only). Phải dùng **Session
   pooler** — xem mục Quy ước.

**Thông tin đã xác định, khỏi tìm lại:**

- Vùng AWS của project Supabase: **ap-southeast-2** (suy từ IPv6 `2406:da1c:16f1:f600::`
  nằm trong `2406:da1c::/35` theo `ip-ranges.amazonaws.com`).
- Cả `aws-0-ap-southeast-2` lẫn `aws-1-ap-southeast-2` đều có IPv4; chưa biết project nằm
  ở cái nào, lệnh trong script thử lần lượt cả hai.
- Mật khẩu database bắt đầu bằng `@`, trong URL phải viết `%40`.
- `english_words` (17 dòng) và `speaking_saved` (1 dòng) **đã chép từ trước** — script tự
  bỏ qua bảng nào đã có dữ liệu nên không sợ nhân đôi.

### 🔴 Sự cố kèm theo: API không phản hồi (đang gỡ)

NAS tự tắt rồi được khởi động lại. Diễn biến đã đo được, theo thứ tự thời gian:

1. Ban đầu `curl` lỗi 35, `failed to receive handshake`, `time_appconnect=0` — TLS hỏng
   hẳn. DNS vẫn ra IP công khai `103.84.155.153`, tức Funnel còn đăng ký. Thủ phạm là
   `storechecking-ts`: Container Manager báo *"stopped unexpectedly"* ba lần, up time 2
   phút trong khi các container khác 33-35 phút → nó đang chết rồi tự dựng lại liên tục.
2. Sau vài lần restart, `storechecking-ts` ổn định (`Up for 7 mins`) và endpoint chuyển
   sang **502 Bad Gateway kèm chứng chỉ hợp lệ**. Tailscale đã tốt; giờ là `api:8080`
   không trả lời.
3. `storechecking-api` báo `Up for 33 mins` — tiến trình còn sống nhưng Kestrel không
   nghe cổng.

**Cách phân biệt hai tầng, ghi lại vì rất hữu dụng:** TLS hỏng hẳn = Tailscale;
502 kèm chứng chỉ hợp lệ = Tailscale tốt, backend không trả lời.

**Đã tìm ra một lỗi thiết kế thật và đã sửa:** `SchemaMigrator` gọi `pg_advisory_lock`,
hàm này **chờ vô hạn**. Một kết nối sót lại từ instance chết — chuyện rất dễ xảy ra sau
khi NAS tắt đột ngột — vẫn giữ khoá cho tới khi Postgres thu hồi, và container kế tiếp sẽ
treo im lặng ở đó: container báo "Up" mà không cổng nào mở, không log, không lỗi. Nay đổi
sang `pg_try_advisory_lock` chờ tối đa 60 giây rồi ném lỗi, kèm log mỗi 2 giây. Thà chết
to tiếng hơn treo im lặng. Đây là giả thuyết hàng đầu cho triệu chứng ở mục 3.

**Việc cần làm, theo thứ tự:**

1. **Đọc log `storechecking-api`** (Container Manager → chọn container → Details → Log).
   Đây là bằng chứng quyết định:
   - dừng ở `Kiểm tra schema…` hoặc `Đang nạp schema X` rồi không có gì nữa → đúng là treo
     ở khoá, bản sửa ở trên giải quyết
   - có dòng `Now listening on http://[::]:8080` → API khoẻ, vấn đề nằm ở mạng docker giữa
     `ts` và `api`; chữa bằng Project → Stop → Build để compose dựng lại mạng cho nhất quán
   - Log tab đôi khi báo "No logs available" (đã gặp với `ts`) — lúc đó dùng SSH:
     `sudo docker logs --tail 100 storechecking-api`
2. Deploy bản có `pg_try_advisory_lock`, rồi xem 502 có hết không.
3. Nếu cần API lên gấp: đặt `SCHEMA_AUTOMIGRATE=false` trong `.env` rồi Project → Build.
   Biến này khai báo trong `docker-compose.yml`; đặt `Schema__AutoMigrate` trực tiếp vào
   `.env` KHÔNG có tác dụng vì `.env` của compose chỉ thay biến trong file compose.
4. Kiểm `schema_history` có đủ 7 dòng chưa.

**Đừng lặp lại hai việc vô ích này:**

- `http://192.168.1.76:8140/health` chỉ có nghĩa khi máy **ở cùng mạng nhà**. Vào DSM qua
  QuickConnect thì vẫn là ở ngoài mạng, gọi IP nội bộ sẽ `ERR_CONNECTION_TIMED_OUT` và
  không nói được gì về API.
- `SchemaMigrator` không sai ở phần nạp SQL: log CI của commit `1909a50` cho thấy nó nạp
  trót lọt cả 7 file, kể cả `007-inventory.sql` với hàm plpgsql dấu `$$`, rồi 44 test xanh.
  Vấn đề là ở chỗ chờ khoá, không phải ở chỗ chạy SQL.

### Việc tiếp theo — chuyển module

Thứ tự: **Ghi chú → Đóng gói → Chi tiêu → Marketing → Kho hàng.**

Mỗi module đi đúng vòng này, hết vòng mới sang module sau:

1. ~~Schema~~ — XONG HẾT. `db/001`..`db/007` đã có đủ 22 bảng/view; API tự nạp lúc khởi
   động (`SchemaMigrator`), không phải làm tay trên NAS nữa.
2. Domain → Application → Infrastructure → Api, theo bảng phân tầng ở trên.
3. Test hợp đồng cho từng endpoint + test cách ly hai user cho từng bảng.
4. Deploy — `.\tools\deploy.ps1`. Schema đi theo code, tự nạp.
5. Chuyển dữ liệu **đúng lúc cắt sang**, không chuyển trước:
   `./tools/migrate-from-supabase.sh <bảng cha trước, bảng con sau>` — chạy trên NAS,
   cần `SUPABASE_DB_URL`. Chép theo đúng thứ tự truyền vào vì có khoá ngoại.
6. Angular trỏ sang API mới, rồi mới xoá hàm Supabase tương ứng.

### Hai thứ phải giải TRƯỚC khi tới Marketing

- **Xác thực máy-gọi-máy.** `api/cron-post.js` trên Vercel đọc/ghi `post_queue` bằng
  `SUPABASE_SERVICE_ROLE_KEY`. API .NET hiện chỉ nhận JWT người dùng. Cần thêm một đường
  cho máy (API key hoặc token dịch vụ) trước khi `post_queue` rời Supabase.
- **`post_queue.image_url`** đang trỏ tới Supabase Storage. Chủ repo sẽ **tự upload lại
  ảnh** lên NAS, nhưng URL trong bảng vẫn phải viết lại khi chuyển.

### Một thứ phải giải TRƯỚC khi tới Kho hàng

- **Trigger `check_stock()`** — nghiệp vụ chặn bán vượt tồn đang nằm trong database.
  plpgsql thuần, không đụng `auth.uid()`, bê nguyên sang được. Làm cả hai lớp: giữ trigger
  làm chốt cuối, thêm kiểm ở Application để báo lỗi tử tế.

### Việc lặt vặt còn nợ

- [ ] **Thu hồi token GHCR.** Token đang dùng có quyền `repo` (đọc/ghi mọi repo riêng tư)
      trong khi chỉ cần `read:packages`, và nó nằm dạng chữ thường ở hai chỗ trên NAS:
      `watchtower/config.json` và `/root/.docker/config.json`.
- [ ] Bỏ `listEnglishWords` / `addEnglishWord` / `*SavedSentence*` khỏi `supabase.service.ts`
      bên repo Angular — code chết, không còn chỗ nào gọi.
- [ ] Angular còn 3 migration Supabase chưa chạy (xem mục cuối file).

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
| Lịch làm | `work_days`, `work_month_notes` | 5 | ✅ có API, Angular chưa trỏ sang |
| Tiếng Anh | `english_words`, `speaking_saved` | 6 | ✅ xong |
| Ghi chú | `notes` | 4 | 🔄 API xong, Angular chưa trỏ sang |
| Đóng gói | `packing_videos` | 7 | ⬜ chưa |
| Chi tiêu | `expense_categories`, `expenses`, `monthly_income`, `v_expense_month_total`, `v_expense_month_category` | 11 | ⬜ chưa |
| Marketing | `marketing_groups`, `marketing_posts`, `marketing_post_targets`, `post_queue` | 16 | ⬜ chưa |
| Kho hàng | `batches`, `products`, `sales`, `product_damages`, `product_stock`, `batch_summary` | 20 | ⬜ chưa |
| Đơn hàng | `orders` | 3 | 🔒 **ở lại Supabase vĩnh viễn** |

Thứ tự chuyển: **Ghi chú → Đóng gói → Chi tiêu → Marketing → Kho hàng.** Nhỏ và ít giá
trị trước, đúng nguyên tắc đã có. Kho hàng đi sau cùng vì dữ liệu quý nhất và có nhiều
thứ khó nhất.

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
