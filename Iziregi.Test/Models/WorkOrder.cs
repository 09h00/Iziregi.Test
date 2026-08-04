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

    // ✅ Délai (date). Pas de valeur par défaut (29.07.2026, demande de Joe) : default(DateTime)
    // signifie "pas de délai renseigné" (voir DeadlineDatePicker dans WorkOrderWindow, et
    // PdfService qui traite déjà default comme une case vide).
    public System.DateTime DeadlineDate { get; set; }

    public string Reserve { get; set; } = "";

    // Pipeline
    public bool IsInCreation { get; set; }
    public bool IsSentToCompany { get; set; }
    public bool IsQuoteReceived { get; set; }
    public bool IsSentToSigner { get; set; }
    public bool IsValidated { get; set; }

    // Timestamps des envois de liens BDR (pour détecter l'expiration)
    public System.DateTime? CompanyLinkSentAt { get; set; }
    public System.DateTime? SignerLinkSentAt { get; set; }

    // Lien entreprise expire après 9 jours sans devis reçu
    public bool IsCompanyLinkExpired =>
        IsSentToCompany && !IsQuoteReceived &&
        CompanyLinkSentAt.HasValue &&
        (System.DateTime.UtcNow - CompanyLinkSentAt.Value).TotalDays > 9;

    // Lien signataire expire après 9 jours sans validation
    public bool IsSignerLinkExpired =>
        IsSentToSigner && !IsValidated &&
        SignerLinkSentAt.HasValue &&
        (System.DateTime.UtcNow - SignerLinkSentAt.Value).TotalDays > 9;

    // Avertissement : lien va expirer dans ≤ 3 jours
    public bool IsCompanyLinkExpiringSoon =>
        IsSentToCompany && !IsQuoteReceived && CompanyLinkSentAt.HasValue &&
        !IsCompanyLinkExpired &&
        (System.DateTime.UtcNow - CompanyLinkSentAt.Value).TotalDays >= 6;

    public bool IsSignerLinkExpiringSoon =>
        IsSentToSigner && !IsValidated && SignerLinkSentAt.HasValue &&
        !IsSignerLinkExpired &&
        (System.DateTime.UtcNow - SignerLinkSentAt.Value).TotalDays >= 6;

    // Jours restants (pour affichage J-X)
    public int CompanyLinkDaysRemaining =>
        CompanyLinkSentAt.HasValue
            ? System.Math.Max(0, 9 - (int)System.Math.Floor((System.DateTime.UtcNow - CompanyLinkSentAt.Value).TotalDays))
            : 0;

    public int SignerLinkDaysRemaining =>
        SignerLinkSentAt.HasValue
            ? System.Math.Max(0, 9 - (int)System.Math.Floor((System.DateTime.UtcNow - SignerLinkSentAt.Value).TotalDays))
            : 0;

    public string CompanyLinkDaysRemainingLabel
    {
        get
        {
            if (!IsCompanyLinkExpiringSoon) return "";
            var d = CompanyLinkDaysRemaining;
            return d <= 1 ? "J-1" : $"J-{d}";
        }
    }

    public string SignerLinkDaysRemainingLabel
    {
        get
        {
            if (!IsSignerLinkExpiringSoon) return "";
            var d = SignerLinkDaysRemaining;
            return d <= 1 ? "J-1" : $"J-{d}";
        }
    }

    public bool HasExpiredLink => IsCompanyLinkExpired || IsSignerLinkExpired;

    public string ExpiredLinkTooltip
    {
        get
        {
            if (IsCompanyLinkExpired && IsSignerLinkExpired)
                return "Lien entreprise et lien signataire expirés — Regénérer dans la fiche";
            if (IsCompanyLinkExpired)
                return "Lien entreprise expiré (> 9 jours) — Regénérer dans la fiche";
            if (IsSignerLinkExpired)
                return "Lien signataire expiré (> 9 jours) — Regénérer dans la fiche";
            return "";
        }
    }

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
    // ✅ NOUVEAU (20.07.2026) : Forfait TTC saisi directement par l'utilisateur (montant lu sur
    // le pdf annexé de l'entreprise). Remplace le principe qty*prix unitaire ci-dessus pour les
    // nouveaux bons : HT et TVA sont recalculés à rebours à partir de ce montant TTC.
    // =========================
    public double ForfaitTtc { get; set; }

    // =========================
    // ✅ NOUVEAU : PDF devis forfaitaire (lecture seule)
    // =========================
    public string ForfaitPdfFileName { get; set; } = "";
    public byte[]? ForfaitPdfFileBytes { get; set; }

    // ✅ NOUVEAU (04.08.2026, demande de Joe) : N° du devis (saisi par l'entreprise), obligatoire
    // dès qu'un PDF est joint (voir WorkOrderWindow.EnsureQuoteRequiredFieldsOrWarn).
    public string ForfaitQuoteNumber { get; set; } = "";

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
    // Format: 01-D1 (01 = numéro du bon par projet, D1 = ID dossier/projet)
    // =========================
    public string ProjectTag
    {
        get
        {
            if (!ProjectId.HasValue || ProjectId.Value <= 0) return "";
            return $"D{ProjectId.Value}";
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