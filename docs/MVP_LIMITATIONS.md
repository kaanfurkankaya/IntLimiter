# MVP Limitations

## Ne calisiyor?

- .NET 8 solution build aliyor.
- WPF client admin manifest ile aciliyor.
- Worker Service named pipe IPC sunuyor.
- Rule'lar JSON olarak restart sonrasi saklaniyor.
- WinDivert DLL/driver mevcutsa IPv4/TCP paketleri yakalanip token bucket ile geciktiriliyor ve tekrar enjekte ediliyor.
- Global upload/download rule'lari WinDivert modunda paket seviyesinde uygulanir.
- Process bazli rule'lar TCP 5-tuple -> PID eslemesi basarili oldugunda uygulanir.
- WinDivert baslatilamazsa QoS fallback process bazli outbound/upload policy uretir.
- Stop all limits WinDivert handle'ini kapatir, queue'yu temizler, `IntLimiter_` QoS policy'lerini siler ve persisted rule'lari temizler.

## Ne calismiyor veya sinirli?

- Custom WFP callout driver yok.
- UDP shaping bu MVP'de uygulanmadi.
- IPv6 shaping bu MVP'de uygulanmadi.
- Per-process hiz gostergesi WinDivert tarafindan yakalanan paketlerden hesaplanir; limiter pasifken sifir gorunebilir.
- QoS fallback download sinirlamaz.
- Global QoS fallback bilerek uygulanmadi; yanlis global OS policy riskini azaltmak icin sadece app/process policy kullanilir.

## TCP/UDP/IPv6 durumu

- IPv4 TCP: desteklenir.
- UDP: dokumante limit, henuz devre disi.
- IPv6: dokumante limit, henuz devre disi.
- Loopback: varsayilan olarak haric tutulur.

## Process mapping limitleri

Paketlerde PID bulunmadigi icin mapping `GetExtendedTcpTable` ile yapilir. Bu yuzden:

- Cok kisa omurlu TCP baglantilari kacabilir.
- SYN/ilk paketlerde mapping henuz hazir olmayabilir.
- SYSTEM veya protected process executable path bilgisi okunamayabilir.
- Ayni remote/local tuple tekrar kullanildiginda kisa sureli yanlis eslesme riski vardir.

## Admin ve driver gereksinimleri

WinDivert icin:

- Service admin/SYSTEM yetkisiyle calismalidir.
- `WinDivert.dll` ve `WinDivert64.sys` service output klasorunde bulunmalidir.
- Driver Windows tarafindan yuklenebilmelidir.

Eksikse service acik hata loglar ve QoS fallback dener.

## Latency ve retransmission etkileri

Bu MVP paketleri user-mode queue'da bekletir. Limit dusukse:

- RTT artar.
- TCP congestion control daha agresif yavaslayabilir.
- Queue overflow durumunda paket dusurulur, TCP retransmission gorulebilir.
- Cok yuksek hizlarda user-mode packet path CPU maliyeti yaratabilir.

## Limitlerin dogru calismayabilecegi durumlar

- VPN, proxy, WSL, Hyper-V veya sanal adapter path'leri.
- TLS/HTTP3/QUIC gibi UDP tabanli trafik.
- IPv6 oncelikli uygulamalar.
- Process path'i okunamayan protected/system process'ler.
- Baska firewall/VPN/packet filter araclarinin WinDivert/WFP davranisini degistirmesi.
