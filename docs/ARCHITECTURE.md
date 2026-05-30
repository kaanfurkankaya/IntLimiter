# IntLimiter Architecture

IntLimiter uc parcali mimariyle kuruldu:

```text
IntLimiter.Client  <Named Pipe IPC>  IntLimiter.Service  ->  IntLimiter.DriverBridge
                                              |
                                              v
                                      IntLimiter.Core
```

## IntLimiter.Client

WPF/.NET 8 desktop uygulamasidir. Admin manifest `requireAdministrator` olarak ayarlandi.

Gorevleri:

- Service durumunu gosterir.
- Aktif process listesini gosterir.
- Global upload/download rule ekler.
- Secili process icin upload/download rule ekler.
- Rule enable/disable bilgisini service'e geri yollar.
- Rule siler.
- Stop all limits komutunu gonderir.
- Loglari gosterir.

UI, service ile `NamedPipeServiceControlClient` uzerinden async konusur.

## IntLimiter.Service

.NET Worker Service olarak yazildi ve `Microsoft.Extensions.Hosting.WindowsServices` ile Windows Service modunu destekler.

Gorevleri:

- ProgramData altinda rule store ve log dosyasini yonetir.
- UI kapansa bile calisir.
- Named pipe IPC server'ini barindirir.
- WinDivert motoruna veya QoS fallback'e kurallari uygular.
- Service kapanirken WinDivert handle'larini kapatir ve QoS fallback policy'lerini temizler.

IPC pipe adi:

```text
IntLimiter.Service
```

## IntLimiter.Core

Ortak modeller ve arayuzler:

- `BandwidthRule`
- `ProcessIdentity`
- `NetworkFlow`
- `LimiterRuntimeStatus`
- `ITrafficLimiter`
- `IRuleStore`
- `IProcessNetworkMonitor`
- `IServiceControlClient`
- `TokenBucket`
- `JsonRuleStore`
- `ProcessNetworkMonitor`

Rule store:

```text
%ProgramData%\IntLimiter\rules.json
```

Log:

```text
%ProgramData%\IntLimiter\IntLimiter.log.jsonl
```

## Packet capture ve shaping yolu

WinDivert modu:

1. Service `ip and tcp and !loopback` filtresiyle WinDivert handle acar.
2. IPv4/TCP paketleri user-mode'a gelir.
3. Paket parser source/destination IP ve port bilgisini okur.
4. Outbound paket upload, inbound paket download kabul edilir.
5. Packet 5-tuple, `GetExtendedTcpTable` tablosu ile PID/process identity'ye eslenir.
6. Uyan global ve process rule'lari bulunur.
7. Rule token bucket'larinda byte rezervasyonu yapilir.
8. Gecikme yoksa paket hemen `WinDivertSendEx` ile reinject edilir.
9. Gecikme varsa paket bounded priority queue'ya alinir.
10. Sender worker due time geldiginde paketi reinject eder.

## QoS fallback yolu

WinDivert DLL/driver yuklenemezse service Windows QoS policy fallback'i dener.

Desteklenen fallback rule'lari:

- ProcessName / ProcessPath / PID kaynakli process rule'lari
- Direction: Upload veya Both

Desteklenmeyen fallback rule'lari:

- Download-only
- Full global download shaping

Policy adi her zaman `IntLimiter_` prefix'iyle baslar.

## Logging

Kodda iki seviye log vardir:

- `ILogger<T>`: service console / Windows Service logging pipeline.
- `IAppLog`: UI'da gorunen ve JSON Lines dosyasina yazilan IntLimiter event log'u.

Structured logging service tarafinda `ILogger` message template'leriyle kullanildi; UI event log'u JSONL formatindadir.
