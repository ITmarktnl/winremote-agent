using System.Text.RegularExpressions;

namespace WinRemote.Agent;

internal static partial class Program
{
	[STAThread]
	private static void Main(string[] args)
	{
		ApplicationConfiguration.Initialize();

		var start = Startopdracht.Parse(args);
		if (start.ViaWebsite)
		{
			// Gestart via een winremote://-link vanaf de website. Eerst bevestigen, zoals bij elke
			// tool voor hulp op afstand hoort: een klik op een link mag nooit ongemerkt een sessie openen.
			var keuze = MessageBox.Show(
				"Wilt u een hulpsessie starten?\n\nUw technicus kan daarna uw scherm zien en uw computer bedienen. " +
				"Start alleen een sessie als u hier zelf om gevraagd bent.",
				"WinRemote – hulp op afstand",
				MessageBoxButtons.YesNo,
				MessageBoxIcon.Question,
				MessageBoxDefaultButton.Button1);
			if (keuze != DialogResult.Yes) return;
		}

		Application.Run(new MainForm(start));
	}
}

/// <summary>
/// Hoe de agent gestart is:
///  - zonder argumenten: zelf een nieuwe sessiecode aanvragen en tonen;
///  - <c>winremote://join/482917</c> (URL-protocol, geregistreerd door de installer): aansluiten op de
///    code die de klant al op winremote.nl ziet;
///  - <c>--code 482917</c>: hetzelfde, voor scripts.
/// </summary>
public sealed partial record Startopdracht(string? Code, bool ViaWebsite)
{
	[GeneratedRegex(@"(\d{6})")]
	private static partial Regex ZesCijfers();

	public static Startopdracht Parse(string[] args)
	{
		for (var i = 0; i < args.Length; i++)
		{
			var a = args[i];
			if (a.StartsWith("winremote:", StringComparison.OrdinalIgnoreCase))
			{
				var m = ZesCijfers().Match(a);
				return new Startopdracht(m.Success ? m.Groups[1].Value : null, ViaWebsite: true);
			}
			if (a is "--code" && i + 1 < args.Length)
			{
				var m = ZesCijfers().Match(args[i + 1]);
				if (m.Success) return new Startopdracht(m.Groups[1].Value, ViaWebsite: false);
			}
		}
		return new Startopdracht(null, ViaWebsite: false);
	}
}
