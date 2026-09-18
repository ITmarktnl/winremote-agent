using System.Drawing;
using System.Drawing.Drawing2D;

namespace Windowshulp.Agent;

/// <summary>
/// Het venster dat de klant ziet: huisstijl van het IT-bedrijf, de sessiecode en de status.
/// </summary>
public sealed class MainForm : Form
{
	private readonly Branding _merk = Branding.UitBestandsnaam();
	private readonly Signalering _sig = new();
	private readonly Startopdracht _start;
	private Sessie? _sessie;
	private readonly CancellationTokenSource _cts = new();

	private readonly Label _kop = new();
	private readonly Label _sub = new();
	private readonly Label _codeLabel = new();
	private readonly Label _code = new();
	private readonly Label _status = new();
	private readonly Button _kopieer = new();
	private readonly Button _stop = new();
	private readonly Panel _balk = new();
	private readonly Panel _stip = new();

	public MainForm(Startopdracht start)
	{
		_start = start;
		Text = $"{_merk.Naam} – Hulp op afstand";
		StartPosition = FormStartPosition.CenterScreen;
		FormBorderStyle = FormBorderStyle.FixedSingle;
		MaximizeBox = false;
		ClientSize = new Size(440, 340);
		BackColor = Color.White;
		Font = new Font("Segoe UI", 10f);
		TopMost = true;

		// Gekleurde kopbalk in de huisstijl
		_balk.Dock = DockStyle.Top;
		_balk.Height = 88;
		_balk.BackColor = _merk.Kleur;
		_balk.Paint += (_, e) =>
		{
			using var brush = new LinearGradientBrush(_balk.ClientRectangle, ControlPaint.Dark(_merk.Kleur, 0.15f), ControlPaint.Light(_merk.Kleur, 0.1f), 10f);
			e.Graphics.FillRectangle(brush, _balk.ClientRectangle);
		};

		_kop.Text = _merk.Naam;
		_kop.Font = new Font("Segoe UI", 18f, FontStyle.Bold);
		_kop.ForeColor = Color.White;
		_kop.BackColor = Color.Transparent;
		_kop.AutoSize = true;
		_kop.Location = new Point(24, 18);

		_sub.Text = _merk.Ondertitel;
		_sub.Font = new Font("Segoe UI", 9.5f);
		_sub.ForeColor = Color.FromArgb(235, 255, 255, 255);
		_sub.BackColor = Color.Transparent;
		_sub.AutoSize = true;
		_sub.Location = new Point(26, 54);
		_balk.Controls.Add(_kop);
		_balk.Controls.Add(_sub);

		_codeLabel.Text = "Uw sessiecode";
		_codeLabel.ForeColor = Color.FromArgb(95, 100, 112);
		_codeLabel.AutoSize = true;
		_codeLabel.Location = new Point(24, 112);

		_code.Text = "· · · · · ·";
		_code.Font = new Font("Segoe UI", 34f, FontStyle.Bold);
		_code.ForeColor = Color.FromArgb(12, 13, 17);
		_code.AutoSize = true;
		_code.Location = new Point(20, 134);

		_kopieer.Text = "Kopiëren";
		_kopieer.FlatStyle = FlatStyle.Flat;
		_kopieer.FlatAppearance.BorderColor = Color.FromArgb(228, 230, 235);
		_kopieer.Size = new Size(100, 34);
		_kopieer.Location = new Point(316, 150);
		_kopieer.Enabled = false;
		_kopieer.Click += (_, _) => { if (_sig.Code is not null) Clipboard.SetText(_sig.Code); };

		_stip.Size = new Size(10, 10);
		_stip.Location = new Point(26, 226);
		_stip.BackColor = Color.FromArgb(200, 200, 200);
		_stip.Paint += (_, e) =>
		{
			e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
			using var b = new SolidBrush(_stip.BackColor);
			e.Graphics.Clear(BackColor);
			e.Graphics.FillEllipse(b, 0, 0, 9, 9);
		};

		_status.Text = "Verbinden met de supportserver…";
		_status.ForeColor = Color.FromArgb(60, 64, 72);
		_status.AutoSize = false;
		_status.Size = new Size(380, 44);
		_status.Location = new Point(44, 220);

		_stop.Text = "Sessie beëindigen";
		_stop.FlatStyle = FlatStyle.Flat;
		_stop.FlatAppearance.BorderColor = _merk.Kleur;
		_stop.ForeColor = _merk.Kleur;
		_stop.Size = new Size(160, 38);
		_stop.Location = new Point(24, 282);
		_stop.Click += (_, _) => Close();

		var uitleg = new Label
		{
			Text = "Geef deze code door aan uw technicus.",
			ForeColor = Color.FromArgb(95, 100, 112),
			AutoSize = true,
			Location = new Point(200, 292)
		};

		Controls.AddRange([_balk, _codeLabel, _code, _kopieer, _stip, _status, _stop, uitleg]);

		Shown += async (_, _) => await StartAsync();
		FormClosing += async (_, e) =>
		{
			if (_sessie is null) return;
			e.Cancel = true;
			var s = _sessie; _sessie = null;
			await s.DisposeAsync();
			await _sig.DisposeAsync();
			Close();
		};
	}

	private async Task StartAsync()
	{
		try
		{
			string code;
			if (_start.Code is not null)
			{
				// De klant ziet deze code al op windowshulp.nl; hier alleen aansluiten.
				if (!await _sig.GebruikCodeAsync(_start.Code, _cts.Token))
				{
					_code.Text = "· · · · · ·";
					_status.Text = "Deze code is verlopen. Ga terug naar windowshulp.nl en klik opnieuw op Sessie starten.";
					_stip.BackColor = Color.FromArgb(200, 16, 46);
					_stip.Invalidate();
					return;
				}
				code = _start.Code;
			}
			else
			{
				code = await _sig.NieuweSessieAsync(_cts.Token);
			}
			_code.Text = $"{code[..3]} {code[3..]}";
			_kopieer.Enabled = true;

			_sessie = new Sessie(_sig);
			_sessie.Status += tekst => BeginInvoke(() => _status.Text = tekst);
			_sessie.TechnicusVerbonden += aan => BeginInvoke(() =>
			{
				_stip.BackColor = aan ? Color.FromArgb(34, 163, 92) : Color.FromArgb(200, 200, 200);
				_stip.Invalidate();
			});
			_sig.Verbroken += reden => BeginInvoke(() =>
			{
				_status.Text = $"Verbinding met de supportserver verbroken ({reden}).";
				_stip.BackColor = Color.FromArgb(200, 16, 46);
				_stip.Invalidate();
			});

			await _sig.VerbindAsync(_cts.Token);
			_status.Text = _start.Code is not null
				? "Klaar. Geef de code door aan uw technicus en wacht tot die verbindt…"
				: "Klaar. Wachten tot de technicus de code invoert…";
		}
		catch (Exception ex)
		{
			_status.Text = $"Kan geen verbinding maken: {ex.Message}";
			_stip.BackColor = Color.FromArgb(200, 16, 46);
			_stip.Invalidate();
		}
	}
}
