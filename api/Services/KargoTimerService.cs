using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using KargoTakip.Services;

public class KargoTimerService : BackgroundService
{
    private readonly KargoService _kargoService;

    public KargoTimerService(KargoService kargoService)
    {
        _kargoService = kargoService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Timer service is now disabled as updates will be done manually
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
} 