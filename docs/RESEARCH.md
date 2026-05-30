# IntLimiter Research

Bu dokuman NetLimiter benzeri bir Windows bant genisligi sinirlayicinin genel, public ve dokumante edilmis mimarisini ozetler. Proprietary NetLimiter kodu kopyalanmadi ve reverse engineering yapilmadi.

## NetLimiter benzeri uygulamalar nasil calisir?

NetLimiter'in kendi public dokumantasyonu urunun uc temel parcadan olustugunu soyler: Driver, Service ve Client. Bu modelde driver ag trafigine dusuk seviyede dokunur, service kurallari ve kalici calisma durumunu yonetir, client ise sadece kullanici arayuzu ve kontrol katmanidir.

Kaynak: https://netlimiter.com/docs/internals/3-netlimiter-components

Genel tasarim:

- Driver veya paket yakalama katmani TCP/IP stack uzerindeki paketleri gorur, geciktirir, bloklar veya tekrar enjekte eder.
- Windows Service admin/SYSTEM yetkisiyle calisir, UI kapansa bile kurallari uygular.
- Desktop Client service ile IPC uzerinden konusur, process listesini ve kurallari gosterir.

## Normal masaustu uygulamasi neden yetmez?

Standart user-mode UI uygulamasi process listesi okuyabilir ve socket acabilir, ancak baska process'lerin inbound/download paketlerini TCP/IP stack'e girmeden once durdurup zamanlayamaz. Download shaping icin paketlerin yerel makineye ulasmasi ile uygulamaya teslim edilmesi arasinda bir enforcement noktasi gerekir. Bu nokta normal WPF/WinUI uygulamasinda yoktur.

## WFP ve callout driver neden uzun vadeli dogru cozum?

Microsoft Windows Filtering Platform (WFP), Windows network stack icin resmi filtreleme altyapisidir. Microsoft dokumantasyonuna gore filter engine TCP/IP tabanli network data uzerinde filtreleme yapar; kernel-mode layer'larda callout driver'lar ek isleme, packet/stream modification ve logging yapabilir. Base Filtering Engine (BFE) filtre konfig urasyonunu ve guvenlik modelini koordine eder.

Kaynaklar:

- https://learn.microsoft.com/en-us/windows/win32/fwp/about-windows-filtering-platform
- https://learn.microsoft.com/en-us/windows/win32/fwp/windows-filtering-platform-architecture-overview
- https://learn.microsoft.com/en-us/windows-hardware/drivers/network/introduction-to-windows-filtering-platform-callout-drivers

Uzun vadede profesyonel urun icin WFP callout driver dogru yoldur cunku:

- Inbound ve outbound trafige resmi kernel/user-mode filtreleme noktalarindan erisir.
- ALE, transport, stream ve network layer seviyelerinde daha dogru process/flow baglami saglar.
- Paketleri user-mode'a tasiyip geciktirmenin latency ve performans maliyetini azaltabilir.
- Signed driver, installer, crash recovery ve policy arbitration gibi urunlestirme gereksinimleri daha nettir.

## MVP icin neden WinDivert?

WinDivert, Windows 10/11 uzerinde user-mode uygulamalarin paket capture/filter/drop/reinject yapmasini saglayan public bir paket divert kutuphanesidir. Resmi dokumantasyonu capture, drop, sniff, modify ve reinject yeteneklerini aciklar. Kernel driver yazmadan gercek paket geciktirme denemesi yapilabildigi icin MVP icin daha hizlidir.

Kaynaklar:

- https://reqrypt.org/windivert.html
- https://reqrypt.org/windivert-doc.html
- https://www.nuget.org/packages/SharpDivert

IntLimiter MVP, C# tarafinda SharpDivert binding'i kullanir. WinDivert binary'leri repo icinde bundle edilmez; kullanici resmi WinDivert 2.2 paketinden `WinDivert.dll` ve `WinDivert64.sys` dosyalarini service output klasorune koymalidir.

## Process bazli ag trafigi izleme

Network layer paketinde PID yoktur. MVP bu yuzden IPv4/TCP paketinin 5-tuple bilgisini Windows IP Helper API ile alinan TCP owner PID tablosuna esler. Microsoft `MIB_TCPTABLE_OWNER_PID` dokumani, bu tablonun GetExtendedTcpTable cagrisi ile PID bilgili IPv4 TCP linklerini dondurdugunu aciklar.

Kaynak:

- https://learn.microsoft.com/en-us/windows/win32/api/tcpmib/ns-tcpmib-mib_tcptable_owner_pid

Bu yontem kurulmus TCP flow'larda calisir. SYN anlari, cok kisa omurlu baglantilar, UDP ve bazi sistem process'leri icin esleme eksik olabilir.

## Upload/download sinirlama mantigi

IntLimiter WinDivert modunda:

- Upload: WinDivert adres bilgisinde outbound olan paketler.
- Download: inbound olan paketler.
- Her paket icin global ve process kurallari bulunur.
- Paket boyutu token bucket'a rezerve edilir.
- Token yeterliyse paket hemen reinject edilir.
- Token yetmezse paket bounded priority queue'ya alinir ve due time geldiginde reinject edilir.
- Queue limitini asan paketler dusurulur ve loglanir.

## Token bucket algoritmasi

Her rule icin ayri bucket vardir:

- Refill rate = `LimitBytesPerSecond`
- Capacity = en az 1 saniyelik trafik
- Paket boyutu token'dan kucuk/esitse aninda gonderilir
- Token yetmezse eksik byte miktari / refill rate kadar gecikme hesaplanir
- Ayni pakete birden fazla rule uyarsa en buyuk gecikme uygulanir

## Windows QoS Policy fallback sinirlari

Windows PowerShell `New-NetQosPolicy`, uygulama path condition ve throttle rate ile outbound QoS policy uretebilir. Microsoft ornekleri `-AppPathNameMatchCondition` ve `-ThrottleRateActionBitsPerSecond` kullanimini gosterir.

Kaynak:

- https://learn.microsoft.com/powershell/module/netqos/new-netqospolicy

Sinirlar:

- Pratik fallback olarak esasen outbound/upload tarafinda kullanilir.
- Full download shaping saglamaz.
- Global tum sistem download siniri icin yeterli degildir.
- Policy'ler OS QoS pipeline'ina baglidir; paketleri IntLimiter process'i icinde tek tek zamanlamaz.

IntLimiter fallback modda sadece `IntLimiter_` prefix'li policy'ler olusturur ve temizler.
