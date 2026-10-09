(() => {
  'use strict';

  // Sözleşme: backend'in API'si ile aynı origin'den çalışır (bkz. record_result notları).
  const DURUM_ETIKETLERI = {
    Draft: 'Taslak',
    Pending: 'Onay bekliyor',
    Approved: 'Onaylandı',
    Rejected: 'Reddedildi',
  };
  const DURUM_SINIFLARI = { Draft: 'taslak', Pending: 'bekliyor', Approved: 'onaylandi', Rejected: 'reddedildi' };
  const VARSAYILAN_BASLIKLAR = {
    400: 'Lütfen formdaki hataları düzeltin.',
    404: 'Kayıt bulunamadı.',
    409: 'Bu işlem talebin mevcut durumunda yapılamaz.',
  };
  const PARA = new Intl.NumberFormat('tr-TR', { style: 'currency', currency: 'TRY' });

  const icerik = document.getElementById('icerik');
  const islemde = new Set();
  let gorunumNo = 0;
  let bildirim = '';

  // ---------- Ağ katmanı ----------

  class ApiError extends Error {
    constructor({ status = 0, title, detail = '', errors = {} }) {
      super(title);
      this.name = 'ApiError';
      this.status = status;
      this.title = title;
      this.detail = detail;
      this.errors = errors;
    }
  }

  // ProblemDetails gövdesini (errors: { alan: [mesaj] }) ApiError'a çevirir.
  function sorunNesnesi(durum, veri) {
    const d = veri && typeof veri === 'object' ? veri : {};
    const hatalar = {};
    if (d.errors && typeof d.errors === 'object') {
      for (const [alan, mesajlar] of Object.entries(d.errors)) {
        // ASP.NET PascalCase anahtarlarını form alanı adına (camelCase) çevirir.
        hatalar[alan.charAt(0).toLowerCase() + alan.slice(1)] = [].concat(mesajlar).map(String);
      }
    }
    return new ApiError({
      status: durum,
      title: VARSAYILAN_BASLIKLAR[durum] || (durum ? 'Sunucu hatası oluştu.' : 'Beklenmeyen hata.'),
      detail: typeof d.detail === 'string' ? d.detail : '',
      errors: hatalar,
    });
  }

  async function api(yontem, yol, govde) {
    const istek = { method: yontem, headers: { Accept: 'application/json' } };
    if (govde !== undefined) {
      istek.headers['Content-Type'] = 'application/json';
      istek.body = JSON.stringify(govde);
    }

    let yanit;
    try {
      yanit = await fetch(yol, istek);
    } catch {
      throw new ApiError({
        title: 'Bağlantı hatası',
        detail: 'Sunucuya ulaşılamadı. Bağlantınızı kontrol edip tekrar deneyin.',
      });
    }

    const metin = await yanit.text();
    let veri = null;
    if (metin) {
      try { veri = JSON.parse(metin); } catch { veri = null; }
    }
    if (!yanit.ok) throw sorunNesnesi(yanit.status, veri);
    return veri;
  }

  function normalize(hata) {
    if (hata instanceof ApiError) return hata;
    return new ApiError({ title: 'Beklenmeyen hata.', detail: hata && hata.message ? hata.message : '' });
  }

  // Aynı anahtarla çalışan bir işlem varken yeni tıklamayı yok sayar ve düğmeleri kilitler.
  async function tekSeferlik(anahtar, dugmeler, is) {
    if (islemde.has(anahtar)) return;
    islemde.add(anahtar);
    dugmeler.forEach((d) => { d.disabled = true; });
    try {
      return await is();
    } finally {
      islemde.delete(anahtar);
      dugmeler.forEach((d) => { d.disabled = false; });
    }
  }

  // ---------- DOM yardımcıları ----------

  function el(etiket, ozellikler = {}, ...cocuklar) {
    const e = document.createElement(etiket);
    for (const [ad, deger] of Object.entries(ozellikler)) {
      if (deger === null || deger === undefined || deger === false) continue;
      if (ad === 'class') e.className = deger;
      else if (ad.startsWith('on')) e.addEventListener(ad.slice(2), deger);
      else e.setAttribute(ad, deger === true ? '' : String(deger));
    }
    for (const c of cocuklar.flat()) {
      if (c === null || c === undefined || c === false) continue;
      e.append(c instanceof Node ? c : document.createTextNode(String(c)));
    }
    return e;
  }

  const dugmelerIn = (kapsayici) => [...kapsayici.querySelectorAll('button')];

  function baglanti(hedef, metin, sinif = '') {
    return el('a', { href: hedef, class: `dugme ${sinif}`.trim() }, metin);
  }

  function sayfaBasligi(metin, ...eylemler) {
    return el('div', { class: 'sayfa-basligi' },
      el('h2', {}, metin),
      eylemler.length ? el('div', { class: 'eylemler' }, eylemler) : null);
  }

  function yukleniyorKutusu() {
    return el('p', { class: 'durum yukleniyor', role: 'status' }, 'Yükleniyor…');
  }

  function bosKutusu(metin) {
    return el('p', { class: 'durum bos' }, metin);
  }

  function bilgiKutusu(metin) {
    return el('p', { class: 'bilgi' }, metin);
  }

  function bildirimKutusu() {
    if (!bildirim) return null;
    const metin = bildirim;
    bildirim = '';
    return el('p', { class: 'bildirim', role: 'status' }, metin);
  }

  function hataKutusu(hata, yenile) {
    return el('div', { class: 'hata-kutusu', role: 'alert' },
      el('strong', {}, hata.title),
      hata.detail ? el('p', {}, hata.detail) : null,
      yenile ? el('button', { type: 'button', class: 'ikincil', onclick: yenile }, 'Yeniden dene') : null);
  }

  function durumRozeti(durum) {
    return el('span', { class: `rozet ${DURUM_SINIFLARI[durum] || ''}` }, DURUM_ETIKETLERI[durum] || durum);
  }

  function paraBicimi(tutar) {
    return PARA.format(Number(tutar));
  }

  function tablo(basliklar, satirlar) {
    return el('div', { class: 'tablo-sarmalayici' },
      el('table', {},
        el('thead', {}, el('tr', {}, basliklar.map((b) => el('th', { scope: 'col' }, b)))),
        el('tbody', {}, satirlar.map((hucreler) => el('tr', {}, hucreler.map((h) => el('td', {}, h)))))));
  }

  // Yükleme → (başarı | boş | hata) akışını tek yerde yönetir. Eski yanıtlar ekrana yazılmaz.
  async function veriyiYukle(alan, yukle, goster) {
    const no = ++gorunumNo;
    alan.replaceChildren(yukleniyorKutusu());
    try {
      const veri = await yukle();
      if (no === gorunumNo) {
        alan.replaceChildren();
        goster(veri);
      }
    } catch (hata) {
      if (no === gorunumNo) {
        alan.replaceChildren(hataKutusu(normalize(hata), () => veriyiYukle(alan, yukle, goster)));
      }
    }
  }

  // ---------- Form yardımcıları ----------

  function alan(etiket, girdi, ad, zorunlu = false) {
    girdi.id = `f-${ad}`;
    girdi.name = ad;
    return el('div', { class: 'alan' },
      el('label', { for: girdi.id }, zorunlu ? `${etiket} *` : etiket),
      girdi);
  }

  function formTemizle(form) {
    form.querySelectorAll('.alan-hatasi').forEach((e) => e.remove());
    form.querySelectorAll('[aria-invalid]').forEach((e) => e.removeAttribute('aria-invalid'));
    form.querySelector('.form-hatasi').replaceChildren();
  }

  // 400 ProblemDetails.errors alanları ilgili inputun yanında, kalanlar form özetinde gösterilir.
  function formHatasi(form, hata) {
    formTemizle(form);
    const ozet = form.querySelector('.form-hatasi');
    const kalan = [];
    for (const [ad, mesajlar] of Object.entries(hata.errors || {})) {
      const girdi = form.elements.namedItem(ad);
      if (girdi) {
        girdi.setAttribute('aria-invalid', 'true');
        girdi.closest('.alan').append(el('span', { class: 'alan-hatasi' }, mesajlar.join(' ')));
      } else {
        kalan.push(...mesajlar);
      }
    }
    ozet.replaceChildren(
      el('strong', {}, hata.title),
      hata.detail ? el('p', {}, hata.detail) : null,
      kalan.length ? el('ul', {}, kalan.map((m) => el('li', {}, m))) : null);
  }

  function istemciDogrulamaHatasi(form, ad, mesaj) {
    formHatasi(form, new ApiError({ status: 400, title: VARSAYILAN_BASLIKLAR[400], errors: { [ad]: [mesaj] } }));
  }

  function eylemHatasi(alanEl, hata) {
    alanEl.replaceChildren(hataKutusu(hata));
  }

  // "1.234,50" ve "150,5" gibi Türkçe biçimleri kabul eder; geçersizse null döner.
  function tutarCoz(metin) {
    const temiz = String(metin).trim().replace(/\s/g, '').replace(/\.(?=.*,)/g, '').replace(',', '.');
    const sayi = Number(temiz);
    if (temiz === '' || !Number.isFinite(sayi)) return null;
    return Math.round(sayi * 100) / 100;
  }

  // ---------- Yönlendirme ----------

  function rotaCoz() {
    const [yol, sorgu = ''] = location.hash.replace(/^#/, '').split('?');
    const parca = yol.split('/').filter(Boolean).map(decodeURIComponent);
    return { parca, sorgu: new URLSearchParams(sorgu) };
  }

  function go(hedef) {
    if (location.hash === hedef) rotayiCiz();
    else location.hash = hedef;
  }

  function gorunumSecimi(gorunum) {
    document.querySelectorAll('[data-gorunum]').forEach((a) => {
      if (a.dataset.gorunum === gorunum) a.setAttribute('aria-current', 'page');
      else a.removeAttribute('aria-current');
    });
  }

  function rotayiCiz() {
    const { parca, sorgu } = rotaCoz();
    const [gorunum = 'calisan', ikinci, ucuncu] = parca;
    gorunumSecimi(gorunum);

    if (gorunum === 'yonetici') {
      if (!ikinci) return yoneticiBekleyenler();
      if (ikinci === 'ozet') return yoneticiOzet();
      return yoneticiDetay(ikinci);
    }

    if (!ikinci) return calisanListesi(sorgu.get('durum') || '');
    if (ikinci === 'yeni') return calisanForm(null);
    if (ucuncu === 'duzenle') return calisanForm(ikinci);
    return calisanDetay(ikinci);
  }

  // ---------- Çalışan görünümü ----------

  function calisanListesi(filtre) {
    const secici = el('select', { id: 'durum-filtresi' },
      [['', 'Tümü'], ...Object.entries(DURUM_ETIKETLERI)].map(([deger, metin]) =>
        el('option', { value: deger, selected: deger === filtre }, metin)));
    secici.addEventListener('change', () => go(`#/calisan${secici.value ? `?durum=${secici.value}` : ''}`));

    const liste = el('div', { class: 'icerik-alani' });
    icerik.replaceChildren(
      sayfaBasligi('Taleplerim', baglanti('#/calisan/yeni', 'Yeni talep')),
      bildirimKutusu(),
      el('div', { class: 'arac-cubugu' }, el('label', { for: 'durum-filtresi' }, 'Durum:'), secici),
      liste);

    veriyiYukle(liste,
      () => api('GET', `/api/expenses${filtre ? `?status=${encodeURIComponent(filtre)}` : ''}`),
      (kayitlar) => {
        if (!kayitlar.length) {
          liste.append(bosKutusu(filtre
            ? 'Bu durumda talep yok.'
            : 'Henüz talep yok. "Yeni talep" ile ilk taslağınızı oluşturabilirsiniz.'));
          return;
        }
        liste.append(tablo(['Başlık', 'Tutar', 'Tarih', 'Durum'], kayitlar.map((k) => [
          el('a', { href: `#/calisan/${k.id}` }, k.title),
          paraBicimi(k.amount),
          k.date,
          durumRozeti(k.status),
        ])));
      });
  }

  function calisanForm(id) {
    const govde = el('div', { class: 'icerik-alani' });
    icerik.replaceChildren(
      sayfaBasligi(id ? 'Talebi düzenle' : 'Yeni talep'),
      govde);

    if (!id) {
      govde.append(formuOlustur(null));
      return;
    }
    veriyiYukle(govde,
      () => api('GET', `/api/expenses/${id}`),
      (kayit) => {
        if (kayit.status !== 'Draft') {
          govde.append(
            bilgiKutusu(`Bu talep "${DURUM_ETIKETLERI[kayit.status]}" durumunda olduğu için düzenlenemez.`),
            baglanti(`#/calisan/${id}`, 'Talebe dön', 'ikincil'));
          return;
        }
        govde.append(formuOlustur(kayit));
      });
  }

  function formuOlustur(kayit) {
    const yeni = !kayit;
    const form = el('form', { class: 'form', novalidate: true });

    const baslik = el('input', { type: 'text', maxlength: '200', autocomplete: 'off' });
    baslik.value = kayit ? kayit.title : '';
    const aciklama = el('textarea', { rows: '4', maxlength: '2000' });
    aciklama.value = kayit && kayit.description ? kayit.description : '';
    const tutar = el('input', { type: 'text', inputmode: 'decimal', placeholder: 'örn. 150,50', autocomplete: 'off' });
    tutar.value = kayit ? String(kayit.amount).replace('.', ',') : '';
    const tarih = el('input', { type: 'date' });
    tarih.value = kayit ? kayit.date : '';

    const kaydetBtn = el('button', { type: 'submit' }, yeni ? 'Taslağı kaydet' : 'Değişiklikleri kaydet');
    const iptal = baglanti(yeni ? '#/calisan' : `#/calisan/${kayit.id}`, 'İptal', 'ikincil');

    form.append(
      alan('Başlık', baslik, 'title', true),
      alan('Açıklama', aciklama, 'description'),
      alan('Tutar (₺)', tutar, 'amount', true),
      alan('Tarih', tarih, 'date', true),
      el('div', { class: 'form-hatasi', role: 'alert' }),
      el('div', { class: 'eylemler' }, kaydetBtn, iptal));

    form.addEventListener('submit', (e) => {
      e.preventDefault();
      const anahtar = yeni ? 'olustur' : `kaydet:${kayit.id}`;
      tekSeferlik(anahtar, dugmelerIn(form), async () => {
        formTemizle(form);
        const tutarDegeri = tutarCoz(tutar.value);
        if (tutarDegeri === null) {
          istemciDogrulamaHatasi(form, 'amount', 'Geçerli bir tutar girin (örn. 150,50).');
          return;
        }
        const govde = {
          title: baslik.value.trim(),
          description: aciklama.value.trim() || null,
          amount: tutarDegeri,
          date: tarih.value,
        };
        const sonuc = yeni
          ? await api('POST', '/api/expenses', govde)
          : await api('PUT', `/api/expenses/${kayit.id}`, govde);
        bildirim = yeni ? 'Taslak kaydedildi.' : 'Taslak güncellendi.';
        go(`#/calisan/${(sonuc && sonuc.id) || kayit.id}`);
      }).catch((hata) => formHatasi(form, normalize(hata)));
    });

    return form;
  }

  // Ortak detay bloğu: durum, tutar, tarih, açıklama, karar gerekçesi ve geçmiş.
  function detayBilgisi(kayit) {
    const gecmis = kayit.history || [];
    const gerekce = kayit.decisionReason
      || [...gecmis].reverse().find((h) => h.toStatus === 'Rejected' && h.reason)?.reason;
    let gerekceMetni = gerekce;
    if (!gerekceMetni) {
      gerekceMetni = kayit.status === 'Draft' || kayit.status === 'Pending'
        ? 'Henüz karar verilmedi.'
        : 'Gerekçe belirtilmedi.';
    }

    return el('div', {},
      el('dl', { class: 'bilgi-listesi' },
        el('dt', {}, 'Durum'), el('dd', {}, durumRozeti(kayit.status)),
        el('dt', {}, 'Tutar'), el('dd', {}, paraBicimi(kayit.amount)),
        el('dt', {}, 'Tarih'), el('dd', {}, kayit.date),
        el('dt', {}, 'Açıklama'), el('dd', {}, kayit.description || '—'),
        el('dt', {}, 'Karar gerekçesi'), el('dd', {}, gerekceMetni)),
      el('section', { class: 'kart' },
        el('h3', {}, 'Geçmiş'),
        gecmis.length
          ? el('ol', { class: 'gecmis' }, gecmis.map((h) => el('li', {},
            el('div', {}, h.fromStatus
              ? `${DURUM_ETIKETLERI[h.fromStatus]} → ${DURUM_ETIKETLERI[h.toStatus]}`
              : `Oluşturuldu: ${DURUM_ETIKETLERI[h.toStatus]}`),
            h.reason ? el('div', {}, `Gerekçe: ${h.reason}`) : null,
            el('small', {}, new Date(h.occurredAt).toLocaleString('tr-TR')))))
          : bosKutusu('Henüz geçmiş kaydı yok.')));
  }

  function calisanDetay(id) {
    const govde = el('div', { class: 'icerik-alani' });
    icerik.replaceChildren(
      sayfaBasligi('Talep detayı', baglanti('#/calisan', 'Listeye dön', 'ikincil')),
      bildirimKutusu(),
      govde);

    veriyiYukle(govde,
      () => api('GET', `/api/expenses/${id}`),
      (kayit) => {
        govde.append(detayBilgisi(kayit));

        if (kayit.status !== 'Draft') {
          govde.append(bilgiKutusu(
            `Bu talep "${DURUM_ETIKETLERI[kayit.status]}" durumunda olduğu için düzenlenemez veya silinemez.`));
          return;
        }

        const eylemAlani = el('div', { class: 'eylemler', 'aria-live': 'polite' });
        const gonderBtn = el('button', { type: 'button' }, 'Onaya gönder');
        const silBtn = el('button', { type: 'button', class: 'tehlike' }, 'Sil');
        const eylemKutusu = el('div', { class: 'karar' },
          el('div', { class: 'eylemler' },
            baglanti(`#/calisan/${id}/duzenle`, 'Düzenle', 'ikincil'),
            gonderBtn,
            silBtn),
          eylemAlani);
        govde.append(eylemKutusu);

        gonderBtn.addEventListener('click', () => {
          tekSeferlik(`gonder:${id}`, dugmelerIn(eylemKutusu), async () => {
            await api('POST', `/api/expenses/${id}/submit`);
            bildirim = 'Talep onaya gönderildi.';
            go(`#/calisan/${id}`);
          }).catch((hata) => eylemHatasi(eylemAlani, normalize(hata)));
        });

        silBtn.addEventListener('click', () => {
          if (!window.confirm('Bu taslak silinsin mi? Bu işlem geri alınamaz.')) return;
          tekSeferlik(`sil:${id}`, dugmelerIn(eylemKutusu), async () => {
            await api('DELETE', `/api/expenses/${id}`);
            bildirim = 'Taslak silindi.';
            go('#/calisan');
          }).catch((hata) => eylemHatasi(eylemAlani, normalize(hata)));
        });
      });
  }

  // ---------- Yönetici görünümü ----------

  function yoneticiBekleyenler() {
    const liste = el('div', { class: 'icerik-alani' });
    icerik.replaceChildren(
      sayfaBasligi('Onay bekleyen talepler', baglanti('#/yonetici/ozet', 'Özet', 'ikincil')),
      bildirimKutusu(),
      liste);

    veriyiYukle(liste,
      () => api('GET', '/api/manager/expenses/pending'),
      (kayitlar) => {
        if (!kayitlar.length) {
          liste.append(bosKutusu('Onay bekleyen talep yok.'));
          return;
        }
        liste.append(tablo(['Başlık', 'Tutar', 'Tarih'], kayitlar.map((k) => [
          el('a', { href: `#/yonetici/${k.id}` }, k.title),
          paraBicimi(k.amount),
          k.date,
        ])));
      });
  }

  function yoneticiDetay(id) {
    const govde = el('div', { class: 'icerik-alani' });
    icerik.replaceChildren(
      sayfaBasligi('Talep incelemesi', baglanti('#/yonetici', 'Listeye dön', 'ikincil')),
      govde);

    veriyiYukle(govde,
      () => api('GET', `/api/expenses/${id}`),
      (kayit) => {
        govde.append(detayBilgisi(kayit));

        if (kayit.status !== 'Pending') {
          govde.append(bilgiKutusu('Bu talep onay bekleyen durumda değil; karar verilemez.'));
          return;
        }

        const kararAlani = el('div', { class: 'karar' });
        const eylemAlani = el('div', { class: 'eylemler', 'aria-live': 'polite' });
        const onayBtn = el('button', { type: 'button', class: 'birincil' }, 'Onayla');

        const redForm = el('form', { class: 'red-formu', novalidate: true });
        const gerekce = el('textarea', { rows: '3', maxlength: '2000' });
        const redBtn = el('button', { type: 'submit', class: 'tehlike' }, 'Reddet');
        redForm.append(
          alan('Red gerekçesi', gerekce, 'reason', true),
          el('div', { class: 'form-hatasi', role: 'alert' }),
          el('div', { class: 'eylemler' }, redBtn));

        kararAlani.append(
          el('h3', {}, 'Karar'),
          el('div', { class: 'eylemler' }, onayBtn),
          eylemAlani,
          redForm);
        govde.append(kararAlani);

        onayBtn.addEventListener('click', () => {
          tekSeferlik(`karar:${id}`, dugmelerIn(kararAlani), async () => {
            await api('POST', `/api/manager/expenses/${id}/approve`);
            bildirim = 'Talep onaylandı.';
            go('#/yonetici');
          }).catch((hata) => eylemHatasi(eylemAlani, normalize(hata)));
        });

        redForm.addEventListener('submit', (e) => {
          e.preventDefault();
          formTemizle(redForm);
          const metin = gerekce.value.trim();
          // Gerekçe boşsa istek hiç gönderilmez.
          if (!metin) {
            istemciDogrulamaHatasi(redForm, 'reason', 'Red gerekçesi zorunludur.');
            return;
          }
          tekSeferlik(`karar:${id}`, dugmelerIn(kararAlani), async () => {
            await api('POST', `/api/manager/expenses/${id}/reject`, { reason: metin });
            bildirim = 'Talep reddedildi.';
            go('#/yonetici');
          }).catch((hata) => formHatasi(redForm, normalize(hata)));
        });
      });
  }

  function yoneticiOzet() {
    const govde = el('div', { class: 'icerik-alani' });
    icerik.replaceChildren(
      sayfaBasligi('Özet', baglanti('#/yonetici', 'Bekleyenler', 'ikincil')),
      govde);

    veriyiYukle(govde,
      () => api('GET', '/api/manager/summary'),
      (ozet) => {
        const sayilar = ozet.counts || {};
        const toplamSayi = Object.keys(DURUM_ETIKETLERI).reduce((t, d) => t + (sayilar[d] || 0), 0);
        govde.append(el('div', { class: 'ozet-kartlari' },
          Object.keys(DURUM_ETIKETLERI).map((d) => el('div', { class: 'kart' },
            el('span', { class: 'kart-baslik' }, DURUM_ETIKETLERI[d]),
            el('strong', { class: 'kart-sayi' }, String(sayilar[d] || 0))))),
        el('div', { class: 'kart onay-toplam' },
          el('span', { class: 'kart-baslik' }, 'Onaylanan toplam'),
          el('strong', { class: 'kart-sayi' }, paraBicimi(ozet.approvedTotal || 0))));
        if (!toplamSayi) govde.append(bosKutusu('Henüz talep yok.'));
      });
  }

  window.addEventListener('hashchange', rotayiCiz);
  rotayiCiz();
})();
