using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Windowshulp.Agent;

/// <summary>
/// Maakt opnames van het primaire scherm (GDI, eenvoudig en overal werkend) inclusief muiscursor,
/// en schaalt ze naar een maximale breedte voor de encoder. Levert 32-bit BGRA-pixels.
/// Een snellere DXGI-variant kan later achter dezelfde interface.
/// </summary>
public sealed class SchermOpname : IDisposable
{
	private Bitmap? _vol;
	private Bitmap? _klein;
	private Graphics? _gVol;
	private Graphics? _gKlein;

	public Rectangle Scherm { get; private set; }
	public int Breedte { get; private set; }
	public int Hoogte { get; private set; }

	public SchermOpname(int maxBreedte = 1600)
	{
		Scherm = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1280, 720);
		var schaal = Math.Min(1.0, (double)maxBreedte / Scherm.Width);
		// Even afmetingen: dat vinden videocodecs prettig.
		Breedte = (int)(Scherm.Width * schaal) & ~1;
		Hoogte = (int)(Scherm.Height * schaal) & ~1;
	}

	/// <summary>Neemt één frame op en geeft de BGRA-bytes (Breedte × Hoogte × 4) terug.</summary>
	public byte[] Frame()
	{
		_vol ??= new Bitmap(Scherm.Width, Scherm.Height, PixelFormat.Format32bppArgb);
		_gVol ??= Graphics.FromImage(_vol);
		_gVol.CopyFromScreen(Scherm.Location, Point.Empty, Scherm.Size, CopyPixelOperation.SourceCopy);
		TekenCursor(_gVol);

		Bitmap bron = _vol;
		if (Breedte != Scherm.Width)
		{
			_klein ??= new Bitmap(Breedte, Hoogte, PixelFormat.Format32bppArgb);
			_gKlein ??= MaakSchaler(_klein);
			_gKlein.DrawImage(_vol, new Rectangle(0, 0, Breedte, Hoogte), new Rectangle(0, 0, Scherm.Width, Scherm.Height), GraphicsUnit.Pixel);
			bron = _klein;
		}

		var data = bron.LockBits(new Rectangle(0, 0, Breedte, Hoogte), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
		try
		{
			var bytes = new byte[Breedte * Hoogte * 4];
			var rij = Breedte * 4;
			for (var y = 0; y < Hoogte; y++)
				Marshal.Copy(data.Scan0 + y * data.Stride, bytes, y * rij, rij);
			return bytes;
		}
		finally
		{
			bron.UnlockBits(data);
		}
	}

	private static Graphics MaakSchaler(Bitmap doel)
	{
		var g = Graphics.FromImage(doel);
		g.InterpolationMode = InterpolationMode.Bilinear;
		g.PixelOffsetMode = PixelOffsetMode.Half;
		g.CompositingMode = CompositingMode.SourceCopy;
		return g;
	}

	private void TekenCursor(Graphics g)
	{
		var info = new CURSORINFO { cbSize = Marshal.SizeOf<CURSORINFO>() };
		if (!GetCursorInfo(ref info) || info.flags != CURSOR_SHOWING || info.hCursor == IntPtr.Zero) return;
		var hotspotX = 0; var hotspotY = 0;
		if (GetIconInfo(info.hCursor, out var icon))
		{
			hotspotX = icon.xHotspot; hotspotY = icon.yHotspot;
			if (icon.hbmMask != IntPtr.Zero) DeleteObject(icon.hbmMask);
			if (icon.hbmColor != IntPtr.Zero) DeleteObject(icon.hbmColor);
		}
		var x = info.ptScreenPos.X - Scherm.X - hotspotX;
		var y = info.ptScreenPos.Y - Scherm.Y - hotspotY;
		var hdc = g.GetHdc();
		try { DrawIconEx(hdc, x, y, info.hCursor, 0, 0, 0, IntPtr.Zero, DI_NORMAL); }
		finally { g.ReleaseHdc(hdc); }
	}

	public void Dispose()
	{
		_gKlein?.Dispose(); _gVol?.Dispose(); _klein?.Dispose(); _vol?.Dispose();
	}

	#region Win32
	private const int CURSOR_SHOWING = 1;
	private const uint DI_NORMAL = 3;

	[StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
	[StructLayout(LayoutKind.Sequential)] private struct CURSORINFO { public int cbSize, flags; public IntPtr hCursor; public POINT ptScreenPos; }
	[StructLayout(LayoutKind.Sequential)] private struct ICONINFO { public bool fIcon; public int xHotspot, yHotspot; public IntPtr hbmMask, hbmColor; }

	[DllImport("user32.dll")] private static extern bool GetCursorInfo(ref CURSORINFO pci);
	[DllImport("user32.dll")] private static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO piconinfo);
	[DllImport("user32.dll")] private static extern bool DrawIconEx(IntPtr hdc, int x, int y, IntPtr hIcon, int cx, int cy, uint step, IntPtr brush, uint flags);
	[DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr hObject);
	#endregion
}
