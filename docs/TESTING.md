# IntLimiter Testing

Bu testleri Administrator PowerShell ile calistir.

## 1. Build ve unit test

```powershell
dotnet restore .\IntLimiter.sln
dotnet build .\IntLimiter.sln
dotnet test .\IntLimiter.sln --no-build
```

## 2. WinDivert dosyalarini yerlestir

Resmi WinDivert 2.2 paketini indir:

```text
https://reqrypt.org/windivert.html
```

Service output klasorune su dosyalari koy:

```text
src\IntLimiter.Service\bin\Debug\net8.0-windows\WinDivert.dll
src\IntLimiter.Service\bin\Debug\net8.0-windows\WinDivert64.sys
```

Release publish icin ayni dosyalari publish klasorune koy:

```text
src\IntLimiter.Service\bin\Release\net8.0-windows\win-x64\publish\
```

## 3. Dev modda calistir

```powershell
.\scripts\run-dev-admin.ps1
```

Alternatif olarak iki admin terminal ac:

```powershell
dotnet run --project .\src\IntLimiter.Service\IntLimiter.Service.csproj
dotnet run --project .\src\IntLimiter.Client\IntLimiter.Client.csproj
```

## 4. Global download testi

Limit yokken hiz olc:

```powershell
curl.exe -L -o $env:TEMP\intlimiter-before.bin "https://speed.cloudflare.com/__down?bytes=10000000"
```

UI'da:

- Global Download Limit: `512`
- Unit: `KB/s`
- Global limitleri ekle
- Kurallari uygula

Tekrar indir:

```powershell
curl.exe -L -o $env:TEMP\intlimiter-after.bin "https://speed.cloudflare.com/__down?bytes=10000000"
```

Beklenen: ikinci indirme yaklasik 512 KB/s seviyesine iner. HTTP server, TCP slow start ve cache nedeniyle kisa testlerde dalgalanma normaldir.

## 5. Global upload testi

iperf3 varsa:

```powershell
iperf3.exe -c <server-ip> -t 20
```

UI'da Global Upload Limit gir ve testi tekrarla.

iperf3 yoksa upload testi icin kendi HTTP endpoint'ine dosya gonder:

```powershell
curl.exe -X PUT --data-binary "@$env:TEMP\intlimiter-before.bin" https://<upload-test-endpoint>
```

## 6. Process bazli test

1. UI'da Processleri yenile.
2. `curl.exe`, `chrome.exe`, `msedge.exe` veya test edecegin process'i sec.
3. Per-App Download Limit veya Upload Limit gir.
4. Secili process limitini ekle.
5. Kurallari uygula.
6. Sadece secili uygulamada hiz dusmelidir.

curl icin ornek:

```powershell
curl.exe -L -o $env:TEMP\intlimiter-process.bin "https://speed.cloudflare.com/__down?bytes=10000000"
```

## 7. Limit kaldirma testi

UI'da:

- TÃ¼m limitleri durdur

Ardindan ayni download/upload testini tekrar calistir. Hiz eski haline donmelidir.

## 8. QoS fallback testi

WinDivert dosyalarini service output klasorunden gecici olarak kaldir ve service'i admin olarak baslat.

UI'da bir process icin upload limit ekle. PowerShell ile policy kontrol et:

```powershell
Get-NetQosPolicy -PolicyStore ActiveStore | Where-Object Name -like 'IntLimiter_*'
```

Temizlik:

```powershell
.\scripts\uninstall-service.ps1
```

veya UI'da:

```text
TÃ¼m limitleri durdur
```


