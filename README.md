# Todo API

.NET 8 minimal API, EF Core ve SQLite ile yazılmış basit bir Todo CRUD servisi.

## Gereksinimler

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

## Çalıştırma

```bash
# 1. EF Core CLI aracını geri yükle (repo'da yerel tool olarak tanımlı)
dotnet tool restore

# 2. Veritabanını oluştur / migration'ları uygula
dotnet ef database update --project src/TodoApi

# 3. Uygulamayı başlat
dotnet run --project src/TodoApi
```

Uygulama açılışta bekleyen migration'ları otomatik uygular, yani 2. adım isteğe bağlıdır.
SQLite dosyası (`todos.db`) uygulamanın çalıştığı dizinde oluşturulur. Bağlantı cümlesi
`src/TodoApi/appsettings.json` içindeki `ConnectionStrings:Default` ile değiştirilebilir.

Konsolda yazan adresi kullanın (varsayılan: `http://localhost:5xxx`). Belirli bir port için:

```bash
dotnet run --project src/TodoApi --urls http://localhost:5000
```

## Endpointler

| Metot  | Yol           | Açıklama                 | Başarılı yanıt  |
|--------|---------------|--------------------------|-----------------|
| GET    | `/todos`      | Tüm todo'ları listeler   | `200 OK`        |
| GET    | `/todos/{id}` | Tek bir todo getirir     | `200 OK` / `404`|
| POST   | `/todos`      | Yeni todo oluşturur      | `201 Created`   |
| PUT    | `/todos/{id}` | Todo'yu günceller        | `200 OK` / `404`|
| DELETE | `/todos/{id}` | Todo'yu siler            | `204 No Content` / `404` |

`title` boş olamaz ve en fazla 200 karakter olabilir; aksi halde `400` (ValidationProblem) döner.

### Model

```json
{
  "id": 1,
  "title": "Süt al",
  "isCompleted": false,
  "createdAt": "2026-09-24T20:22:29.1029439Z"
}
```

`id` ve `createdAt` (UTC) sunucu tarafından atanır.

### Örnek istekler

```bash
curl -X POST http://localhost:5000/todos -H "Content-Type: application/json" -d '{"title":"Süt al"}'
curl http://localhost:5000/todos
curl http://localhost:5000/todos/1
curl -X PUT http://localhost:5000/todos/1 -H "Content-Type: application/json" -d '{"title":"Süt al","isCompleted":true}'
curl -X DELETE http://localhost:5000/todos/1
```

## Yeni migration ekleme

```bash
dotnet ef migrations add <MigrationAdi> --project src/TodoApi
```
