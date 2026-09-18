using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace Windowshulp.Agent;

/// <summary>
/// Zet invoerberichten van de technicus om in echte muis- en toetsenbordacties via SendInput.
/// Berichten (JSON over het datakanaal "invoer"):
///   { "t":"mm", "x":0.42, "y":0.17, "s":123 }  muis bewegen, genormaliseerd 0..1 over het scherm, s = volgnummer
///   { "t":"md"|"mu", "b":0|1|2, "x":…, "y":… } muisknop in/uit (links, midden, rechts), met de positie van de klik
///   { "t":"wh", "dx":0, "dy":-120 }            scrollen
///   { "t":"kd"|"ku", "code":"KeyA", "key":"a" } toets in/uit (DOM KeyboardEvent.code / .key)
/// </summary>
public static class Invoer
{
	public static void Verwerk(JsonNode bericht, System.Drawing.Rectangle scherm)
	{
		switch (bericht["t"]?.GetValue<string>())
		{
			case "mm":
				MuisNaar(bericht["x"]!.GetValue<double>(), bericht["y"]!.GetValue<double>(), scherm);
				break;
			case "md":
			case "mu":
				// Eerst naar de klikpositie, dan pas klikken: zo landt een klik altijd waar de technicus hem zag,
				// ook als de laatste beweging over het snelle kanaal verloren ging.
				if (bericht["x"] is JsonNode kx && bericht["y"] is JsonNode ky)
					MuisNaar(kx.GetValue<double>(), ky.GetValue<double>(), scherm);
				MuisKnop(bericht["b"]?.GetValue<int>() ?? 0, bericht["t"]!.GetValue<string>() == "md");
				break;
			case "wh":
				Scroll(bericht["dx"]?.GetValue<double>() ?? 0, bericht["dy"]?.GetValue<double>() ?? 0);
				break;
			case "kd":
				Toets(bericht["code"]?.GetValue<string>(), bericht["key"]?.GetValue<string>(), true);
				break;
			case "ku":
				Toets(bericht["code"]?.GetValue<string>(), bericht["key"]?.GetValue<string>(), false);
				break;
		}
	}

	private static void MuisNaar(double nx, double ny, System.Drawing.Rectangle scherm)
	{
		// Absolute coördinaten lopen van 0..65535 over het virtuele scherm.
		var vx = GetSystemMetrics(SM_XVIRTUALSCREEN);
		var vy = GetSystemMetrics(SM_YVIRTUALSCREEN);
		var vw = GetSystemMetrics(SM_CXVIRTUALSCREEN);
		var vh = GetSystemMetrics(SM_CYVIRTUALSCREEN);
		var px = scherm.X + nx * scherm.Width;
		var py = scherm.Y + ny * scherm.Height;
		var ax = (int)Math.Round((px - vx) * 65535.0 / vw);
		var ay = (int)Math.Round((py - vy) * 65535.0 / vh);
		Stuur(new INPUT
		{
			type = INPUT_MOUSE,
			u = new InputUnion { mi = new MOUSEINPUT { dx = ax, dy = ay, dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK } }
		});
	}

	private static void MuisKnop(int knop, bool omlaag)
	{
		uint vlag = (knop, omlaag) switch
		{
			(0, true) => MOUSEEVENTF_LEFTDOWN,
			(0, false) => MOUSEEVENTF_LEFTUP,
			(1, true) => MOUSEEVENTF_MIDDLEDOWN,
			(1, false) => MOUSEEVENTF_MIDDLEUP,
			(2, true) => MOUSEEVENTF_RIGHTDOWN,
			(2, false) => MOUSEEVENTF_RIGHTUP,
			_ => 0
		};
		if (vlag == 0) return;
		Stuur(new INPUT { type = INPUT_MOUSE, u = new InputUnion { mi = new MOUSEINPUT { dwFlags = vlag } } });
	}

	private static void Scroll(double dx, double dy)
	{
		// Browser geeft pixels; Windows verwacht veelvouden van 120 per "klik". Omgekeerd teken voor verticaal.
		if (dy != 0)
			Stuur(new INPUT { type = INPUT_MOUSE, u = new InputUnion { mi = new MOUSEINPUT { mouseData = (int)Math.Clamp(-dy, -600, 600), dwFlags = MOUSEEVENTF_WHEEL } } });
		if (dx != 0)
			Stuur(new INPUT { type = INPUT_MOUSE, u = new InputUnion { mi = new MOUSEINPUT { mouseData = (int)Math.Clamp(dx, -600, 600), dwFlags = MOUSEEVENTF_HWHEEL } } });
	}

