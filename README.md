# Kargo Takip Web Uygulaması

Bu proje, kargo takip işlemlerini yönetmek için geliştirilmiş bir web uygulamasıdır. Frontend ve Backend olmak üzere iki ana bileşenden oluşmaktadır.

## Özellikler

- Kargo takip numarası ile sorgulama
- UPS kargo durumu otomatik kontrol
- Kargo ekleme, silme ve listeleme
- Gerçek zamanlı teslimat durumu takibi
- Öngörülen teslimat zamanı gösterimi
- Mağaza ve talep ID'si ile takip
- Docker ile kolay deployment

## Teknolojiler

### Backend
- .NET Core Web API
- C#
- Docker

### Frontend
- HTML/CSS/JavaScript
- Nginx
- Docker

## Kurulum

1. Projeyi klonlayın:
```bash


2. Docker ile çalıştırın:
```bash
docker-compose up -d
```

## Portlar

- Frontend: `http://[sunucu-ip]:6786`
- Backend API: `http://[sunucu-ip]:6789`

## API Endpoints

### GET /api/kargo
Tüm kargoları listeler

### POST /api/kargo
Yeni kargo ekler

Örnek request body:
```json
{
    "firma": "UPS",
    "takipNo": "1Z999AA1234567890",
    "magazaID": "M123",
    "talepID": "T456"
}
```

### DELETE /api/kargo/{takipNo}
Belirtilen takip numarasına sahip kargoyu siler

## Geliştirme

Projeyi geliştirme ortamında çalıştırmak için:

1. Backend için:
```bash
cd api
dotnet run
```

2. Frontend için:
```bash
cd frontend
# Statik dosyaları bir web sunucusu ile servis edin
```
