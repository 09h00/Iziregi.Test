// File: Models/Project.cs
namespace Iziregi.Test.Models;

public class Project
{
    public long Id { get; set; }

    // Nom affiché
    public string Name { get; set; } = "";

    // ✅ NOUVEAU : adresse séparée
    public string AddressLine { get; set; } = "";
    public string ZipCity { get; set; } = "";

    // ✅ Compat (ancien champ) : on le garde pour ne rien casser tant que tout n’est pas migré partout
    // (On le remplira encore pendant une phase de transition si nécessaire.)
    public string Address { get; set; } = "";

    // ✅ Optionnel : URL du projet (si tu veux aussi au niveau projet)
    public string Website { get; set; } = "";

    // ✅ Couleur du projet (HEX) : ex "#2563EB"
    public string ColorHex { get; set; } = "";

    // ✅ NOUVEAU : gestionnaire du dossier (nom + champ libre tél./email)
    public string ManagerName { get; set; } = "";
    public string ManagerContact { get; set; } = "";

    public bool IsActive { get; set; }

    // ✅ NOUVEAU (18.08.2026, demande de Joe) : dossier verrouillé par mot de passe. Hash stocké
    // (jamais le mot de passe en clair), voir Db.SetProjectPassword/VerifyProjectPassword.
    public string PasswordHash { get; set; } = "";

    public bool HasPassword => !string.IsNullOrWhiteSpace(PasswordHash);

    public string LockIcon => HasPassword ? "🔒" : "";
}