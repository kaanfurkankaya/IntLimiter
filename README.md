<p align="center">
  <h1 align="center">🌐 IntLimiter</h1>
  <p align="center">
    <b>Windows için Gerçek Zamanlı Ağ İzleme ve Bant Genişliği Sınırlama Aracı</b>
  </p>
  <p align="center">
    <img src="https://img.shields.io/badge/.NET-9.0-512BD4?style=flat-square&logo=dotnet" alt=".NET 9" />
    <img src="https://img.shields.io/badge/Platform-Windows-0078D4?style=flat-square&logo=windows" alt="Windows" />
    <img src="https://img.shields.io/badge/UI-WPF-68217A?style=flat-square" alt="WPF" />
    <img src="https://img.shields.io/badge/License-MIT-green?style=flat-square" alt="MIT License" />
  </p>
</p>

---

**IntLimiter**, NetLimiter benzeri çalışan açık kaynaklı bir Windows masaüstü uygulamasıdır. Bilgisayarınızdaki tüm işlemlerin ağ trafiğini gerçek zamanlı izlemenizi ve bant genişliğini sınırlamanızı sağlar.

## ✨ Özellikler

| Özellik | Açıklama |
|---------|----------|
| 📊 **Gerçek Zamanlı İzleme** | Her işlem için anlık indirme/yükleme hızları |
| 🔒 **İşlem Bazlı Limitleme** | Tek bir uygulamanın bant genişliğini sınırlayın |
| 🌐 **Komple PC Limiti** | Tüm bilgisayar trafiğini tek tuşla sınırlayın |
| 📌 **Sistem Tepsisi** | Arka planda sessizce çalışır, tepsiden yönetin |
| 🎨 **Win11 Tasarım** | Windows 11 Görev Yöneticisi tarzı modern koyu tema |
| 🔍 **Arama & Filtreleme** | İşlem adı veya PID ile anında filtreleyin |
| 🛡️ **Otomatik Temizlik** | Çıkışta tüm limitler otomatik kaldırılır |

## 📸 Ekran Görüntüsü

> _Ekran görüntüsü eklemek için `screenshots/` klasörüne resim ekleyin ve aşağıdaki satırı düzenleyin:_
>
> `![IntLimiter Ana Ekran](screenshots/main.png)`

## 🚀 Kurulum

### Gereksinimler

- Windows 10/11
- [.NET 9 Runtime](https://dotnet.microsoft.com/download/dotnet/9.0)
- **Yönetici (Administrator) yetkisi** — ETW izleme ve QoS politikaları için gereklidir

### Derleyerek Çalıştırma

```bash
git clone https://github.com/KULLANICI_ADI/IntLimiter.git
cd IntLimiter
dotnet run --project IntLimiter
```

> ⚠️ Uygulama çalışırken UAC izni isteyecektir. Bu, ağ trafiğini izlemek ve sınırlamak için gereklidir.

### Yayınlanmış Sürüm

```bash
dotnet publish IntLimiter -c Release -r win-x64 --self-contained
```

Çıktı: `IntLimiter/bin/Release/net9.0-windows/win-x64/publish/`

## 📖 Kullanım

### Ağ Trafiğini İzleme
Uygulama açıldığında tüm aktif işlemler otomatik listelenir. Her işlem için:
- ↓ **İndirme hızı** (mavi)
- ↑ **Yükleme hızı** (yeşil)
- Toplam indirilen/yüklenen veri

### İşlem Bazlı Limit
1. Listeden bir işleme **sağ tıklayın**
2. **"Hız Limiti Ayarla"** seçin
3. İndirme/yükleme limiti ve birimini (Kbit/s, Mbit/s, KB/s, MB/s) girin
4. **"Uygula"** butonuna basın

### Tüm PC Limiti
1. Sol paneldeki **"🌐 PC Limiti Ayarla"** butonuna tıklayın
2. İstediğiniz limiti girin
3. **"Uygula"** butonuna basın

### Sistem Tepsisi
- **X butonuna** basınca uygulama kapanmaz, sistem tepsisine küçülür
- Tepsi ikonuna **çift tıklayın** → pencereyi geri açar
- Tepsi ikonuna **sağ tıklayın** → Göster / Çıkış menüsü
- **"Çıkış"** seçildiğinde tüm limitler otomatik kaldırılır

## 🏗️ Mimari

```
IntLimiter/
├── Models/
│   └── ProcessNetworkInfo.cs    # İşlem veri modeli
├── Services/
│   ├── NetworkMonitorService.cs  # ETW ile ağ izleme
│   └── BandwidthLimiterService.cs # QoS ile bant sınırlama
├── ViewModels/
│   └── MainViewModel.cs         # MVVM ViewModel
├── Views/
│   └── LimitDialog.xaml          # Limit ayar penceresi
├── Converters/
│   └── Converters.cs             # Değer dönüştürücüler
├── Resources/
│   └── Styles.xaml               # Win11 koyu tema
├── MainWindow.xaml               # Ana pencere + sistem tepsisi
└── App.xaml                      # Uygulama giriş noktası
```

### Teknolojiler

| Bileşen | Teknoloji |
|---------|-----------|
| Framework | .NET 9, WPF |
| Ağ İzleme | ETW (Event Tracing for Windows) |
| Bant Sınırlama | Windows NetQoS Policies |
| MVVM | CommunityToolkit.Mvvm |
| İkon Çıkarma | System.Drawing.Common |
| Sistem Tepsisi | System.Windows.Forms.NotifyIcon |

### Nasıl Çalışır?

1. **ETW (Event Tracing for Windows)** — Kernel seviyesinde TCP/UDP olaylarını yakalar, her işlem için indirme/yükleme baytlarını sayar
2. **NetQoS Policies** — Windows'un yerleşik Ağ Kalitesi politikalarını kullanarak trafik hızını sınırlar
3. **Smoothing Algorithm** — Hız değerleri kademeli olarak düşer, titreme olmaz

## ⚠️ Bilinen Sınırlamalar

- **İndirme limiti**: Windows NetQoS öncelikle giden trafiği (upload) sınırlar. Gelen trafik (download) sınırlaması işletim sistemi seviyesinde kısıtlıdır.
- **Yönetici yetkisi**: ETW ve QoS politikaları için zorunludur
- **Windows 10/11**: Yalnızca Windows platformunda çalışır

## 📝 Lisans

Bu proje [MIT Lisansı](LICENSE) ile lisanslanmıştır.

## 🤝 Katkıda Bulunma

Pull request'ler memnuniyetle karşılanır! Büyük değişiklikler için lütfen önce bir issue açın.

1. Fork edin
2. Feature branch oluşturun (`git checkout -b feature/yeni-ozellik`)
3. Commit edin (`git commit -m 'Yeni özellik eklendi'`)
4. Push edin (`git push origin feature/yeni-ozellik`)
5. Pull Request açın
