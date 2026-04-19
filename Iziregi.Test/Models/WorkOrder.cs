namespace Iziregi.Test.Models;

public class WorkOrder
{
    public long Id { get; set; }

    public long? ProjectId { get; set; }
    public int BdrNumber { get; set; }

    public string Place { get; set; } = "";
    public string RequestedBy { get; set; } = "";
    public string PerformedBy { get; set; } = "";
    public DateTime RequestDate { get; set; }

    public bool IsValidated { get; set; }
    public bool IsPerformed { get; set; }
    public bool IsCancelled { get; set; }

    public bool IsPendingValidation { get; set; }

    public string Description { get; set; } = "";

    // Devis
    public double LaborHours { get; set; }
    public double LaborRate { get; set; }
    public double TravelQty { get; set; }
    public double TravelRate { get; set; }
    public double TvaRate { get; set; }
    public string QuoteNotes { get; set; } = "";

    // Signature
    public string SignatureName { get; set; } = "";
    public DateTime? SignatureDate { get; set; }
    public byte[]? SignaturePng { get; set; }

    // Statuts process
    public bool IsSentToCompany { get; set; }     // export devis fait
    public bool IsQuoteReceived { get; set; }     // import réponse entreprise fait

    public bool HasFullSignature =>
        !string.IsNullOrWhiteSpace(SignatureName) &&
        SignatureDate.HasValue &&
        SignaturePng != null &&
        SignaturePng.Length > 0;

    public bool IsDraft =>
        !IsCancelled &&
        !IsSentToCompany &&
        !string.IsNullOrWhiteSpace(Place) &&
        !string.IsNullOrWhiteSpace(RequestedBy) &&
        !string.IsNullOrWhiteSpace(PerformedBy) &&
        !string.IsNullOrWhiteSpace(Description);

    // ✅ IMPORTANT : cette case doit tomber dès que IsQuoteReceived = true
    public bool IsPendingQuote =>
        !IsCancelled &&
        IsSentToCompany &&
        !IsQuoteReceived;
}