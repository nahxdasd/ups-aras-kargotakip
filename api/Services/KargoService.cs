using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Text.Json;
using System.IO;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json.Serialization;
using System.Threading;
using KargoTakip.Models;

namespace KargoTakip.Services
{
    public class KargoService
    {
        private readonly HttpClient _httpClient;
        private readonly string _dataFilePath;
        private List<KargoData> _kargoList;
        private readonly ILogger<KargoService> _logger;
        private readonly string _fourMeEmail;
        private readonly string _fourMePassword;

        private string FormatTrackingNumber(string trackingNumber)
        {
            if (string.IsNullOrEmpty(trackingNumber)) return trackingNumber;
            
            // Eğer Z ile başlıyor ve başında 1 yoksa, başına 1 ekle
            if (trackingNumber.StartsWith("Z", StringComparison.OrdinalIgnoreCase) && 
                !trackingNumber.StartsWith("1", StringComparison.OrdinalIgnoreCase))
            {
                return "1" + trackingNumber;
            }
            return trackingNumber;
        }
        
        // 2FA Session yönetimi
        private readonly Dictionary<string, AuthSession> _authSessions;
        private readonly Dictionary<string, IWebDriver> _activeDrivers; // Browser session'ları sakla
        private readonly object _sessionLock = new object();
        
        // Status güncelleme metodu
        private void UpdateSessionStatus(string sessionId, string status)
        {
            lock (_sessionLock)
            {
                if (_authSessions.TryGetValue(sessionId, out var session))
                {
                    session.CurrentStatus = status;
                    session.LastUpdated = DateTime.Now;
                    _logger.LogInformation($"Session {sessionId} status güncellendi: {status}");
                }
            }
        }
        
        public StatusResponse GetSessionStatus(string sessionId)
        {
            lock (_sessionLock)
            {
                if (_authSessions.TryGetValue(sessionId, out var session))
                {
                    return new StatusResponse
                    {
                        Status = session.CurrentStatus,
                        LastUpdated = session.LastUpdated,
                        IsComplete = session.IsAuthenticated || session.CurrentStatus.Contains("Hata")
                    };
                }
                else
                {
                    return new StatusResponse
                    {
                        Status = "Session bulunamadı",
                        LastUpdated = DateTime.Now,
                        IsComplete = true
                    };
                }
            }
        }

        public KargoService(ILogger<KargoService> logger, IConfiguration configuration)
        {
            _httpClient = new HttpClient();
            _logger = logger;
            _kargoList = new List<KargoData>();
            _dataFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "kargo_data.json");
            Directory.CreateDirectory(Path.GetDirectoryName(_dataFilePath));
            LoadKargoData();
            _fourMeEmail = configuration["FourMe:Email"] ?? "";
            _fourMePassword = configuration["FourMe:Password"] ?? "";
            _authSessions = new Dictionary<string, AuthSession>();
            _activeDrivers = new Dictionary<string, IWebDriver>();
        }

        public string FourMeEmail => _fourMeEmail;
        public string FourMePassword => _fourMePassword;

