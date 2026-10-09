# Harcama Yönetimi — web arayüzü

Çalışan ve yönetici görünümlerini tek bir sayfada sunan arayüz. Yalnızca vanilla JavaScript (ES modülleri), HTML ve CSS; framework, bundler veya npm yok.

## Dosya yapısı

```
web/
  index.html          Sayfa iskeleti (iki görünüm + üst menü)
  config.js           API tabanı (window.EXPENSE_APP_CONFIG.apiBase)
  css/styles.css      Stiller
  js/app.js           Giriş noktası: görünüm geçişi
  js/employee.js      Çalışan görünümü: taslak formu, liste, gönder/düzenle/sil, ayrıntı
  js/manager.js       Yönetici görünümü: özet, bekleyen talepler, onayla/reddet
  js/details.js       Talep ayrıntısı (alanlar, red gerekçesi, geçmiş); iki görünümde ortak
  js/api.js           fetch sarmalayıcı, hata çevirisi, endpoint fonksiyonları
  js/dom.js           DOM yardımcıları, alan hataları, tıklama koruması (guard)
  js/format.js        Türkçe durum etiketleri, tutar ve tarih biçimi
```

## API tabanı

`web/config.js` içindeki `apiBase`:

| Değer | Anlam |
|---|---|
| `""` (**varsayılan**) | API ile aynı origin. `web/` ve `/api` aynı sunucudan (ör. reverse proxy) servis edilmeli. |
| `"http://localhost:5000"` | Farklı origin. API'de CORS açılmadan tarayıcı istekleri engellenir. |

Not: Backend şu an CORS başlığı göndermiyor. Bu yüzden varsayılan değer aynı origin'dir.

## Çalıştırma

ES modülleri `file://` üzerinden çalışmaz; sayfayı bir HTTP sunucusundan açın. Örnek (API'yi aynı origin'de sunacak bir proxy gerekir):

```bash
dotnet run --project src/ExpenseApi          # API, varsayılan http://localhost:5000
# web/ dizinini /api yönlendirmesi olan bir sunucudan servis edin
```

## Görünümler

- **Çalışan**: taslak oluştur / düzenle / sil / onaya gönder. Liste durum filtresiyle daraltılabilir. Yalnızca `Draft` talepler için Düzenle, Onaya gönder ve Sil düğmeleri gösterilir. Ayrıntı: durum, red gerekçesi, geçmiş.
- **Yönetici**: durum sayıları ve onaylanan toplam (`GET /api/approvals/summary`), bekleyen talepler, inceleme paneli, onayla, reddet (gerekçe zorunlu; boşsa istek gönderilmez).

Görünüm seçimi üst menüdeki sekmelerle yapılır. Kimlik doğrulama yoktur.

## Davranış

- Gönder, kaydet, onayla, reddet ve liste işlemleri sürerken ilgili düğmeler devre dışıdır (`guard`).
- Hatalar: 400 alan hataları ilgili alanın altında gösterilir; 404/409 ve 5xx için Türkçe metin gösterilir; ağ hatasında API adresini kontrol etmesi söylenir.
- Tarih `YYYY-MM-DD`, tutar sayı olarak gönderilir; ekranda `1.234,50` biçiminde gösterilir.
