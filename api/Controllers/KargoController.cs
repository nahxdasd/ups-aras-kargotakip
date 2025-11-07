using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using System.Text.RegularExpressions;
using System.Net.Http;
using KargoTakip.Services;
using KargoTakip.Models;

[ApiController]
[Route("api/[controller]")]
public class KargoController : ControllerBase
{
    private readonly KargoService _service;
    private readonly HttpClient _httpClient;

    public KargoController(KargoService service)
    {
        _service = service;
        _httpClient = new HttpClient();
    }

    [HttpPost]
    public async Task<IActionResult> Add([FromBody] KargoData kargo)
    {
        if (kargo == null || string.IsNullOrEmpty(kargo.TrackingNumber))
            return BadRequest("Geçersiz kargo bilgisi.");

        var existingKargo = await _service.GetKargoByTrackingNumber(kargo.TrackingNumber);
        if (existingKargo != null)
            return Conflict("Takip numarası zaten mevcut.");

        await _service.AddKargo(kargo);
        return Ok();
    }
    
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var kargolar = await _service.GetAllKargos();
        return Ok(kargolar);
    }

    [HttpDelete("{trackingNumber}")]
    public async Task<IActionResult> Delete(string trackingNumber)
    {
        if (string.IsNullOrEmpty(trackingNumber))
            return BadRequest("Takip numarası gereklidir.");

        await _service.DeleteKargo(trackingNumber);
        return Ok(new { success = true, message = "Kargo başarıyla silindi" });
    }

    [HttpPost("load-from-4me")]
    public async Task<IActionResult> LoadFrom4Me([FromBody] FourMeCredentials? credentials = null)
    {
        string? email = credentials?.Email;
        string? password = credentials?.Password;
        try
        {
            await _service.LoadDataFrom4me(email, password);
            var kargolar = await _service.GetAllKargos();
            return Ok(new { success = true, message = "4me verileri başarıyla yüklendi", data = kargolar });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = $"4me verileri yüklenirken hata oluştu: {ex.Message}" });
        }
    }

    [HttpPost("update-all")]
    public async Task<IActionResult> UpdateAllKargos()
    {
        try
        {
            await _service.CheckKargoStatuses();
            var kargolar = await _service.GetAllKargos();
            return Ok(new { success = true, message = "Tüm kargolar güncellendi", data = kargolar });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = $"Kargolar güncellenirken hata oluştu: {ex.Message}" });
        }
    }

    [HttpDelete("delete-all")]
    public async Task<IActionResult> DeleteAllKargos()
    {
        await _service.DeleteAllKargos();
        return NoContent(); // 204 No Content
    }

    [HttpPost("login")]
    public async Task<IActionResult> InitiateLogin([FromBody] LoginRequest request)
    {
        if (string.IsNullOrEmpty(request.Email) || string.IsNullOrEmpty(request.Password))
        {
            return BadRequest(new { success = false, message = "Email ve şifre gereklidir." });
        }

        try
        {
            var result = await _service.InitiateLogin(request.Email, request.Password);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = $"Login hatası: {ex.Message}" });
        }
    }

    [HttpPost("verify-2fa")]
    public async Task<IActionResult> VerifyTwoFactor([FromBody] TwoFactorRequest request)
    {
        if (string.IsNullOrEmpty(request.SessionId) || string.IsNullOrEmpty(request.Code))
        {
            return BadRequest(new { success = false, message = "Session ID ve kod gereklidir." });
        }

        try
        {
            var result = await _service.VerifyTwoFactor(request.SessionId, request.Code);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = $"2FA doğrulama hatası: {ex.Message}" });
        }
    }

    [HttpGet("status/{sessionId}")]
    public IActionResult GetStatus(string sessionId)
    {
        try
        {
            var status = _service.GetSessionStatus(sessionId);
            return Ok(status);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { status = "Hata oluştu", isComplete = true });
        }
    }
}

public class FourMeCredentials
{
    public string? Email { get; set; }
    public string? Password { get; set; }
}
