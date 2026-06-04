// File: Models/WorkOrder.cs
namespace Iziregi.Test.Models;

public class WorkOrder
{
    public long Id { get; set; }

    public long? ProjectId { get; set; }
    public int BdrNumber { get; set; }

    // ✅ Dashboard : sélection multi (non persisté)
    public bool IsBatchSelected { get; set; }

    // Date de création du bon (date uniquement)
    public System.DateTime CreatedOn { get; set; } = System.DateTime.Today;

    public string Place { get; set; } = "";

    // ✅ Nouveau champ Demande : Etage (liste)
    public string Etage { get; set; } = "";

    public string RequestedBy { get; set; } = "";
    public string PerformedBy { get; set; } = "";
    public System.DateTime RequestDate { get; set; }

    // ✅ Nouveau : délai (date)
    public System.DateTime DeadlineDate { get; set; } = System.DateTime.Today;

    public string Reserve { get; set; } = "";

    // Pipeline
    public bool IsInCreation { get; set; }
    public bool IsSentToCompany { get; set; }
    public bool IsQuoteReceived { get; set; }
    public bool IsSentToSigner { get; set; }
    public bool IsValidated { get; set; }

    // ✅ NOUVEAU : décision de validation (affichée sur le dashboard)
    // Valeurs attendues : "Validé" | "Refusé" | "Annulé" | "" (non choisi)
    public string ValidationDecision { get; set; } = "";

    // Flags
    public bool IsValidatedPdfSent { get; set; }

    // ✅ On garde IsPerformed (compat), mais on ajoute un vrai champ date indépendant
    public bool IsPerformed { get; set; }

    public bool IsCancelled { get; set; }   // ✅ remis pour compat MainWindow

    // ✅ Nouvelles dates indépendantes (dashboard)
    public System.DateTime? DistributedAt { get; set; }   // "Distribué le"
    public System.DateTime? PerformedAt { get; set; }     // "Effectué le"

    // Corbeille
    public bool IsTrashed { get; set; }
    public System.DateTime? TrashedAt { get; set; }

    // Archives
    public bool IsArchived { get; set; }
    public System.DateTime? ArchivedAt { get; set; }

    public string Description { get; set; } = "";

    // Devis (ajouts)
    public string QuoteName { get; set; } = "";
    public System.DateTime QuoteDate { get; set; } = System.DateTime.Today;

    public double LaborHours { get; set; }
    public double LaborRate { get; set; }
    public double TravelQty { get; set; }
    public double TravelRate { get; set; }
    public double TvaRate { get; set; }

    // ✅ NOUVEAU : Rabais (%) appliqué sur le Total HT
    // (persisté en DB, utilisé pour recalculer HT/TVA/TTC)
    public double DiscountRate { get; set; }

    // =========================
    // ✅ NOUVEAU : Forfait selon doc annexé (ligne forfait)
    // =========================
    public double ForfaitQty { get; set; }
    public double ForfaitUnitPrice { get; set; }

    // =========================
    // ✅ NOUVEAU : PDF devis forfaitaire (lecture seule)
    // =========================
    public string ForfaitPdfFileName { get; set; } = "";
    public byte[]? ForfaitPdfFileBytes { get; set; }

    public string QuoteNotes { get; set; } = "";

    // Signature
    public string SignatureName { get; set; } = "";
    public System.DateTime? SignatureDate { get; set; }
    public byte[]? SignaturePng { get; set; }

    public bool HasFullSignature =>
        !string.IsNullOrWhiteSpace(SignatureName) &&
        SignatureDate.HasValue &&
        SignaturePng != null &&
        SignaturePng.Length > 0;

    // =========================
    // Affichage numérotation
    // Format: 01-P1 (01 = numéro du bon par projet, P1 = ID projet)
    // =========================
    public string ProjectTag
    {
        get
        {
            if (!ProjectId.HasValue || ProjectId.Value <= 0) return "";
            return $"P{ProjectId.Value}";
        }
    }

    public string BdrNumberDisplay
    {
        get
        {
            // 1 seul zéro devant pour 1..9
            if (BdrNumber < 0) return "0";
            return BdrNumber < 10 ? $"0{BdrNumber}" : BdrNumber.ToString();
        }
    }

    public string BdrDisplay
    {
        get
        {
            var tag = ProjectTag;
            if (string.IsNullOrWhiteSpace(tag))
                return BdrNumberDisplay;

            return $"{BdrNumberDisplay}-{tag}";
        }
    }
}