	private static void Toets(string? code, string? key, bool omlaag)
	{
		if (code is not null && VirtueleToetsen.TryGetValue(code, out var vk))
		{
			var flags = omlaag ? 0u : KEYEVENTF_KEYUP;
			if (UitgebreideToetsen.Contains(vk)) flags |= KEYEVENTF_EXTENDEDKEY;
			Stuur(new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, wScan = (ushort)MapVirtualKey(vk, 0), dwFlags = flags } } });
			return;
		}

		// Geen bekende speciale toets: stuur het teken zelf als Unicode (werkt voor elke toetsenbordindeling).
		if (key is { Length: 1 })
		{
			Stuur(new INPUT
			{
				type = INPUT_KEYBOARD,
				u = new InputUnion { ki = new KEYBDINPUT { wScan = key[0], dwFlags = KEYEVENTF_UNICODE | (omlaag ? 0u : KEYEVENTF_KEYUP) } }
			});
		}
	}

	private static void Stuur(INPUT input)
	{
		var arr = new[] { input };
		SendInput(1, arr, Marshal.SizeOf<INPUT>());
	}

	/// <summary>DOM KeyboardEvent.code → Windows virtual-key. Letters en cijfers gaan als Unicode, dus die staan hier niet.</summary>
	private static readonly Dictionary<string, ushort> VirtueleToetsen = new()
	{
		["Enter"] = 0x0D, ["NumpadEnter"] = 0x0D, ["Backspace"] = 0x08, ["Tab"] = 0x09, ["Escape"] = 0x1B,
		["Space"] = 0x20, ["Delete"] = 0x2E, ["Insert"] = 0x2D, ["Home"] = 0x24, ["End"] = 0x23,
		["PageUp"] = 0x21, ["PageDown"] = 0x22, ["ArrowLeft"] = 0x25, ["ArrowUp"] = 0x26, ["ArrowRight"] = 0x27, ["ArrowDown"] = 0x28,
		["ShiftLeft"] = 0xA0, ["ShiftRight"] = 0xA1, ["ControlLeft"] = 0xA2, ["ControlRight"] = 0xA3,
		["AltLeft"] = 0xA4, ["AltRight"] = 0xA5, ["MetaLeft"] = 0x5B, ["MetaRight"] = 0x5C, ["ContextMenu"] = 0x5D,
		["CapsLock"] = 0x14, ["NumLock"] = 0x90, ["ScrollLock"] = 0x91, ["PrintScreen"] = 0x2C, ["Pause"] = 0x13,
		["F1"] = 0x70, ["F2"] = 0x71, ["F3"] = 0x72, ["F4"] = 0x73, ["F5"] = 0x74, ["F6"] = 0x75,
		["F7"] = 0x76, ["F8"] = 0x77, ["F9"] = 0x78, ["F10"] = 0x79, ["F11"] = 0x7A, ["F12"] = 0x7B,
		// Letters via virtual-key zodat Ctrl+C/Ctrl+V e.d. werken (Unicode-injectie negeert modifiers).
		["KeyA"] = 0x41, ["KeyB"] = 0x42, ["KeyC"] = 0x43, ["KeyD"] = 0x44, ["KeyE"] = 0x45, ["KeyF"] = 0x46, ["KeyG"] = 0x47,
		["KeyH"] = 0x48, ["KeyI"] = 0x49, ["KeyJ"] = 0x4A, ["KeyK"] = 0x4B, ["KeyL"] = 0x4C, ["KeyM"] = 0x4D, ["KeyN"] = 0x4E,
		["KeyO"] = 0x4F, ["KeyP"] = 0x50, ["KeyQ"] = 0x51, ["KeyR"] = 0x52, ["KeyS"] = 0x53, ["KeyT"] = 0x54, ["KeyU"] = 0x55,
		["KeyV"] = 0x56, ["KeyW"] = 0x57, ["KeyX"] = 0x58, ["KeyY"] = 0x59, ["KeyZ"] = 0x5A,
		["Digit0"] = 0x30, ["Digit1"] = 0x31, ["Digit2"] = 0x32, ["Digit3"] = 0x33, ["Digit4"] = 0x34,
		["Digit5"] = 0x35, ["Digit6"] = 0x36, ["Digit7"] = 0x37, ["Digit8"] = 0x38, ["Digit9"] = 0x39
	};

	private static readonly HashSet<ushort> UitgebreideToetsen = [0x2E, 0x2D, 0x24, 0x23, 0x21, 0x22, 0x25, 0x26, 0x27, 0x28, 0xA3, 0xA5, 0x5B, 0x5C, 0x90, 0x2C];

	#region Win32
	private const int INPUT_MOUSE = 0, INPUT_KEYBOARD = 1;
	private const uint MOUSEEVENTF_MOVE = 0x0001, MOUSEEVENTF_LEFTDOWN = 0x0002, MOUSEEVENTF_LEFTUP = 0x0004,
		MOUSEEVENTF_RIGHTDOWN = 0x0008, MOUSEEVENTF_RIGHTUP = 0x0010, MOUSEEVENTF_MIDDLEDOWN = 0x0020, MOUSEEVENTF_MIDDLEUP = 0x0040,
		MOUSEEVENTF_WHEEL = 0x0800, MOUSEEVENTF_HWHEEL = 0x1000, MOUSEEVENTF_VIRTUALDESK = 0x4000, MOUSEEVENTF_ABSOLUTE = 0x8000;
	private const uint KEYEVENTF_EXTENDEDKEY = 0x0001, KEYEVENTF_KEYUP = 0x0002, KEYEVENTF_UNICODE = 0x0004;
	private const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77, SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;

	[StructLayout(LayoutKind.Sequential)]
	private struct INPUT { public int type; public InputUnion u; }

	[StructLayout(LayoutKind.Explicit)]
	private struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }

	[StructLayout(LayoutKind.Sequential)]
	private struct MOUSEINPUT { public int dx, dy, mouseData; public uint dwFlags, time; public IntPtr dwExtraInfo; }

	[StructLayout(LayoutKind.Sequential)]
	private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

	[DllImport("user32.dll", SetLastError = true)]
	private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

	[DllImport("user32.dll")]
	private static extern uint MapVirtualKey(uint uCode, uint uMapType);

	[DllImport("user32.dll")]
	private static extern int GetSystemMetrics(int nIndex);
	#endregion
}
