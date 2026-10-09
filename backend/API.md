# Todo API Dokümantasyonu

ASP.NET Core 8 Minimal API + SQLite (EF Core) ile yazılmış basit bir Todo servisi.

- **Base URL:** `http://localhost:5000`
- **Base path:** `/api/todos`
- **CORS:** Her origin, method ve header'a izin verilir.
- **İçerik tipi:** İstek ve yanıt gövdeleri JSON'dur (alan adları camelCase).

## Çalıştırma

.NET 8 SDK gerekir.

```bash
cd backend/TodoApi && dotnet run
```

SQLite veritabanı dosyası (`todos.db`) ilk çalıştırmada çalışma dizininde otomatik oluşturulur.

### Ortam değişkenleri

| Değişken                     | Varsayılan               | Açıklama                      |
|------------------------------|--------------------------|-------------------------------|
| `ConnectionStrings__Default` | `Data Source=todos.db`   | SQLite bağlantı cümlesi       |

```bash
ConnectionStrings__Default="Data Source=/tmp/other.db" dotnet run
```

## Veri modeli: `Todo`

| Alan          | Tip     | Açıklama                                                                 |
|---------------|---------|--------------------------------------------------------------------------|
| `id`          | int     | Sunucu tarafından atanır.                                                |
| `title`       | string  | Zorunlu. Baştaki/sondaki boşluklar kırpılır; kırpıldıktan sonra 1–200 karakter. |
| `isCompleted` | bool    | Oluşturulurken her zaman `false`.                                        |
| `createdAt`   | string  | ISO-8601 UTC zaman damgası, `Z` ile biter. Sunucu atar, değiştirilemez.  |

```json
{
  "id": 1,
  "title": "Süt al",
  "isCompleted": false,
  "createdAt": "2026-10-09T10:43:41.4308458Z"
}
```

## Endpoint'ler

`{id}` bir tam sayı olmalıdır; tam sayı olmayan bir değer (ör. `/api/todos/abc`) `404` döner.

### 1. Tüm todo'ları listele

`GET /api/todos`

**Request body:** yok

**Başarılı yanıt:** `200 OK`, `id`'ye göre artan sırada `Todo` dizisi. Kayıt yoksa `[]`.

```json
[
  { "id": 1, "title": "Süt al", "isCompleted": false, "createdAt": "2026-10-09T10:43:41.4308458Z" },
  { "id": 2, "title": "Faturayı öde", "isCompleted": true, "createdAt": "2026-10-09T10:45:02.1200000Z" }
]
```

**Hata kodları:** yok

### 2. Tek bir todo getir

`GET /api/todos/{id}`

**Request body:** yok

**Başarılı yanıt:** `200 OK`

```json
{ "id": 1, "title": "Süt al", "isCompleted": false, "createdAt": "2026-10-09T10:43:41.4308458Z" }
```

**Hata kodları:**
- `404 Not Found`: Bu `id` ile kayıt yok (boş gövde).

### 3. Todo oluştur

`POST /api/todos`

**Request body:**

```json
{ "title": "  Süt al  " }
```

**Başarılı yanıt:** `201 Created`. `Location` header'ı yeni kaydı gösterir (ör. `Location: /api/todos/1`).

```json
{ "id": 1, "title": "Süt al", "isCompleted": false, "createdAt": "2026-10-09T10:43:41.4308458Z" }
```

**Hata kodları:**
- `400 Bad Request`: `title` eksik, boş veya 200 karakterden uzun ([doğrulama hatası formatı](#400-doğrulama-hatası-formatı)).

### 4. Todo güncelle

`PUT /api/todos/{id}`

Tam güncellemedir: `title` ve `isCompleted` birlikte gönderilir. `isCompleted` gönderilmezse `false` kabul edilir.

**Request body:**

```json
{ "title": "Süt ve ekmek al", "isCompleted": true }
```

**Başarılı yanıt:** `200 OK`, güncellenmiş kayıt (`id` ve `createdAt` değişmez).

```json
{ "id": 1, "title": "Süt ve ekmek al", "isCompleted": true, "createdAt": "2026-10-09T10:43:41.4308458Z" }
```

**Hata kodları:**
- `404 Not Found`: Bu `id` ile kayıt yok (boş gövde). Kayıt kontrolü doğrulamadan **önce** yapılır; yani olmayan bir `id` için geçersiz bir `title` gönderilse bile `404` döner.
- `400 Bad Request`: `title` eksik, boş veya 200 karakterden uzun ([doğrulama hatası formatı](#400-doğrulama-hatası-formatı)).

### 5. Todo sil

`DELETE /api/todos/{id}`

**Request body:** yok

**Başarılı yanıt:** `204 No Content` (boş gövde)

**Hata kodları:**
- `404 Not Found`: Bu `id` ile kayıt yok (boş gövde).

## 400 doğrulama hatası formatı

Doğrulama hataları `Content-Type: application/problem+json` ile ValidationProblem (RFC 9110 problem details) olarak döner. Hatalar `errors` altında alan adına göre gruplanır.

`title` eksik, `null`, boş ya da sadece boşluk ise:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "title": ["Title is required."]
  }
}
```

`title` kırpıldıktan sonra 200 karakterden uzunsa:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "title": ["Title must be at most 200 characters."]
  }
}
```

> Not: Gövde geçerli bir JSON değilse ASP.NET Core yine `400` döner, ancak bu yanıt yukarıdaki ValidationProblem formatında **değildir**.
