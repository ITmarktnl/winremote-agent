using System.Drawing;

namespace Windowshulp.Agent;

/// <summary>
/// Huisstijl van het IT-bedrijf dat deze agent uitgeeft. De .exe heet bijvoorbeeld
/// "Windowshulp-jouwbedrijf.exe": het deel na het streepje is de tenant-slug. Later haalt de agent de
/// bijbehorende naam, kleur en logo op van windowshulp.nl; nu geldt de standaard-huisstijl.
/// </summary>
public sealed record Branding(string Slug, string Naam, string Ondertitel, Color Kleur)
{
	public const string SignaalBasis = "https://signaal.windowshulp.nl";
	public const string SignaalWs = "wss://signaal.windowshulp.nl";

	public static Branding Standaard { get; } =
		new("demo", "Windowshulp", "Hulp op afstand", Color.FromArgb(0xC8, 0x10, 0x2E));

	/// <summary>Leidt de tenant-slug af uit de bestandsnaam van de .exe.</summary>
	public static Branding UitBestandsnaam()
	{
		var naam = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "Windowshulp");
		var streep = naam.IndexOf('-');
		if (streep < 0 || streep == naam.Length - 1) return Standaard;
		var slug = naam[(streep + 1)..].Trim().ToLowerInvariant();
		return Standaard with { Slug = slug, Naam = MaakLeesbaar(slug) };
	}

	private static string MaakLeesbaar(string slug)
	{
		var woorden = slug.Split(new[] { '-', '_' }, StringSplitOptions.RemoveEmptyEntries)
			.Select(w => char.ToUpperInvariant(w[0]) + w[1..]);
		return string.Join(' ', woorden);
	}
}
