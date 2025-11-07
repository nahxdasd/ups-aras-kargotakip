public class Kargo
{
    public string? Firma { get; set; }
    public string TakipNo { get; set; } = string.Empty;
    public string? MagazaID { get; set; }
    public string? TalepID { get; set; }
    public bool TeslimEdildi { get; set; }
    public string? OngorulenTeslimat { get; set; }
    public string LastUpdate { get; set; } = string.Empty;
}