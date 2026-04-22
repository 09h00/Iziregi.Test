// File: Models/WorkOrder.cs
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

    // Pipeline (5 étapes exclusives)
    public bool IsInCreation { get; set; }           // En création
    public bool IsSentToCompany { get; set; }         // Envoyé à l’entreprise
    public bool IsQuoteReceived { get; set; }         // Devis rempli
    public bool IsSentToSigner { get; set; }          // Envoyé au signataire
    public bool IsValidated { get; set; }             // Validé

    // Flags indépendants
    public bool IsValidatedPdfSent { get; set; }      // PDF validé envoyé (auto)
    public bool IsPerformed { get; set; }             // Effectué (manuel)
    public bool IsCancelled { get; set; }             // Annulé (manuel)

    // Corbeille
    public bool IsTrashed { get; set; }               // Dans la corbeille
    public DateTime? TrashedAt { get; set; }          // Date de mise à la corbeille

    // Archives (NOUVEAU)
    public bool IsArchived { get; set; }              // Archivé
    public DateTime? ArchivedAt { get; set; }         // Date d’archivage

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

    public bool HasFullSignature =>
        !string.IsNullOrWhiteSpace(SignatureName) &&
        SignatureDate.HasValue &&
        SignaturePng != null &&
        SignaturePng.Length > 0;
}