        private void LoadKargoData()
        {
            try
            {
                if (File.Exists(_dataFilePath))
                {
                    var json = File.ReadAllText(_dataFilePath);
                    _kargoList = JsonSerializer.Deserialize<List<KargoData>>(json) ?? new List<KargoData>();
                    _logger.LogInformation($"Kargo verileri yüklendi. Toplam {_kargoList.Count} kargo bulundu.");
                }
                else
                {
                    _logger.LogInformation("Kargo veri dosyası bulunamadı. Yeni dosya oluşturulacak.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Kargo verileri yüklenirken hata oluştu");
                _kargoList = new List<KargoData>();
            }
        }

        private void SaveKargoData()
        {
            try
            {
                var json = JsonSerializer.Serialize(_kargoList, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_dataFilePath, json);
                _logger.LogInformation($"Kargo verileri kaydedildi. Toplam {_kargoList.Count} kargo kaydedildi.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Kargo verileri kaydedilirken hata oluştu");
            }
        }

        public async Task<List<KargoData>> GetAllKargos()
        {
            return await Task.FromResult(_kargoList);
        }

        public async Task<KargoData?> GetKargoByTrackingNumber(string? trackingNumber)
        {
            if (string.IsNullOrEmpty(trackingNumber))
                return null;
                
            return await Task.FromResult(_kargoList.FirstOrDefault(k => k.TrackingNumber == trackingNumber));
        }

        public async Task AddKargo(KargoData kargo)
        {
            if (kargo == null || string.IsNullOrEmpty(kargo.TrackingNumber))
                return;

            // Kargo numarasını format'la
            kargo.TrackingNumber = FormatTrackingNumber(kargo.TrackingNumber);

            if (!_kargoList.Any(k => k.TrackingNumber == kargo.TrackingNumber))
            {
                _kargoList.Add(kargo);
                SaveKargoData();
            }
        }

        public async Task UpdateKargoStatus(string? trackingNumber, string status)
        {
            if (string.IsNullOrEmpty(trackingNumber))
                return;

            var kargo = _kargoList.FirstOrDefault(k => k.TrackingNumber == trackingNumber);
            if (kargo != null)
            {
                kargo.Status = status;
                kargo.LastUpdated = DateTime.Now;
                SaveKargoData();
            }
        }

        public async Task CheckKargoStatuses()
        {
            _logger.LogInformation("Kargo durumları kontrol ediliyor (track123.com)...");
            var kargolar = await GetAllKargos();
            _logger.LogInformation($"Toplam {kargolar.Count} kargo kontrol edilecek.");
            
            IWebDriver? driver = null;
            
            try
            {
                // Tek bir browser aç
                var options = new ChromeOptions();
                options.AddArgument("--headless");
                options.AddArgument("--no-sandbox");
                options.AddArgument("--disable-dev-shm-usage");
                options.AddArgument("--disable-gpu");
                options.AddArgument("--window-size=1920,1080");
                options.AddArgument("--disable-extensions");
                options.AddArgument("--disable-infobars");
                options.AddArgument("--remote-debugging-port=0");
                options.AddArgument("--disable-blink-features=AutomationControlled");
                options.AddArgument("--disable-notifications");
                options.AddArgument("--disable-popup-blocking");
                options.AddArgument("--user-agent=Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/109.0.0.0 Safari/537.36");
                
                var service = ChromeDriverService.CreateDefaultService();
                service.HideCommandPromptWindow = true;
                
                driver = new ChromeDriver(service, options);
                _logger.LogInformation("Browser açıldı, kargolar tek tek kontrol edilecek.");
                
                // Her kargoyu sırayla kontrol et
            foreach (var kargo in kargolar)
            {
                if (string.IsNullOrEmpty(kargo.TrackingNumber))
                    continue;
                
                    try
                    {
                        _logger.LogInformation($"Kargo durumu kontrol ediliyor: {kargo.TrackingNumber}");
                        
                        bool isDelivered = false;
                        string estimatedDelivery = "-";
                        
                        // Takip numarasına göre kargo firması belirleniyor
                        bool isUPS = kargo.TrackingNumber.StartsWith("1Z", StringComparison.OrdinalIgnoreCase);
                        bool isAras = !isUPS && System.Text.RegularExpressions.Regex.IsMatch(kargo.TrackingNumber, @"^\d+$");
                        
                        if (isAras)
                        {
                            // ARAS KARGO kontrolü
                            try
                            {
                                _logger.LogInformation($"Kargo {kargo.TrackingNumber} için Aras Kargo kontrol ediliyor...");
                                var urlAras = $"https://kargotakip.araskargo.com.tr/mainpage.aspx?code={kargo.TrackingNumber}";
                                _logger.LogInformation($"Aras Kargo sayfası açılıyor: {urlAras}");
                                driver.Navigate().GoToUrl(urlAras);
                                
                                // Sayfanın açılması için 6 saniye bekle
                                await Task.Delay(6000);
                                
                                // Aras Kargo için "TESLİM EDİLDİ" araması
                                isDelivered = await CheckDeliveredStatusAras(driver, kargo.TrackingNumber);
                                
                                if (isDelivered)
                                {
                                    _logger.LogInformation($"✓✓✓ Aras Kargo'da TESLİM EDİLDİ bulundu: {kargo.TrackingNumber}");
                                }
                                else
                                {
                                    _logger.LogInformation($"Aras Kargo'da 'TESLİM EDİLDİ' bulunamadı: {kargo.TrackingNumber}");
                                }
                            }
                            catch (Exception exAras)
                            {
                                _logger.LogWarning($"Aras Kargo kontrolü sırasında hata: {exAras.Message}");
                            }
                        }
                        else if (isUPS)
                        {
                            // K2Track kontrolü (UPS için)
                            try
                            {
                                _logger.LogInformation($"Kargo {kargo.TrackingNumber} için K2Track kontrol ediliyor...");
                                var urlK2Track = $"https://up.k2track.in/ups/tracking-res#{kargo.TrackingNumber}";
                                _logger.LogInformation($"K2Track UPS sayfası açılıyor: {urlK2Track}");
                                driver.Navigate().GoToUrl(urlK2Track);
                                
                                // Sayfanın yüklenmesi için bekleme süresi
                                await Task.Delay(7000); // 7 saniye bekleme
                                
                                var k2Wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));
                                
                                try 
                                {
                                    // K2Track için tam CSS selector'ı kullan
                                    var selector = "div.font-bold.line-clamp-2.text-xl.flex-grow.service-branded-text.ml-2.sm\\:ml-4";
                                    var statusElement = k2Wait.Until(d => d.FindElement(By.CssSelector(selector)));
                                    
                                    if (statusElement != null)
                                    {
                                        var statusText = statusElement.Text.Trim().ToUpperInvariant();
                                        _logger.LogInformation($"[K2Track] Teslimat durumu: {statusText}");
                                        
                                        if (statusText == "DELIVERED")
                                        {
                                            isDelivered = true;
                                            _logger.LogInformation($"[K2Track] ✅ Kargo teslim edildi: {kargo.TrackingNumber}");
                                            await UpdateKargoStatus(kargo.TrackingNumber, "Teslim Edildi");
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogWarning($"[K2Track] Durum elementi bulunamadı: {ex.Message}");
                                }
                                
                                // Eski kontrol sistemini kaldırdık
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning($"[K2Track] Sayfa yüklenme beklemesi sırasında hata: {ex.Message}");
                                // Hata durumunda ek bekleme
                                await Task.Delay(6000);
                            }
                        }
                        
                        // Öngörülen teslimat zamanı - Eğer bulunamadıysa "-" kullan
                        estimatedDelivery = "-";
                        
                        // Durumu güncelle
                        if (isDelivered)
                        {
                            kargo.Status = "Teslim Edildi";
                            _logger.LogInformation($"Kargo {kargo.TrackingNumber} durumu: Teslim Edildi");
                        }
                        else
                        {
                            kargo.Status = "Yolda";
                            _logger.LogInformation($"Kargo {kargo.TrackingNumber} durumu: Yolda");
                        }
                        
                        kargo.EstimatedDelivery = estimatedDelivery;
                        kargo.LastUpdated = DateTime.Now;
                        SaveKargoData();
                        
                        _logger.LogInformation($"Kargo durumu güncellendi: {kargo.TrackingNumber} - {kargo.Status}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Kargo durumu kontrol edilirken hata oluştu: {kargo.TrackingNumber}");
                        // Hata durumunda durumu güncelleme (mevcut durumu koru)
                    }
                }
            }
            finally
            {
                // Browser'ı kapat
                try
                {
                    if (driver != null)
                    {
                        driver.Quit();
                        driver.Dispose();
                        _logger.LogInformation("Browser kapatıldı.");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Browser kapatılırken hata: {ex.Message}");
                }
            }
            
            _logger.LogInformation("Kargo durumları kontrolü tamamlandı.");
        }



        // Aras Kargo için "TESLİM EDİLDİ" kontrol metodu
        private async Task<bool> CheckDeliveredStatusK2Track(IWebDriver driver, string trackingNumber)
        {
            try
            {
                var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10)); // Timeout'u düşürdük
                
                // K2Track UPS takip sayfasını aç
                var url = $"https://up.k2track.in/ups/tracking-res#{trackingNumber}";
                _logger.LogInformation($"[K2Track] Kargo {trackingNumber} için UPS takip sayfası açılıyor: {url}");
                
                // Sayfanın yüklenmesini bekle - performans metriklerine göre ayarlandı
                await Task.Delay(1750); // 1.75 saniye bekleme (performans ölçümlerindeki toplam engelleme süresine göre)
                
                // İlk olarak tam olarak belirtilen class kombinasyonunu kontrol et
                try 
                {
                    // Exact CSS selector'ı kullan
                    var selector = "div.font-bold.line-clamp-2.text-xl.flex-grow.service-branded-text.ml-2.sm\\:ml-4";
                    var statusElements = wait.Until(d => d.FindElements(By.CssSelector(selector)));
                    
                    if (statusElements.Count > 0)
                    {
                        foreach (var element in statusElements)
                        {
                            var statusText = element.Text.Trim().ToUpperInvariant();
                            _logger.LogInformation($"[K2Track] Bulunan durum: {statusText}");
                            
                            if (statusText == "DELIVERED") // Tam eşleşme kontrol et
                            {
                                _logger.LogInformation($"[K2Track] ✅ Kargo {trackingNumber} teslim edilmiş!");
                                return true;
                            }
                        }
                    }
                    
                    // Eğer ilk selector bulunamazsa, daha genel bir arama yap
                    var altElements = driver.FindElements(By.CssSelector(".service-branded-text"));
                    foreach (var element in altElements)
                    {
                        var statusText = element.Text.Trim().ToUpperInvariant();
                        if (statusText == "DELIVERED")
                        {
                            _logger.LogInformation($"[K2Track] ✅ Kargo {trackingNumber} teslim edilmiş! (alternatif kontrol)");
                            return true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[K2Track] Status elementi bulunamadı: {ex.Message}");
                    
                    // Son bir deneme - JavaScript ile kontrol
                    try 
                    {
                        var js = (IJavaScriptExecutor)driver;
                        var result = js.ExecuteScript(@"
                            return Array.from(document.querySelectorAll('.service-branded-text'))
                                .some(el => el.textContent.trim().toUpperCase() === 'DELIVERED');
                        ");
                        
                        if (result != null && (bool)result)
                        {
                            _logger.LogInformation($"[K2Track] ✅ Kargo {trackingNumber} teslim edilmiş! (JS kontrol)");
                            return true;
                        }
                    }
                    catch (Exception jsEx)
                    {
                        _logger.LogWarning($"[K2Track] JavaScript kontrolü başarısız: {jsEx.Message}");
                    }
                }
                
                _logger.LogInformation($"[K2Track] ❌ Kargo {trackingNumber} henüz teslim edilmemiş.");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[K2Track] Genel hata: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> CheckDeliveredStatusAras(IWebDriver driver, string trackingNumber)
        {
            try
            {
                var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(15));
                
                // span#Son_Durum içinde "TESLİM EDİLDİ" text'i ara
                _logger.LogInformation($"[Aras] Kargo {trackingNumber} için span#Son_Durum içinde 'TESLİM EDİLDİ' aranıyor...");
                
                // Sayfanın yüklenmesini bekle
                await Task.Delay(3000);
                
                // Son_Durum elementini bekle
                try
                {
                    wait.Until(d => d.FindElements(By.Id("Son_Durum")).Count > 0);
                }
                catch
                {
                    _logger.LogWarning($"[Aras] Son_Durum elementi bulunamadı, beklemeye devam ediliyor...");
                    await Task.Delay(2000);
                }
                
                // span#Son_Durum elementini kontrol et
                var sonDurumElements = driver.FindElements(By.Id("Son_Durum"));
                _logger.LogInformation($"[Aras] {sonDurumElements.Count} adet Son_Durum elementi bulundu");
                
                foreach (var sonDurum in sonDurumElements)
                {
                    try
                    {
                        var sonDurumContent = sonDurum.Text.Trim();
                        _logger.LogInformation($"[Aras] Son_Durum text: '{sonDurumContent}'");
                        
                        // Metin içinde "TESLİM EDİLDİ" var mı kontrol et
                        if (sonDurumContent.Contains("TESLİM EDİLDİ", StringComparison.OrdinalIgnoreCase))
                        {
                            _logger.LogInformation($"[Aras] ✓✓✓ Son_Durum içinde 'TESLİM EDİLDİ' bulundu: '{sonDurumContent}'");
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"[Aras] Son_Durum okunurken hata: {ex.Message}");
                    }
                }
                
                _logger.LogInformation($"[Aras] Son_Durum içinde 'TESLİM EDİLDİ' bulunamadı");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[Aras] Genel hata: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> CheckDeliveredStatusAfterShip(IWebDriver driver, string trackingNumber)
        {
            try
            {
                var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(15));
                
                // 1. div.text-xl içinde "Delivered" text'i ara (EN ÖNCELİKLİ)
                _logger.LogInformation($"[AfterShip] Kargo {trackingNumber} için div.text-xl içinde 'Delivered' aranıyor...");
                try
                {
                    // Önce div.text-xl elementlerini bekle
                    wait.Until(d => d.FindElements(By.CssSelector("div.text-xl")).Count > 0);
                    
                    var textXlElements = driver.FindElements(By.CssSelector("div.text-xl"));
                    _logger.LogInformation($"[AfterShip] {textXlElements.Count} adet div.text-xl elementi bulundu");
                    
                    foreach (var textXl in textXlElements)
                    {
                        try
                        {
                            var textXlContent = textXl.Text.Trim();
                            _logger.LogInformation($"[AfterShip] div.text-xl text: '{textXlContent.Substring(0, Math.Min(100, textXlContent.Length))}'");
                            
                            // Metin içinde "Delivered" var mı kontrol et
                            if (textXlContent.Contains("Delivered", StringComparison.OrdinalIgnoreCase))
                            {
                                _logger.LogInformation($"[AfterShip] ✓✓✓ div.text-xl içinde 'Delivered' bulundu: '{textXlContent}'");
                                return true;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"[AfterShip] div.text-xl okunurken hata: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[AfterShip] div.text-xl araması sırasında hata: {ex.Message}");
                }
                
                // 2. Alternatif: Genel sayfa içeriğinde "Delivered" kontrolü
                _logger.LogInformation($"[AfterShip] Kargo {trackingNumber} için sayfa içeriğinde genel 'Delivered' kontrolü...");
                try
                {
                    var bodyText = driver.FindElement(By.TagName("body")).Text;
                    if (bodyText.Contains("Delivered", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogInformation($"[AfterShip] ✓✓✓ Sayfa içeriğinde 'Delivered' kelimesi bulundu");
                        
                        // Delivered kelimesinin geçtiği satırları bul
                        var lines = bodyText.Split('\n');
                        var deliveredLines = lines.Where(line => line.Contains("Delivered", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(line)).Take(5).ToList();
                        
                        if (deliveredLines.Any())
                        {
                            _logger.LogInformation($"[AfterShip] {deliveredLines.Count} satırda 'Delivered' geçiyor:");
                            foreach (var line in deliveredLines)
                            {
                                _logger.LogInformation($"[AfterShip]   - {line.Substring(0, Math.Min(100, line.Length))}");
                            }
                            return true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[AfterShip] Sayfa içeriği kontrolü sırasında hata: {ex.Message}");
                }
                
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[AfterShip] Genel hata: {ex.Message}");
                return false;
            }
        }

        // 17track.net için "Delivered" kontrol metodu (Python script mantığı)
        private async Task<bool> CheckDeliveredStatus17Track(IWebDriver driver, string trackingNumber)
        {
            try
            {
                var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(20));
                
                // 1. title="Delivered" içeren elementleri ara
                _logger.LogInformation($"[17track] Kargo {trackingNumber} için title='Delivered' elementleri aranıyor...");
                try
                {
                    var elementsWithTitle = driver.FindElements(By.XPath("//*[@title='Delivered']"));
                    _logger.LogInformation($"[17track] {elementsWithTitle.Count} adet title='Delivered' elementi bulundu");
                    
                    foreach (var elem in elementsWithTitle)
                    {
                        try
                        {
                            if (elem.Displayed)
                            {
                                _logger.LogInformation($"[17track] ✓✓✓ title='Delivered' elementi bulundu ve görünür: {trackingNumber}");
                                return true;
                            }
                        }
                        catch { }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[17track] title='Delivered' araması sırasında hata: {ex.Message}");
                }
                
                // 2. h3 içinde "Delivered" text'i ara
                _logger.LogInformation($"[17track] Kargo {trackingNumber} için h3 içinde 'Delivered' aranıyor...");
                try
                {
                    var h3Elements = driver.FindElements(By.XPath("//h3[contains(text(), 'Delivered')]"));
                    _logger.LogInformation($"[17track] {h3Elements.Count} adet h3 elementi bulundu");
                    
                    foreach (var h3 in h3Elements)
                    {
                        try
                        {
                            if (h3.Displayed && h3.Text.Contains("Delivered", StringComparison.OrdinalIgnoreCase))
                            {
                                _logger.LogInformation($"[17track] ✓✓✓ h3 içinde 'Delivered' bulundu: '{h3.Text}'");
                                return true;
                            }
                        }
                        catch { }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[17track] h3 araması sırasında hata: {ex.Message}");
                }
                
                // 3. div içinde "Delivered" text'i ara
                _logger.LogInformation($"[17track] Kargo {trackingNumber} için div içinde 'Delivered' aranıyor...");
                try
                {
                    var divElements = driver.FindElements(By.XPath("//div[contains(text(), 'Delivered')]"));
                    _logger.LogInformation($"[17track] {divElements.Count} adet div elementi bulundu (Delivered içeren)");
                    
                    foreach (var div in divElements.Take(10)) // İlk 10'unu kontrol et
                    {
                        try
                        {
                            var divText = div.Text.Trim();
                            if (divText.Contains("Delivered", StringComparison.OrdinalIgnoreCase) && div.Displayed)
                            {
                                _logger.LogInformation($"[17track] ✓✓✓ div içinde 'Delivered' bulundu: '{divText.Substring(0, Math.Min(80, divText.Length))}'...");
                                return true;
                            }
                        }
                        catch { }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[17track] div araması sırasında hata: {ex.Message}");
                }
                
                // 4. span içinde "Delivered" text'i ara
                _logger.LogInformation($"[17track] Kargo {trackingNumber} için span içinde 'Delivered' aranıyor...");
                try
                {
                    var spanElements = driver.FindElements(By.XPath("//span[contains(text(), 'Delivered')]"));
                    _logger.LogInformation($"[17track] {spanElements.Count} adet span elementi bulundu (Delivered içeren)");
                    
                    foreach (var span in spanElements.Take(10)) // İlk 10'unu kontrol et
                    {
                        try
                        {
                            var spanText = span.Text.Trim();
                            if (spanText.Contains("Delivered", StringComparison.OrdinalIgnoreCase) && span.Displayed)
                            {
                                _logger.LogInformation($"[17track] ✓✓✓ span içinde 'Delivered' bulundu: '{spanText.Substring(0, Math.Min(80, spanText.Length))}'...");
                                return true;
                            }
                        }
                        catch { }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[17track] span araması sırasında hata: {ex.Message}");
                }
                
                // 5. Genel sayfa içeriğinde "Delivered" kontrolü
                _logger.LogInformation($"[17track] Kargo {trackingNumber} için sayfa içeriğinde genel 'Delivered' kontrolü...");
                try
                {
                    var bodyText = driver.FindElement(By.TagName("body")).Text;
                    if (bodyText.Contains("Delivered", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogInformation($"[17track] ✓✓✓ Sayfa içeriğinde 'Delivered' kelimesi bulundu");
                        
                        // Delivered kelimesinin geçtiği satırları bul
                        var lines = bodyText.Split('\n');
                        var deliveredLines = lines.Where(line => line.Contains("Delivered", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(line)).Take(5).ToList();
                        
                        if (deliveredLines.Any())
                        {
                            _logger.LogInformation($"[17track] {deliveredLines.Count} satırda 'Delivered' geçiyor:");
                            foreach (var line in deliveredLines)
                            {
                                _logger.LogInformation($"[17track]   - {line.Substring(0, Math.Min(100, line.Length))}");
                            }
                            return true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[17track] Sayfa içeriği kontrolü sırasında hata: {ex.Message}");
                }
                
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[17track] Genel hata: {ex.Message}");
                return false;
            }
        }

        // Track123.com için "Delivered" kontrol metodu
        private async Task<bool> CheckDeliveredStatusTrack123(IWebDriver driver, string trackingNumber)
        {
            try
            {
                var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(15));
                bool isDelivered = false;
                
                // ÖNCELİK: div.track-list-title içindeki metni kontrol et (tracking result alanı)
                try
                {
                    wait.Until(d => d.FindElements(By.CssSelector("div.track-list-title, div.result, span.status")).Count > 0);
                }
                catch
                {
                    _logger.LogWarning($"[track123] Kargo {trackingNumber} için tracking elementleri bulunamadı, beklemeye devam ediliyor...");
                    await Task.Delay(2000);
                }
                
                // 1. div.track-list-title elementlerini kontrol et (EN ÖNCELİKLİ)
                var trackListTitles = driver.FindElements(By.CssSelector("div.track-list-title"));
                _logger.LogInformation($"[track123] Kargo {trackingNumber} için {trackListTitles.Count} adet track-list-title bulundu.");
                
                foreach (var titleElement in trackListTitles)
                {
                    try
                    {
                        var titleText = titleElement.Text.Trim();
                        _logger.LogInformation($"[track123] Kargo {trackingNumber} için track-list-title text: '{titleText}'");
                        
                        // Metin içinde "Delivered" var mı kontrol et
                        if (titleText.Contains("Delivered", StringComparison.OrdinalIgnoreCase) ||
                            titleText.Equals("DELIVERED", StringComparison.OrdinalIgnoreCase))
                        {
                            isDelivered = true;
                            _logger.LogInformation($"[track123] ✓✓✓ Kargo {trackingNumber} için track-list-title içinde 'Delivered' metni bulundu.");
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"[track123] Kargo {trackingNumber} için track-list-title okunurken hata: {ex.Message}");
                    }
                }
                
                // 2. Eğer track-list-title'da bulunamadıysa, div.result içindeki metni kontrol et
                if (!isDelivered)
                {
                    var resultDivs = driver.FindElements(By.CssSelector("div.result"));
                    _logger.LogInformation($"[track123] Kargo {trackingNumber} için {resultDivs.Count} adet result div bulundu.");
                    
                    foreach (var resultDiv in resultDivs)
                    {
                        try
                        {
                            var resultText = resultDiv.Text;
                            _logger.LogInformation($"[track123] Kargo {trackingNumber} için result text (ilk 1000 karakter): '{resultText.Substring(0, Math.Min(1000, resultText.Length))}'");
                            
                            // Metin içinde "Delivered" var mı kontrol et
                            if (resultText.Contains("Delivered", StringComparison.OrdinalIgnoreCase) ||
                                resultText.Contains("DELIVERED", StringComparison.OrdinalIgnoreCase))
                            {
                                isDelivered = true;
                                _logger.LogInformation($"[track123] ✓✓✓ Kargo {trackingNumber} için result div içinde 'Delivered' metni bulundu.");
                                return true;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"[track123] Kargo {trackingNumber} için result div okunurken hata: {ex.Message}");
                        }
                    }
                }
                
                // 3. Eğer hala bulunamadıysa, span.status elementini kontrol et
                if (!isDelivered)
                {
                    var statusElements = driver.FindElements(By.CssSelector("span.status"));
                    _logger.LogInformation($"[track123] Kargo {trackingNumber} için {statusElements.Count} adet span.status bulundu.");
                    
                    foreach (var statusElement in statusElements)
                    {
                        try
                        {
                            var statusText = statusElement.Text.Trim();
                            _logger.LogInformation($"[track123] Kargo {trackingNumber} için span.status text: '{statusText}'");
                            if (statusText.Equals("Delivered", StringComparison.OrdinalIgnoreCase))
                            {
                                isDelivered = true;
                                _logger.LogInformation($"[track123] ✓✓✓ Kargo {trackingNumber} için span.status'te 'Delivered' bulundu.");
                                return true;
                            }
                        }
                        catch { }
                    }
                }
                
                return isDelivered;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[track123] Kargo {trackingNumber} için durum kontrolü sırasında hata: {ex.Message}");
                return false;
            }
        }

        // 2FA Authentication metodları
        public async Task<LoginResponse> InitiateLogin(string email, string password)
        {
            var sessionId = Guid.NewGuid().ToString();
            var session = new AuthSession
            {
                SessionId = sessionId,
                Email = email,
                Password = password,
                CreatedAt = DateTime.Now
            };

            lock (_sessionLock)
            {
                _authSessions[sessionId] = session;
            }

            try
            {
                var result = await PerformLoginAndCheckFor2FA(email, password, sessionId);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Login başlatılırken hata oluştu");
                lock (_sessionLock)
                {
                    _authSessions.Remove(sessionId);
                }
                return new LoginResponse
                {
                    Success = false,
                    Message = $"Giriş hatası: {ex.Message}"
                };
            }
        }

        public async Task<TwoFactorResponse> VerifyTwoFactor(string sessionId, string userCode)
        {
            AuthSession? session;
            IWebDriver? driver;
            
            lock (_sessionLock)
            {
                if (!_authSessions.TryGetValue(sessionId, out session) || 
                    !_activeDrivers.TryGetValue(sessionId, out driver))
                {
                    return new TwoFactorResponse
                    {
                        Success = false,
                        Message = "Geçersiz session ID veya browser kapanmış"
                    };
                }
            }

            // Kullanıcının girdiği kod ile sistemden alınan kodu karşılaştır
            if (session.TwoFactorCode == userCode)
            {
                try
                {
                    _logger.LogInformation("2FA kodu doğrulandı, AYNI BROWSER'da devam ediliyor...");
                    session.IsAuthenticated = true;
                    
                    // AYNI BROWSER'da devam et - KAPATMA!
                    var kargolar = await ContinueWithSameBrowser(driver, sessionId);
                    
                    // Session ve driver temizle
                    lock (_sessionLock)
                    {
                        _authSessions.Remove(sessionId);
                        _activeDrivers.Remove(sessionId);
                    }
                    
                    // Şimdi browser'ı kapat
                    try { driver.Quit(); } catch { }

                    return new TwoFactorResponse
                    {
                        Success = true,
                        Message = "2FA doğrulandı, veriler başarıyla yüklendi.",
                        Data = kargolar
                    };
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Veri yükleme sırasında hata oluştu");
                    
                    // Hata durumunda da browser'ı kapat
                    lock (_sessionLock)
                    {
                        _authSessions.Remove(sessionId);
                        _activeDrivers.Remove(sessionId);
                    }
                    try { driver.Quit(); } catch { }
                    
                    return new TwoFactorResponse
                    {
                        Success = false,
                        Message = $"Veri yükleme hatası: {ex.Message}"
                    };
                }
            }
            else
            {
                return new TwoFactorResponse
                {
                    Success = false,
                    Message = "Geçersiz 2FA kodu"
                };
            }
        }

        private async Task<LoginResponse> PerformLoginAndCheckFor2FA(string email, string password, string sessionId)
        {
            var options = new ChromeOptions();
            options.AddArgument("--headless");
            options.AddArgument("--no-sandbox");
            options.AddArgument("--disable-dev-shm-usage");
            options.AddArgument("--disable-gpu");
            options.AddArgument("--window-size=1920,1080");
            options.AddArgument("--disable-extensions");
            options.AddArgument("--disable-infobars");
            options.AddArgument("--remote-debugging-port=9222");
            options.AddArgument("--disable-blink-features=AutomationControlled");
            options.AddArgument("--disable-notifications");
            options.AddArgument("--disable-popup-blocking");
            options.AddArgument("--start-maximized");
            options.AddArgument("--disable-web-security");
            options.AddArgument("--allow-running-insecure-content");
            options.AddArgument("--ignore-certificate-errors");
            options.AddArgument("--user-agent=Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/109.0.0.0 Safari/537.36");

            var service = ChromeDriverService.CreateDefaultService();
            service.HideCommandPromptWindow = true;

            var driver = new ChromeDriver(service, options); // using KULLANMA - browser açık kalsın!
            bool shouldKeepBrowserOpen = false;
            
            try
            {
                UpdateSessionStatus(sessionId, "🚀 Giriş işlemi başlatılıyor...");
                _logger.LogInformation("Tarayıcı başlatıldı...");
                await Task.Delay(5000); // İlk 5 saniye bekle
                
                // DİREKT inbox sayfasını aç
                UpdateSessionStatus(sessionId, "🌐 4me sayfası açılıyor...");
                var url = "https://gratis-it.4me.com/inbox?q=servicedesk#table=true";
                _logger.LogInformation($"Direkt inbox sayfası açılıyor: {url}");
                driver.Navigate().GoToUrl(url);
                    
                    // Sayfanın yüklenmesini bekle - 5 saniye garanti
                    await Task.Delay(5000);
                    _logger.LogInformation("Sayfa yüklendi, giriş formunu arıyor...");
                    
                    var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(30));
                    
                    // E-posta alanını bul ve doldur
                    _logger.LogInformation("E-posta alanı aranıyor...");
                    var emailInput = wait.Until(d => d.FindElement(By.Id("i0116")));
                    _logger.LogInformation("E-posta alanı bulundu");
                    
                    UpdateSessionStatus(sessionId, "📧 E-posta adresi giriliyor...");
                    emailInput.Clear();
                    emailInput.SendKeys(email);
                    _logger.LogInformation($"E-posta girildi: {email}");
                    
                    // E-posta girişi sonrası 5 saniye bekle - garanti
                    UpdateSessionStatus(sessionId, "✅ E-posta girildi, devam ediliyor...");
                    await Task.Delay(5000);
                    
                    // Enter tuşuna bas
                    emailInput.SendKeys(Keys.Return);
                    UpdateSessionStatus(sessionId, "⏳ Şifre alanı bekleniyor...");
                    _logger.LogInformation("E-posta gönderildi, şifre alanı bekleniyor...");
                    
                    // Şifre alanının görünmesini bekle - 5 saniye garanti
                    await Task.Delay(5000);
                    
                    // Şifre alanını bul ve doldur
                    _logger.LogInformation("Şifre alanı aranıyor...");
                    var passwordInput = wait.Until(d => d.FindElement(By.Id("i0118")));
                    _logger.LogInformation("Şifre alanı bulundu");
                    
                    UpdateSessionStatus(sessionId, "🔐 Şifre giriliyor...");
                    passwordInput.Clear();
                    passwordInput.SendKeys(password);
                    _logger.LogInformation("Şifre girildi");
                    
                    // Şifre girişi sonrası 5 saniye bekle - garanti
                    UpdateSessionStatus(sessionId, "✅ Şifre girildi, giriş yapılıyor...");
                    await Task.Delay(5000);
                    
                    // Enter tuşuna bas
                    passwordInput.SendKeys(Keys.Return);
                    UpdateSessionStatus(sessionId, "🔍 2FA kodu kontrol ediliyor...");
                    _logger.LogInformation("Şifre gönderildi, 2FA kontrolü için 5 saniye bekleniyor...");
                    
                    // Şifre sonrası 5 saniye bekle - 2FA için
                    await Task.Delay(5000);
                    
                    // ÖNCE 2FA kod alanını kontrol et (Python'daki gibi)
                    string? twoFactorCode = null;
                    bool twoFactorFound = false;
                    try
                    {
                        _logger.LogInformation("2FA kod alanı aranıyor...");
                        
                        // Önce sayfanın HTML'ini log'a yazdır (debug için)
                        try 
                        {
                            var pageSource = driver.PageSource;
                            if (pageSource.Contains("displaySign") || pageSource.Contains("DisplaySign"))
                            {
                                _logger.LogInformation("Sayfada 2FA elementi bulundu!");
                            }
                            else
                            {
                                _logger.LogWarning("Sayfada 2FA elementi bulunamadı!");
                            }
                        }
                        catch { }
                        
                        var twoFactorCodeElement = wait.Until(d => d.FindElement(By.Id("idRichContext_DisplaySign")));
                        twoFactorCode = twoFactorCodeElement.Text.Trim();
                        
                        if (!string.IsNullOrEmpty(twoFactorCode) && twoFactorCode.All(char.IsDigit))
                        {
                            UpdateSessionStatus(sessionId, $"🔢 2FA kodu bulundu: {twoFactorCode}");
                            _logger.LogInformation($"🔐 2FA Kodu Bulundu: {twoFactorCode}");
                            
                            twoFactorFound = true; // FLAG SET ET!
                            shouldKeepBrowserOpen = true; // BROWSER'I KAPATMA!
                            
                            // Session'a driver'ı kaydet (2FA onayından sonra kullanmak için)
                            var session = _authSessions[sessionId];
                            session.TwoFactorCode = twoFactorCode;
                            
                            // BROWSER'I AÇIK TUT! - Driver'ı session'a kaydet
                            lock (_sessionLock)
                            {
                                _activeDrivers[sessionId] = driver;
                            }
                            
                            return new LoginResponse
                            {
                                Success = true,
                                RequiresTwoFactor = true,
                                SessionId = sessionId,
                                TwoFactorCode = twoFactorCode,
                                Message = "2FA kodu alındı. Lütfen kodu onaylayın."
                            };
                        }
                        else
                        {
                            _logger.LogWarning($"2FA kod elementi bulundu ama geçersiz: '{twoFactorCode}'");
                        }
                    }
                    catch (Exception twoFaError)
                    {
                        _logger.LogWarning($"2FA işlemi sırasında hata (normal olabilir): {twoFaError.Message}");
                        
                        // Alternatif selectorlar dene
                        try
                        {
                            var alternativeSelectors = new[]
                            {
                                "div.displaySign",
                                "div[data-bind*='displaySign']", 
                                "div.display-sign-height",
                                "[id*='DisplaySign']",
                                "[class*='displaySign']",
                                "div[tabindex='0'][aria-labelledby*='DisplaySign']"
                            };

                            foreach (var selector in alternativeSelectors)
                            {
                                try
                                {
                                    var element = driver.FindElement(By.CssSelector(selector));
                                    var code = element.Text.Trim();
                                    if (!string.IsNullOrEmpty(code) && code.All(char.IsDigit))
                                    {
                                        _logger.LogInformation($"🔐 2FA kodu alternatif selector ile bulundu: {code}");
                                        twoFactorCode = code;
                                        
                                        // Session'a kaydet
                                        var session = _authSessions[sessionId];
                                        session.TwoFactorCode = twoFactorCode;
                                        
                                        return new LoginResponse
                                        {
                                            Success = true,
                                            RequiresTwoFactor = true,
                                            SessionId = sessionId,
                                            TwoFactorCode = twoFactorCode,
                                            Message = "2FA kodu alındı. Lütfen kodu onaylayın."
                                        };
                                    }
                                }
                                catch { continue; }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"Alternatif 2FA kod arama hatası: {ex.Message}");
                        }
                    }
                    
                    // 2FA kodu bulundu mu kontrol et
                    if (twoFactorFound)
                    {
                        _logger.LogInformation("2FA kodu zaten bulundu ve return edildi, bu kod çalışmamalı!");
                        return new LoginResponse { Success = false, Message = "Beklenmeyen durum" };
                    }
                    
                    _logger.LogInformation("2FA kodu bulunamadı, normal giriş akışına devam ediliyor");
                    
                    // 2FA yoksa "Evet" butonuna tıklayıp veri çekmeye devam et
                    _logger.LogInformation("2FA yok, 'Evet' butonuna tıklayıp veri çekmeye başlanıyor...");
                    await Task.Delay(5000);
                    
                    // "Evet" butonuna tıkla (oturum açık kalsın)
                    try
                    {
                        var finalButton = driver.FindElement(By.Id("idSIButton9"));
                        finalButton.Click();
                        _logger.LogInformation("'Evet' butonuna tıklandı");
                        await Task.Delay(5000); // 5 saniye bekle
                    }
                    catch
                    {
                        _logger.LogInformation("'Evet' butonu bulunamadı, zaten inbox'ta olabilir");
                        await Task.Delay(5000);
                    }
                    
                    // Artık inbox'ta olmalıyız, veri çek
                    _logger.LogInformation("Inbox'ta veri çekmeye başlanıyor...");
                    var kargolar = await LoadDataFromInboxWithDriver(driver);
                    
                    // Browser'ı kapat
                    try { driver.Quit(); } catch { }
                    
                    return new LoginResponse
                    {
                        Success = true,
                        RequiresTwoFactor = false,
                        SessionId = sessionId,
                        Message = "Giriş başarılı, veriler yüklendi."
                    };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "2FA kodu alınırken hata oluştu");
                // Sadece browser açık tutulmayacaksa kapat
                if (!shouldKeepBrowserOpen)
                {
                    try { driver.Quit(); } catch { }
                }
                throw;
            }
            // Browser'ı KAPATMA! Session'da tutacağız
        }

        private async Task<List<KargoData>> CompleteDataLoad(AuthSession session)
        {
            // Mevcut LoadDataFrom4me metodunu kullan ama session bilgileri ile
            await LoadDataFrom4me(session.Email, session.Password);
            return await GetAllKargos();
        }

        private async Task<List<KargoData>> ContinueWithSameBrowser(IWebDriver driver, string sessionId)
        {
            _logger.LogInformation("2FA onaylandı, AYNI BROWSER'da 'Evet' butonuna tıklayıp devam ediliyor...");
            
            try
            {
                var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(30));
                
                // 2FA onayı sonrası 5 saniye bekle - KULLANICI ONAYLAYANA KADAR BEKLEME!
                UpdateSessionStatus(sessionId, "✅ 2FA onaylandı! Kargo deskleri çekiliyor bekleyiniz...");
                _logger.LogInformation("2FA kodu onaylandı, 5 saniye bekleyip 'Evet' butonuna tıklanacak...");
                await Task.Delay(5000);
                
                // "Oturum açık kalsın mı?" sayfasındaki "Evet" butonunu bul ve tıkla
                UpdateSessionStatus(sessionId, "🔍 'Evet' butonu aranıyor...");
                _logger.LogInformation("'Oturum açık kalsın mı?' sayfasında 'Evet' butonu aranıyor...");
                
                try
                {
                    var yesButton = wait.Until(d => d.FindElement(By.Id("idSIButton9")));
                    UpdateSessionStatus(sessionId, "👆 'Evet' butonuna tıklanıyor...");
                    _logger.LogInformation("'Evet' butonu bulundu, tıklanıyor...");
                    yesButton.Click();
                    
                    // Evet butonuna tıkladıktan sonra 5 saniye bekle
                    UpdateSessionStatus(sessionId, "⏳ Oturum onaylandı, sayfa yükleniyor...");
                    await Task.Delay(5000);
                    _logger.LogInformation("'Evet' butonuna tıklandı, veri çekmeye başlanıyor...");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"'Evet' butonu bulunamadı: {ex.Message}");
                    UpdateSessionStatus(sessionId, "⚠️ 'Evet' butonu bulunamadı, devam ediliyor...");
                    await Task.Delay(5000);
                }
                
                // Artık inbox sayfasında olmalıyız, veri çekmeye başla
                UpdateSessionStatus(sessionId, "📊 Inbox sayfasında veriler çekiliyor...");
                _logger.LogInformation("Inbox sayfasında veri çekmeye başlanıyor...");
                await Task.Delay(5000); // 5 saniye daha bekle sayfa tamamen yüklensin
                
                // Veri çek - AYNI BROWSER'da
                var kargolar = await LoadDataFromInboxWithDriver(driver, sessionId);
                return kargolar;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Aynı browser'da devam ederken hata oluştu");
                throw;
            }
        }



        private async Task<List<KargoData>> LoadDataFromInboxWithDriver(IWebDriver driver, string sessionId = "")
        {
            _logger.LogInformation("Inbox'tan veri çekme başlatılıyor...");
            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(30));
            
            await Task.Delay(5000); // Sayfa yüklensin
            
            try
            {
                if (!string.IsNullOrEmpty(sessionId)) {
                    UpdateSessionStatus(sessionId, "📊 Toplam öğe sayısı kontrol ediliyor...");
                }


                var bulunanKargoSayisi = 0;
                var islenenKargolar = new HashSet<string>();
                var yeniEklenenKargolar = new List<KargoData>();
            
                // Toplam öğe sayısını al
                int totalItems = 0;
                try
                {
                    var totalItemsElement = wait.Until(d => d.FindElement(By.Id("view_counter")));
                    var totalItemsText = totalItemsElement.Text.Trim();
                    if (int.TryParse(totalItemsText.Replace(" öğe", ""), out totalItems))
                    {
                        _logger.LogInformation($"Toplam öğe sayısı bulundu: {totalItems}");
                        if (!string.IsNullOrEmpty(sessionId)) {
                            UpdateSessionStatus(sessionId, $"📊 Toplam {totalItems} öğe bulundu");
                        }
                    }
                    else
                    {
                        _logger.LogWarning($"Toplam öğe sayısı metni sayıya çevrilemedi: {totalItemsText}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Toplam öğe sayısı elementi bulunamadı: {ex.Message}");
                    totalItems = 1000; // Varsayılan değer
                }

                // Scroll yaparak talepleri dinamik olarak yükle ve işle
                var scrollContainer = wait.Until(d => d.FindElement(By.Id("view_list_container")));
                
                _logger.LogInformation("Talepleri scroll yaparak yükleme ve işleme başlatılıyor...");
                
                if (!string.IsNullOrEmpty(sessionId)) {
                    UpdateSessionStatus(sessionId, "🔄 Talepler yükleniyor ve işleniyor...");
                }
                
                int noProgressCount = 0; // İlerleme olmayan scroll denemesi sayısı
                const int maxNoProgress = 5; // Maksimum ilerleme olmayan deneme sayısı

                while (islenenKargolar.Count(k => k.StartsWith("TALEP_")) < totalItems)
                {
                    // Mevcut görünümdeki tüm talepleri al
                    var currentTalepler = driver.FindElements(By.CssSelector("div.grid-row")).ToList();
                    _logger.LogInformation($"Mevcut görünümde {currentTalepler.Count} talep bulundu.");

                    int processedInIteration = 0;

                    foreach (var talep in currentTalepler)
                    {
                        // Talep ID'sini al
                        string talepId = "";
                        try {
                            var talepIdElement = talep.FindElement(By.CssSelector("div.cell-path"));
                            var talepIdText = talepIdElement.Text.Trim();
                            talepId = Regex.Match(talepIdText, @"\d+").Value;
                        }
                        catch {
                            // Eğer ID alınamazsa bu elementi atla ve logla
                            _logger.LogWarning("Bir talep elementi için ID bulunamadı, atlanıyor.");
                            continue; // Bu elementi atla, işlenmiş sayma
                        }
                        
                        // Eğer bu talep daha önce işlenmediyse devam et
                        if (islenenKargolar.Contains("TALEP_" + talepId))
                        {
                            continue; // Zaten işlenmiş, atla
                        }

                        // Talep işlenmemiş, şimdi işle
                        try
                        {
                            // Konu kontrolü
                            string konu = "";
                            try {
                                var konuElement = talep.FindElement(By.CssSelector("div.cell-subject span"));
                                konu = konuElement.GetAttribute("title") ?? konuElement.Text.Trim();
                            }
                            catch {
                                try {
                                    var konuElement = talep.FindElement(By.CssSelector("div.cell-subject"));
                                    konu = konuElement.Text.Trim();
                                }
                                catch {
                                    _logger.LogWarning($"Talep {talepId} için konu bulunamadı, atlanıyor.");
                                    islenenKargolar.Add("TALEP_" + talepId); // İşlendi olarak işaretle
                                    processedInIteration++;
                                    continue;
                                }
                            }
                            
                            // Mağaza bilgisini liste sayfasından al (daha hızlı)
                            string magazaId = "";
                            try {
                                var requesterElement = talep.FindElement(By.CssSelector("div.cell-requester span"));
                                magazaId = requesterElement.GetAttribute("title") ?? requesterElement.Text.Trim();
                                // "- Gratis" kısmını temizle
                                if (magazaId.Contains(" - "))
                                {
                                    magazaId = magazaId.Split(" - ")[0].Trim();
                                }
                                _logger.LogInformation($"Talep {talepId} için mağaza bilgisi liste sayfasından alındı: {magazaId}");
                            }
                            catch (Exception ex) {
                                _logger.LogWarning($"Talep {talepId} için mağaza bilgisi liste sayfasından alınamadı: {ex.Message}");
                            }

                            // Artık tüm talepleri kontrol ediyoruz, konu filtresi kaldırıldı
                            _logger.LogInformation($"Talep {talepId} işleniyor, konu: {konu}, mağaza: {magazaId}");

                            // Talebe tıkla ve detayları kontrol et
                            try {
                                talep.Click();
                                await Task.Delay(750); // Tıklama sonrası biraz daha bekleme süresi düşürüldü
                            }
                            catch (Exception ex) {
                                _logger.LogError($"Talep {talepId} tıklanamadı: {ex.Message}");
                                islenenKargolar.Add("TALEP_" + talepId); // İşlendi olarak işaretle
                                processedInIteration++;
                                continue;
                            }

                            // Talep detay sayfası elementlerini beklemek için WebDriverWait oluştur
                            var talepWait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));

                            // Sayfanın yüklendiğini belirten bir elementi bekleyin
                            try
                            {
                                talepWait.Until(d => d.FindElement(By.ClassName("header_bar_inner")));
                                _logger.LogInformation($"Talep {talepId} detay sayfası yüklendi.");
                            }
                            catch (WebDriverTimeoutException)
                            {
                                _logger.LogWarning($"Talep {talepId} detay sayfası yüklenemedi, atlanıyor.");
                                islenenKargolar.Add("TALEP_" + talepId); // İşlendi olarak işaretle
                                processedInIteration++;
                                try { driver.Navigate().Back(); await Task.Delay(1000); } catch { } // Geri dönerken daha uzun bekle
                                continue;
                            }

                            if (string.IsNullOrEmpty(magazaId))
                            {
                                _logger.LogWarning($"Talep {talepId} için mağaza ID'si boş veya null kaldı.");
                                islenenKargolar.Add("TALEP_" + talepId); // İşlendi olarak işaretle
                                processedInIteration++;
                                driver.Navigate().Back();
                                await Task.Delay(1000); // Geri dönerken daha uzun bekle
                                continue;
                            }

                            // Notlar bölümünden kargo numarasını bul - Tarihe göre en yenisini seç
                            var trackingNumber = "";
                            
                            try
                            {
                                // Her note'u ayrı ayrı ele al ve tarih bilgisiyle birlikte kargo numarasını bul
                                var noteItems = new List<(DateTime date, int domIndex, string trackingNumber)>();
                                
                                // Notes list container'ı bul
                                var notesListElements = driver.FindElements(By.CssSelector("li div.note"));
                                _logger.LogInformation($"Talep {talepId} için {notesListElements.Count} note elementi bulundu.");
                                
                                // NOT: Note'lar DOM'da en eski en üstte (index 0), en yeni en altta (son index)
                                // DOM index'ini de kullanarak en yeni olanı bulalım
                                for (int i = 0; i < notesListElements.Count; i++)
                                {
                                    var noteElement = notesListElements[i];
                                    try
                                    {
                                        // Note tarihini al
                                        DateTime noteDate = DateTime.MinValue;
                                        try
                                        {
                                            var dateElement = noteElement.FindElement(By.CssSelector("span.note_at.datetime"));
                                            var dateTimeAttr = dateElement.GetAttribute("data-datetime");
                                            if (!string.IsNullOrEmpty(dateTimeAttr))
                                            {
                                                if (DateTime.TryParse(dateTimeAttr, out var parsedDate))
                                                {
                                                    noteDate = parsedDate;
                                                    _logger.LogInformation($"Talep {talepId} için note[{i}] tarihi bulundu: {noteDate}");
                                                }
                                            }
                                        }
                                        catch { }
                                        
                                        // Note içeriğini al
                                        string noteText = "";
                                        try
                                        {
                                            var noteContentElement = noteElement.FindElement(By.CssSelector("div.note-content"));
                                            noteText = noteContentElement.Text ?? "";
                                        }
                                        catch
                                        {
                                            noteText = noteElement.Text ?? "";
                                        }
                                        
                                        if (string.IsNullOrEmpty(noteText))
                                            continue;
                                        
                                        // Bu note'dan kargo numarası bul
                                        string foundTrackingNumber = "";
                                        
                                        // "Kargo Takip No" ile başlayan satırı bul
                                        var kargoTakipLineMatch = Regex.Match(noteText, @"Kargo\s*Takip\s*No\s*[:\-]\s*([^\s\r\n]+)", RegexOptions.IgnoreCase);
                                        if (kargoTakipLineMatch.Success)
                                        {
                                            var fullTrackingFromLine = kargoTakipLineMatch.Groups[1].Value.Trim();
                                            var upsFromLine = Regex.Match(fullTrackingFromLine, @"(1[Zz][0-9A-Za-z]+)");
                                            if (upsFromLine.Success)
                                            {
                                                foundTrackingNumber = upsFromLine.Groups[1].Value;
                                            }
                                        }
                                        
                                        // UPS formatları
                                        if (string.IsNullOrEmpty(foundTrackingNumber))
                                        {
                                            var allUpsMatches = Regex.Matches(noteText, @"1[Zz][0-9A-Za-z]{14,20}", RegexOptions.IgnoreCase);
                                            if (allUpsMatches.Count > 0)
                                            {
                                                foundTrackingNumber = allUpsMatches[allUpsMatches.Count - 1].Value;
                                            }
                                        }
                                        
                                        // Aras Kargo formatı
                                        if (string.IsNullOrEmpty(foundTrackingNumber))
                                        {
                                            var arasMatches = Regex.Matches(noteText, @"[A-Z]{2}\d{9}");
                                            if (arasMatches.Count > 0)
                                            {
                                                foundTrackingNumber = arasMatches[arasMatches.Count - 1].Value;
                                            }
                                        }
                                        
                                        // Yurtiçi Kargo formatı
                                        if (string.IsNullOrEmpty(foundTrackingNumber))
                                        {
                                            var yurticiMatches = Regex.Matches(noteText, @"\d{13}");
                                            if (yurticiMatches.Count > 0)
                                            {
                                                foundTrackingNumber = yurticiMatches[yurticiMatches.Count - 1].Value;
                                            }
                                        }
                                        
                                        // MNG Kargo formatı
                                        if (string.IsNullOrEmpty(foundTrackingNumber))
                                        {
                                            var mngMatches = Regex.Matches(noteText, @"MNG\d{10}");
                                            if (mngMatches.Count > 0)
                                            {
                                                foundTrackingNumber = mngMatches[mngMatches.Count - 1].Value;
                                            }
                                        }
                                        
                                        // Eğer kargo numarası bulunduysa listeye ekle (DOM index ile birlikte)
                                        if (!string.IsNullOrEmpty(foundTrackingNumber))
                                        {
                                            noteItems.Add((noteDate, i, foundTrackingNumber));
                                            _logger.LogInformation($"Talep {talepId} için note[{i}]'da kargo numarası bulundu: {foundTrackingNumber} (Tarih: {noteDate}, DOM Index: {i})");
                                }
                            }
                            catch (Exception ex)
                            {
                                        _logger.LogWarning($"Talep {talepId} için note[{i}] işlenirken hata: {ex.Message}");
                                        continue;
                                    }
                                }
                                
                                // Önce tarihe göre, tarih yoksa DOM index'e göre sırala (en yeni = en büyük tarih veya en büyük index)
                                if (noteItems.Count > 0)
                                {
                                    // Tarih parse edilebilenler varsa tarihe göre, yoksa DOM index'e göre sırala
                                    var hasValidDates = noteItems.Any(x => x.date != DateTime.MinValue);
                                    
                                    if (hasValidDates)
                                    {
                                        // Tarihe göre sırala (en yeni = en büyük tarih)
                                        var sortedNotes = noteItems.OrderByDescending(x => x.date).ThenByDescending(x => x.domIndex).ToList();
                                        trackingNumber = sortedNotes[0].trackingNumber;
                                        _logger.LogInformation($"Talep {talepId} için en yeni kargo numarası seçildi: {trackingNumber} (Tarih: {sortedNotes[0].date}, DOM Index: {sortedNotes[0].domIndex})");
                                    }
                                    else
                                    {
                                        // Tarih yoksa DOM index'e göre sırala (en yeni = en büyük index, yani en alttaki)
                                        var sortedNotes = noteItems.OrderByDescending(x => x.domIndex).ToList();
                                        trackingNumber = sortedNotes[0].trackingNumber;
                                        _logger.LogInformation($"Talep {talepId} için tarih bilgisi olmadığından DOM index'e göre en yeni kargo numarası seçildi: {trackingNumber} (DOM Index: {sortedNotes[0].domIndex})");
                                    }
                                    _logger.LogInformation($"Talep {talepId} için toplam {noteItems.Count} kargo numarası bulundu, en yeni olan seçildi.");
                                }
                                
                                // Eğer note elementleri bulunamazsa eski yöntemi kullan (fallback)
                                if (string.IsNullOrEmpty(trackingNumber))
                                {
                                    _logger.LogWarning($"Talep {talepId} için note elementleri işlenemedi, fallback yöntemine geçiliyor.");
                                    string notesContent = driver.PageSource;
                                    
                                    // "Kargo Takip No" ile başlayan satırı bul
                            var kargoTakipLineMatch = Regex.Match(notesContent, @"Kargo\s*Takip\s*No\s*[:\-]\s*([^\s\r\n]+)", RegexOptions.IgnoreCase);
                            if (kargoTakipLineMatch.Success)
                            {
                                var fullTrackingFromLine = kargoTakipLineMatch.Groups[1].Value.Trim();
                                var upsFromLine = Regex.Match(fullTrackingFromLine, @"(1[Zz][0-9A-Za-z]+)");
                                if (upsFromLine.Success)
                                {
                                    trackingNumber = upsFromLine.Groups[1].Value;
                                }
                            }
                            
                                    // UPS formatları (fallback)
                            if (string.IsNullOrEmpty(trackingNumber))
                            {
                                var allUpsMatches = Regex.Matches(notesContent, @"1[Zz][0-9A-Za-z]{14,20}", RegexOptions.IgnoreCase);
                                if (allUpsMatches.Count > 0)
                                {
                                    trackingNumber = allUpsMatches[allUpsMatches.Count - 1].Value;
                                }
                            }
                            
                                    // Aras Kargo formatı (fallback)
                            if (string.IsNullOrEmpty(trackingNumber))
                            {
                                var arasMatches = Regex.Matches(notesContent, @"[A-Z]{2}\d{9}");
                                if (arasMatches.Count > 0)
                                {
                                    trackingNumber = arasMatches[arasMatches.Count - 1].Value;
                                }
                            }
                            
                                    // Yurtiçi Kargo formatı (fallback)
                            if (string.IsNullOrEmpty(trackingNumber))
                            {
                                var yurticiMatches = Regex.Matches(notesContent, @"\d{13}");
                                if (yurticiMatches.Count > 0)
                                {
                                    trackingNumber = yurticiMatches[yurticiMatches.Count - 1].Value;
                                }
                            }
                            
                                    // MNG Kargo formatı (fallback)
                            if (string.IsNullOrEmpty(trackingNumber))
                            {
                                var mngMatches = Regex.Matches(notesContent, @"MNG\d{10}");
                                if (mngMatches.Count > 0)
                                {
                                    trackingNumber = mngMatches[mngMatches.Count - 1].Value;
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning($"Talep {talepId} için notlar bölümü alınırken hata: {ex.Message}");
                                // Fallback: Tüm sayfa içeriğinden ara
                                string notesContent = driver.PageSource;
                                var allUpsMatches = Regex.Matches(notesContent, @"1[Zz][0-9A-Za-z]{14,20}", RegexOptions.IgnoreCase);
                                if (allUpsMatches.Count > 0)
                                {
                                    trackingNumber = allUpsMatches[allUpsMatches.Count - 1].Value;
                                }
                            }

                            // Sadece kargo numarası bulunan talepleri ekle
                            if (!string.IsNullOrEmpty(trackingNumber))
                            {
                                // Eğer bu takip numarası daha önce işlenmediyse ekle
                                if (!islenenKargolar.Contains(trackingNumber))
                                {
                                    islenenKargolar.Add(trackingNumber);
                                    bulunanKargoSayisi++;

                                    var kargoData = new KargoData
                                    {
                                        TrackingNumber = FormatTrackingNumber(trackingNumber),
                                        StoreId = magazaId,
                                        RequestId = talepId,
                                        RequestSubject = konu,
                                        Status = "Beklemede",
                                        LastUpdated = DateTime.Now
                                    };

                                    await AddKargo(kargoData);
                                    yeniEklenenKargolar.Add(kargoData);
                                    _logger.LogInformation($"✅ KARGO EKLENDİ: {trackingNumber} - Mağaza: {magazaId} - Talep: {talepId} - Konu: {konu}");
                                    
                                    if (!string.IsNullOrEmpty(sessionId)) {
                                        UpdateSessionStatus(sessionId, $"✅ {bulunanKargoSayisi} kargo eklendi: {trackingNumber}");
                                    }
                                }
                                else
                                {
                                    _logger.LogInformation($"⚠️ Kargo numarası {trackingNumber} daha önce işlenmiş, atlanıyor.");
                                }
                            }
                            else
                            {
                                _logger.LogInformation($"ℹ️ Talep ID {talepId} için kargo numarası bulunamadı - Konu: {konu}");
                            }

                            // Geri dön
                            driver.Navigate().Back();
                            await Task.Delay(500); // Geri dönmek için bekleme süresi düşürüldü
                            islenenKargolar.Add("TALEP_" + talepId); // İşlendi olarak işaretle
                            processedInIteration++;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Talep işlenirken hata oluştu: {talepId}");
                            islenenKargolar.Add("TALEP_" + talepId); // İşlendi olarak işaretle
                            processedInIteration++;
                            try { driver.Navigate().Back(); await Task.Delay(500); } catch { } // Hata durumunda da geri dönme bekleme süresi düşürüldü
                            continue;
                        }
                    }
                    // Döngü sonunda işlenen talep sayısını kontrol et
                    int currentProcessedCount = islenenKargolar.Count(k => k.StartsWith("TALEP_"));
                    if (currentProcessedCount >= totalItems)
                    {
                        _logger.LogInformation("Tüm talepler işlendi. Döngü sonlandırılıyor.");
                        break;
                    }

                    // Eğer bu iterasyonda hiç yeni talep işlenmediyse
                    if (processedInIteration == 0)
                    {
                         noProgressCount++;
                        _logger.LogInformation($"Bu iterasyonda yeni talep işlenmedi. İlerleme olmayan deneme sayısı: {noProgressCount}");
                        if (noProgressCount >= maxNoProgress)
                        {
                            _logger.LogWarning($"{maxNoProgress} denemedir yeni talep işlenemiyor. Tüm taleplerin yüklenmemiş olabileceği veya başka bir sorun olabileceği düşünülüyor. İşlem sonlandırılıyor.");
                            break; // Belirli sayıda denemeye rağmen ilerleme yoksa döngüyü sonlandır
                        }
                    }
                    else
                    {
                        noProgressCount = 0; // İlerleme olduysa sayacı sıfırla
                    }

                    // Aşağı kaydır ve yeni elementlerin yüklenmesini bekle
                    _logger.LogInformation("Aşağı kaydırılıyor...");
                    ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollTop += 150;", scrollContainer); // 150 piksel aşağı kaydır
                    await Task.Delay(1500); // Yeni içeriğin yüklenmesi için bekle

                    // Scroll sonrası toplam talep sayısını kontrol et (sadece bilgi amaçlı)
                    var afterScrollTaleplerCount = driver.FindElements(By.CssSelector("div.grid-row")).Count;
                    _logger.LogInformation($"Scroll sonrası toplam {afterScrollTaleplerCount} talep elementine ulaşıldı.");
                }

                if (!string.IsNullOrEmpty(sessionId)) {
                    UpdateSessionStatus(sessionId, $"✅ İşlem tamamlandı! {bulunanKargoSayisi} kargo başarıyla eklendi!");
                }
                _logger.LogInformation($"İşlem tamamlandı. Toplam {islenenKargolar.Count(k => k.StartsWith("TALEP_"))} talep işlendi, {bulunanKargoSayisi} kargo bulundu.");
                return yeniEklenenKargolar;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Veri çekme sırasında hata oluştu");
                if (!string.IsNullOrEmpty(sessionId)) {
                    UpdateSessionStatus(sessionId, "❌ Veri çekme sırasında hata oluştu!");
                }
                return new List<KargoData>();
            }
        }

        public async Task LoadDataFrom4me(string? email, string? password)
        {
            email = string.IsNullOrEmpty(email) ? _fourMeEmail : email;
            password = string.IsNullOrEmpty(password) ? _fourMePassword : password;
            if (string.IsNullOrEmpty(email))
            {
                _logger.LogError("4me e-posta adresi eksik");
                throw new InvalidOperationException("4me e-posta adresi eksik");
            }
            if (string.IsNullOrEmpty(password))
            {
                _logger.LogError("4me şifre eksik");
                throw new InvalidOperationException("4me şifre eksik");
            }

            var options = new ChromeOptions();
            options.AddArgument("--headless");
            options.AddArgument("--no-sandbox");
            options.AddArgument("--disable-dev-shm-usage");
            options.AddArgument("--disable-gpu");
            options.AddArgument("--window-size=1920,1080");
            options.AddArgument("--disable-extensions");
            options.AddArgument("--disable-infobars");
            options.AddArgument("--remote-debugging-port=9222");
            options.AddArgument("--disable-blink-features=AutomationControlled");
            options.AddArgument("--disable-notifications");
            options.AddArgument("--disable-popup-blocking");
            options.AddArgument("--start-maximized");
            options.AddArgument("--disable-web-security");
            options.AddArgument("--allow-running-insecure-content");
            options.AddArgument("--ignore-certificate-errors");
            options.AddArgument("--user-agent=Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/109.0.0.0 Safari/537.36");

            var service = ChromeDriverService.CreateDefaultService();
            service.HideCommandPromptWindow = true;

            using (var driver = new ChromeDriver(service, options))
            {
                try
                {
                    // 4me sayfası açılıyor...
                    driver.Navigate().GoToUrl("https://gratis-it.4me.com/inbox?q=servicedesk#table=true");
                    await Task.Delay(5000);
                    
                    var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(30));
                    
                    // E-posta girişi
                    var emailInput = wait.Until(d => d.FindElement(By.Id("i0116")));
                    emailInput.Clear();
                    emailInput.SendKeys(email);
                    await Task.Delay(3000);
                    
                    // İleri butonu 1
                    var ileriBtn1 = driver.FindElement(By.Id("idSIButton9"));
                    ileriBtn1.Click();
                    await Task.Delay(3000);
                    
                    // Şifre girişi
                    var passwordInput = driver.FindElement(By.Id("i0118"));
                    passwordInput.Clear();
                    passwordInput.SendKeys(password);
                    await Task.Delay(3000);
                    
                    // İleri butonu 2
                    var ileriBtn2 = driver.FindElement(By.Id("idSIButton9"));
                    ileriBtn2.Click();
                    await Task.Delay(3000);
                    
                    // İleri butonu 3
                    var ileriBtn3 = driver.FindElement(By.Id("idSIButton9"));
                    ileriBtn3.Click();
                    await Task.Delay(5000);

                    // Toplam öğe sayısını al
                    int totalItems = 0;
                    try
                    {
                        var totalItemsElement = wait.Until(d => d.FindElement(By.Id("view_counter")));
                        var totalItemsText = totalItemsElement.Text.Trim();
                        if (int.TryParse(totalItemsText.Replace(" öğe", ""), out totalItems))
                        {
                            _logger.LogInformation($"Toplam öğe sayısı bulundu: {totalItems}");
                        }
                        else
                        {
                            _logger.LogWarning($"Toplam öğe sayısı metni sayıya çevrilemedi: {totalItemsText}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Toplam öğe sayısı elementi bulunamadı: {ex.Message}");
                    }

                    var islenenTalepSayisi = 0;
                    var bulunanKargoSayisi = 0;
                    var islenenKargolar = new HashSet<string>();
                    var yeniEklenenKargolar = new List<KargoData>();

                    // Scroll yaparak talepleri dinamik olarak yükle ve işle
                    var scrollContainer = wait.Until(d => d.FindElement(By.Id("view_list_container")));
                    
                    _logger.LogInformation("Talepleri scroll yaparak yükleme ve işleme başlatılıyor...");
                    
                    int previousTaleplerCount = 0;
                    int noProgressCount = 0; // İlerleme olmayan scroll denemesi sayısı
                    const int maxNoProgress = 5; // Maksimum ilerleme olmayan deneme sayısı

                    while (islenenKargolar.Count(k => k.StartsWith("TALEP_")) < totalItems)
                    {
                        // Mevcut görünümdeki tüm talepleri al
                        var currentTalepler = driver.FindElements(By.CssSelector("div.grid-row")).ToList();
                        _logger.LogInformation($"Mevcut görünümde {currentTalepler.Count} talep bulundu.");

                        int processedInIteration = 0;

                        foreach (var talep in currentTalepler)
                        {
                            // Talep ID'sini al
                            string talepId = "";
                            try {
                                var talepIdElement = talep.FindElement(By.CssSelector("div.cell-path"));
                                var talepIdText = talepIdElement.Text.Trim();
                                talepId = Regex.Match(talepIdText, @"\d+").Value;
                            }
                            catch {
                                // Eğer ID alınamazsa bu elementi atla ve logla
                                _logger.LogWarning("Bir talep elementi için ID bulunamadı, atlanıyor.");
                                continue; // Bu elementi atla, işlenmiş sayma
                            }
                            
                            // Eğer bu talep daha önce işlenmediyse devam et
                            if (islenenKargolar.Contains("TALEP_" + talepId))
                            {
                                continue; // Zaten işlenmiş, atla
                            }

                            // Talep işlenmemiş, şimdi işle
                            try
                            {
                                // Konu kontrolü
                                string konu = "";
                                try {
                                    var konuElement = talep.FindElement(By.CssSelector("div.cell-subject span"));
                                    konu = konuElement.GetAttribute("title") ?? konuElement.Text.Trim();
                                }
                                catch {
                                    try {
                                        var konuElement = talep.FindElement(By.CssSelector("div.cell-subject"));
                                        konu = konuElement.Text.Trim();
                                    }
                                    catch {
                                        _logger.LogWarning($"Talep {talepId} için konu bulunamadı, atlanıyor.");
                                        islenenKargolar.Add("TALEP_" + talepId); // İşlendi olarak işaretle
                                        processedInIteration++;
                                        continue;
                                    }
                                }
                                
                                // Mağaza bilgisini liste sayfasından al (daha hızlı)
                                string magazaId = "";
                                try {
                                    var requesterElement = talep.FindElement(By.CssSelector("div.cell-requester span"));
                                    magazaId = requesterElement.GetAttribute("title") ?? requesterElement.Text.Trim();
                                    // "- Gratis" kısmını temizle
                                    if (magazaId.Contains(" - "))
                                    {
                                        magazaId = magazaId.Split(" - ")[0].Trim();
                                    }
                                    _logger.LogInformation($"Talep {talepId} için mağaza bilgisi liste sayfasından alındı: {magazaId}");
                                }
                                catch (Exception ex) {
                                    _logger.LogWarning($"Talep {talepId} için mağaza bilgisi liste sayfasından alınamadı: {ex.Message}");
                                }

                                // Artık tüm talepleri kontrol ediyoruz, konu filtresi kaldırıldı
                                _logger.LogInformation($"Talep {talepId} işleniyor, konu: {konu}, mağaza: {magazaId}");

                                // Talebe tıkla ve detayları kontrol et
                                try {
                                    talep.Click();
                                    await Task.Delay(750); // Tıklama sonrası biraz daha bekleme süresi düşürüldü
                                }
                                catch (Exception ex) {
                                    _logger.LogError($"Talep {talepId} tıklanamadı: {ex.Message}");
                                    islenenKargolar.Add("TALEP_" + talepId); // İşlendi olarak işaretle
                                    processedInIteration++;
                                    continue;
                                }

                                // Talep detay sayfası elementlerini beklemek için WebDriverWait oluştur
                                var talepWait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));

                                // Sayfanın yüklendiğini belirten bir elementi bekleyin
                                try
                                {
                                    talepWait.Until(d => d.FindElement(By.ClassName("header_bar_inner")));
                                    _logger.LogInformation($"Talep {talepId} detay sayfası yüklendi.");
                                }
                                catch (WebDriverTimeoutException)
                                {
                                    _logger.LogWarning($"Talep {talepId} detay sayfası yüklenemedi, atlanıyor.");
                                    islenenKargolar.Add("TALEP_" + talepId); // İşlendi olarak işaretle
                                    processedInIteration++;
                                    try { driver.Navigate().Back(); await Task.Delay(1000); } catch { } // Geri dönerken daha uzun bekle
                                    continue;
                                }

                                // Mağaza bilgisi artık liste sayfasından alınıyor, detay sayfasından alma gereksiz

                                if (string.IsNullOrEmpty(magazaId))
                                {
                                    _logger.LogWarning($"Talep {talepId} için mağaza ID'si boş veya null kaldı.");
                                    islenenKargolar.Add("TALEP_" + talepId); // İşlendi olarak işaretle
                                    processedInIteration++;
                                    driver.Navigate().Back();
                                    await Task.Delay(1000); // Geri dönerken daha uzun bekle
                                    continue;
                                }

                                // Notlar bölümünden kargo numarasını bul - Tarihe göre en yenisini seç
                                var trackingNumber = "";
                                
                                try
                                {
                                    // Her note'u ayrı ayrı ele al ve tarih bilgisiyle birlikte kargo numarasını bul
                                    var noteItems = new List<(DateTime date, int domIndex, string trackingNumber)>();
                                    
                                    // Notes list container'ı bul
                                    var notesListElements = driver.FindElements(By.CssSelector("li div.note"));
                                    _logger.LogInformation($"Talep {talepId} için {notesListElements.Count} note elementi bulundu.");
                                    
                                    // NOT: Note'lar DOM'da en eski en üstte (index 0), en yeni en altta (son index)
                                    // DOM index'ini de kullanarak en yeni olanı bulalım
                                    for (int i = 0; i < notesListElements.Count; i++)
                                    {
                                        var noteElement = notesListElements[i];
                                        try
                                        {
                                            // Note tarihini al
                                            DateTime noteDate = DateTime.MinValue;
                                            try
                                            {
                                                var dateElement = noteElement.FindElement(By.CssSelector("span.note_at.datetime"));
                                                var dateTimeAttr = dateElement.GetAttribute("data-datetime");
                                                if (!string.IsNullOrEmpty(dateTimeAttr))
                                                {
                                                    if (DateTime.TryParse(dateTimeAttr, out var parsedDate))
                                                    {
                                                        noteDate = parsedDate;
                                                        _logger.LogInformation($"Talep {talepId} için note[{i}] tarihi bulundu: {noteDate}");
                                                    }
                                                }
                                            }
                                            catch { }
                                            
                                            // Note içeriğini al
                                            string noteText = "";
                                            try
                                            {
                                                var noteContentElement = noteElement.FindElement(By.CssSelector("div.note-content"));
                                                noteText = noteContentElement.Text ?? "";
                                            }
                                            catch
                                            {
                                                noteText = noteElement.Text ?? "";
                                            }
                                            
                                            if (string.IsNullOrEmpty(noteText))
                                                continue;
                                            
                                            // Bu note'dan kargo numarası bul
                                            string foundTrackingNumber = "";
                                            
                                            // "Kargo Takip No" ile başlayan satırı bul
                                            var kargoTakipLineMatch = Regex.Match(noteText, @"Kargo\s*Takip\s*No\s*[:\-]\s*([^\s\r\n]+)", RegexOptions.IgnoreCase);
                                            if (kargoTakipLineMatch.Success)
                                            {
                                                var fullTrackingFromLine = kargoTakipLineMatch.Groups[1].Value.Trim();
                                                var upsFromLine = Regex.Match(fullTrackingFromLine, @"(1[Zz][0-9A-Za-z]+)");
                                                if (upsFromLine.Success)
                                                {
                                                    foundTrackingNumber = upsFromLine.Groups[1].Value;
                                                }
                                            }
                                            
                                            // UPS formatları
                                            if (string.IsNullOrEmpty(foundTrackingNumber))
                                            {
                                                var allUpsMatches = Regex.Matches(noteText, @"1[Zz][0-9A-Za-z]{14,20}", RegexOptions.IgnoreCase);
                                                if (allUpsMatches.Count > 0)
                                                {
                                                    foundTrackingNumber = allUpsMatches[allUpsMatches.Count - 1].Value;
                                                }
                                            }
                                            
                                            // Aras Kargo formatı
                                            if (string.IsNullOrEmpty(foundTrackingNumber))
                                            {
                                                var arasMatches = Regex.Matches(noteText, @"[A-Z]{2}\d{9}");
                                                if (arasMatches.Count > 0)
                                                {
                                                    foundTrackingNumber = arasMatches[arasMatches.Count - 1].Value;
                                                }
                                            }
                                            
                                            // Yurtiçi Kargo formatı
                                            if (string.IsNullOrEmpty(foundTrackingNumber))
                                            {
                                                var yurticiMatches = Regex.Matches(noteText, @"\d{13}");
                                                if (yurticiMatches.Count > 0)
                                                {
                                                    foundTrackingNumber = yurticiMatches[yurticiMatches.Count - 1].Value;
                                                }
                                            }
                                            
                                            // MNG Kargo formatı
                                            if (string.IsNullOrEmpty(foundTrackingNumber))
                                            {
                                                var mngMatches = Regex.Matches(noteText, @"MNG\d{10}");
                                                if (mngMatches.Count > 0)
                                                {
                                                    foundTrackingNumber = mngMatches[mngMatches.Count - 1].Value;
                                                }
                                            }
                                            
                                            // Eğer kargo numarası bulunduysa listeye ekle (DOM index ile birlikte)
                                            if (!string.IsNullOrEmpty(foundTrackingNumber))
                                            {
                                                noteItems.Add((noteDate, i, foundTrackingNumber));
                                                _logger.LogInformation($"Talep {talepId} için note[{i}]'da kargo numarası bulundu: {foundTrackingNumber} (Tarih: {noteDate}, DOM Index: {i})");
                                    }
                                }
                                catch (Exception ex)
                                {
                                            _logger.LogWarning($"Talep {talepId} için note[{i}] işlenirken hata: {ex.Message}");
                                            continue;
                                        }
                                    }
                                    
                                    // Önce tarihe göre, tarih yoksa DOM index'e göre sırala (en yeni = en büyük tarih veya en büyük index)
                                    if (noteItems.Count > 0)
                                    {
                                        // Tarih parse edilebilenler varsa tarihe göre, yoksa DOM index'e göre sırala
                                        var hasValidDates = noteItems.Any(x => x.date != DateTime.MinValue);
                                        
                                        if (hasValidDates)
                                        {
                                            // Tarihe göre sırala (en yeni = en büyük tarih)
                                            var sortedNotes = noteItems.OrderByDescending(x => x.date).ThenByDescending(x => x.domIndex).ToList();
                                            trackingNumber = sortedNotes[0].trackingNumber;
                                            _logger.LogInformation($"Talep {talepId} için en yeni kargo numarası seçildi: {trackingNumber} (Tarih: {sortedNotes[0].date}, DOM Index: {sortedNotes[0].domIndex})");
                                        }
                                        else
                                        {
                                            // Tarih yoksa DOM index'e göre sırala (en yeni = en büyük index, yani en alttaki)
                                            var sortedNotes = noteItems.OrderByDescending(x => x.domIndex).ToList();
                                            trackingNumber = sortedNotes[0].trackingNumber;
                                            _logger.LogInformation($"Talep {talepId} için tarih bilgisi olmadığından DOM index'e göre en yeni kargo numarası seçildi: {trackingNumber} (DOM Index: {sortedNotes[0].domIndex})");
                                        }
                                        _logger.LogInformation($"Talep {talepId} için toplam {noteItems.Count} kargo numarası bulundu, en yeni olan seçildi.");
                                    }
                                    
                                    // Eğer note elementleri bulunamazsa eski yöntemi kullan (fallback)
                                    if (string.IsNullOrEmpty(trackingNumber))
                                    {
                                        _logger.LogWarning($"Talep {talepId} için note elementleri işlenemedi, fallback yöntemine geçiliyor.");
                                        string notesContent = driver.PageSource;
                                        
                                        // "Kargo Takip No" ile başlayan satırı bul
                                var kargoTakipLineMatch = Regex.Match(notesContent, @"Kargo\s*Takip\s*No\s*[:\-]\s*([^\s\r\n]+)", RegexOptions.IgnoreCase);
                                if (kargoTakipLineMatch.Success)
                                {
                                    var fullTrackingFromLine = kargoTakipLineMatch.Groups[1].Value.Trim();
                                    var upsFromLine = Regex.Match(fullTrackingFromLine, @"(1[Zz][0-9A-Za-z]+)");
                                    if (upsFromLine.Success)
                                    {
                                        trackingNumber = upsFromLine.Groups[1].Value;
                                    }
                                }
                                
                                        // UPS formatları (fallback)
                                if (string.IsNullOrEmpty(trackingNumber))
                                {
                                    var allUpsMatches = Regex.Matches(notesContent, @"1[Zz][0-9A-Za-z]{14,20}", RegexOptions.IgnoreCase);
                                    if (allUpsMatches.Count > 0)
                                    {
                                        trackingNumber = allUpsMatches[allUpsMatches.Count - 1].Value;
                                    }
                                }
                                
                                        // Aras Kargo formatı (fallback)
                                if (string.IsNullOrEmpty(trackingNumber))
                                {
                                    var arasMatches = Regex.Matches(notesContent, @"[A-Z]{2}\d{9}");
                                    if (arasMatches.Count > 0)
                                    {
                                        trackingNumber = arasMatches[arasMatches.Count - 1].Value;
                                    }
                                }
                                
                                        // Yurtiçi Kargo formatı (fallback)
                                if (string.IsNullOrEmpty(trackingNumber))
                                {
                                    var yurticiMatches = Regex.Matches(notesContent, @"\d{13}");
                                    if (yurticiMatches.Count > 0)
                                    {
                                        trackingNumber = yurticiMatches[yurticiMatches.Count - 1].Value;
                                    }
                                }
                                
                                        // MNG Kargo formatı (fallback)
                                if (string.IsNullOrEmpty(trackingNumber))
                                {
                                    var mngMatches = Regex.Matches(notesContent, @"MNG\d{10}");
                                    if (mngMatches.Count > 0)
                                    {
                                        trackingNumber = mngMatches[mngMatches.Count - 1].Value;
                                            }
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogWarning($"Talep {talepId} için notlar bölümü alınırken hata: {ex.Message}");
                                    // Fallback: Tüm sayfa içeriğinden ara
                                    string notesContent = driver.PageSource;
                                    var allUpsMatches = Regex.Matches(notesContent, @"1[Zz][0-9A-Za-z]{14,20}", RegexOptions.IgnoreCase);
                                    if (allUpsMatches.Count > 0)
                                    {
                                        trackingNumber = allUpsMatches[allUpsMatches.Count - 1].Value;
                                    }
                                }

                                // Sadece kargo numarası bulunan talepleri ekle
                                if (!string.IsNullOrEmpty(trackingNumber))
                                {
                                    // Eğer bu takip numarası daha önce işlenmediyse ekle
                                    if (!islenenKargolar.Contains(trackingNumber))
                                    {
                                        islenenKargolar.Add(trackingNumber);
                                        bulunanKargoSayisi++;

                                        var kargoData = new KargoData
                                        {
                                            TrackingNumber = trackingNumber,
                                            StoreId = magazaId,
                                            RequestId = talepId,
                                            RequestSubject = konu,
                                            Status = "Beklemede",
                                            EstimatedDelivery = "-",
                                            LastUpdated = DateTime.Now
                                        };

                                        await AddKargo(kargoData);
                                        yeniEklenenKargolar.Add(kargoData);
                                        _logger.LogInformation($"✅ KARGO EKLENDİ: {trackingNumber} - Mağaza: {magazaId} - Talep: {talepId} - Konu: {konu}");
                                    }
                                    else
                                    {
                                        _logger.LogInformation($"⚠️ Kargo numarası {trackingNumber} daha önce işlenmiş, atlanıyor.");
                                    }
                                }
                                else
                                {
                                    _logger.LogInformation($"ℹ️ Talep ID {talepId} için kargo numarası bulunamadı - Konu: {konu}");
                                }

                                // Geri dön
                                driver.Navigate().Back();
                                await Task.Delay(500); // Geri dönmek için bekleme süresi düşürüldü
                                islenenKargolar.Add("TALEP_" + talepId); // İşlendi olarak işaretle
                                processedInIteration++;
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, $"Talep işlenirken hata oluştu: {talepId}");
                                islenenKargolar.Add("TALEP_" + talepId); // İşlendi olarak işaretle
                                processedInIteration++;
                                try { driver.Navigate().Back(); await Task.Delay(500); } catch { } // Hata durumunda da geri dönme bekleme süresi düşürüldü
                                continue;
                            }
                        }

                        // Döngü sonunda işlenen talep sayısını kontrol et
                        int currentProcessedCount = islenenKargolar.Count(k => k.StartsWith("TALEP_"));
                        if (currentProcessedCount >= totalItems)
                        {
                            _logger.LogInformation("Tüm talepler işlendi. Döngü sonlandırılıyor.");
                            break;
                        }

                        // Eğer bu iterasyonda hiç yeni talep işlenmediyse
                        if (processedInIteration == 0)
                        {
                             noProgressCount++;
                            _logger.LogInformation($"Bu iterasyonda yeni talep işlenmedi. İlerleme olmayan deneme sayısı: {noProgressCount}");
                            if (noProgressCount >= maxNoProgress)
                            {
                                _logger.LogWarning($"{maxNoProgress} denemedir yeni talep işlenemiyor. Tüm taleplerin yüklenmemiş olabileceği veya başka bir sorun olabileceği düşünülüyor. İşlem sonlandırılıyor.");
                                break; // Belirli sayıda denemeye rağmen ilerleme yoksa döngüyü sonlandır
                            }
                        }
                        else
                        {
                            noProgressCount = 0; // İlerleme olduysa sayacı sıfırla
                        }

                        // Aşağı kaydır ve yeni elementlerin yüklenmesini bekle
                        _logger.LogInformation("Aşağı kaydırılıyor...");
                        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollTop += 150;", scrollContainer); // 150 piksel aşağı kaydır
                        await Task.Delay(1500); // Yeni içeriğin yüklenmesi için bekle

                        // Scroll sonrası toplam talep sayısını kontrol et (sadece bilgi amaçlı)
                        var afterScrollTaleplerCount = driver.FindElements(By.CssSelector("div.grid-row")).Count;
                        _logger.LogInformation($"Scroll sonrası toplam {afterScrollTaleplerCount} talep elementine ulaşıldı.");
                    }

                    _logger.LogInformation($"İşlem tamamlandı. Toplam {islenenKargolar.Count(k => k.StartsWith("TALEP_"))} talep işlendi, {bulunanKargoSayisi} kargo bulundu.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "4me verileri yüklenirken hata oluştu");
                    throw;
                }
            }
        }

        public async Task DeleteKargo(string trackingNumber)
        {
            if (string.IsNullOrEmpty(trackingNumber))
                return;

            var kargo = _kargoList.FirstOrDefault(k => k.TrackingNumber == trackingNumber);
            if (kargo != null)
            {
                _kargoList.Remove(kargo);
                SaveKargoData();
                _logger.LogInformation($"Kargo silindi: {trackingNumber}");
            }
        }

        public async Task DeleteAllKargos()
        {
            _kargoList.Clear();
            SaveKargoData();
            _logger.LogInformation("Tüm kargolar silindi.");
            await Task.CompletedTask;
        }
    }

    public class KargoData
    {
        [JsonPropertyName("takipNo")]
        public string TrackingNumber { get; set; } = "";

        [JsonPropertyName("magazaId")]
        public string StoreId { get; set; } = "";

        [JsonPropertyName("talepId")]
        public string RequestId { get; set; } = "";

        [JsonPropertyName("talepAdi")]
        public string RequestSubject { get; set; } = "";

        [JsonPropertyName("durum")]
        public string Status { get; set; } = "Beklemede";

        [JsonPropertyName("ongorulenTeslimat")]
        public string EstimatedDelivery { get; set; } = "-";

        [JsonPropertyName("sonGuncelleme")]
        public DateTime LastUpdated { get; set; } = DateTime.Now;
    }
}

