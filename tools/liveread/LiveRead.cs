// Reads engine globals out of the running game. Attaches to FFX.exe, resolves
// Ghidra virtual addresses against the actual image base, and prints or polls
// values while somebody plays.
//
// The mod's telemetry answers questions that were asked before the build; this
// answers questions that come up while the game is running, without a rebuild
// and without a deploy.

using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

const int PROCESS_VM_READ = 0x0010;
const int PROCESS_QUERY_INFORMATION = 0x0400;

[DllImport("kernel32.dll", SetLastError = true)]
static extern nint OpenProcess(int access, bool inherit, int pid);

[DllImport("kernel32.dll", SetLastError = true)]
static extern bool ReadProcessMemory(nint process, nint address, byte[] buffer, int size, out nint read);

[DllImport("kernel32.dll")]
static extern bool CloseHandle(nint handle);

if (args.Length == 0)
{
    Console.WriteLine("""
        usage: dotnet run LiveRead.cs [--watch <seconds>] <spec> [<spec> ...]

          <spec>  name=VA[:type]  or  VA[:type]
                  VA is the Ghidra virtual address (0x00c49720 or c49720).
                  type is u8, i8, u16, i16, u32, i32, f32, f64, ptr, or NxTYPE
                  for an array (8xu32). Default u32.

          --watch <seconds>  reprint every interval until Ctrl+C, marking values
                             that changed since the previous line.

        Addresses are Ghidra VAs and are rebased onto the live image, which the
        loader does not put at 0x00400000.

        examples:
          dotnet run LiveRead.cs blur=0x0112ca64 need_sync=0x012fb858
          dotnet run LiveRead.cs --watch 1 vcount=0x012fb7a8 rate=0x023cbbec:i32
          dotnet run LiveRead.cs managers=0x00d4b4b8 step=0x00d4b7dc
        """);
    return 0;
}

double watch = 0;
var specs = new List<(string Name, ulong Va, string Type, int Count)>();

for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--watch") { watch = double.Parse(args[++i], CultureInfo.InvariantCulture); continue; }

    string arg = args[i];
    string name = arg.Contains('=') ? arg[..arg.IndexOf('=')] : arg;
    string rest = arg.Contains('=') ? arg[(arg.IndexOf('=') + 1)..] : arg;

    string addr = rest.Contains(':') ? rest[..rest.IndexOf(':')] : rest;
    string type = rest.Contains(':') ? rest[(rest.IndexOf(':') + 1)..] : "u32";

    int count = 1;
    if (type.Contains('x')) { count = int.Parse(type[..type.IndexOf('x')]); type = type[(type.IndexOf('x') + 1)..]; }

    ulong va = ulong.Parse(addr.Replace("0x", "", StringComparison.OrdinalIgnoreCase), NumberStyles.HexNumber);
    specs.Add((name, va, type, count));
}

var game = Process.GetProcessesByName("FFX").FirstOrDefault()
        ?? Process.GetProcessesByName("FFX_JP").FirstOrDefault();

if (game?.MainModule is null)
{
    Console.Error.WriteLine("FFX.exe is not running.");
    return 1;
}

nint image = game.MainModule.BaseAddress;
int size = game.MainModule.ModuleMemorySize;

// The Ghidra addresses are VAs against the PE's preferred base. The loader
// relocates, so every address has to move by the same delta.
const ulong GhidraImageBase = 0x00400000;
long slide = image - (nint)GhidraImageBase;

Console.WriteLine($"FFX.exe pid {game.Id}, image 0x{image:X}..0x{image + size:X}, slide 0x{slide:X}");

nint handle = OpenProcess(PROCESS_VM_READ | PROCESS_QUERY_INFORMATION, false, game.Id);
if (handle == 0)
{
    Console.Error.WriteLine($"OpenProcess failed: {Marshal.GetLastWin32Error()}");
    return 1;
}

int Width(string type) => type switch
{
    "u8" or "i8" => 1,
    "u16" or "i16" => 2,
    "f64" => 8,
    _ => 4
};

string Format(byte[] b, int off, string type) => type switch
{
    "u8" => $"{b[off]:X2}",
    "i8" => $"{(sbyte)b[off]}",
    "u16" => $"{BitConverter.ToUInt16(b, off):X4}",
    "i16" => $"{BitConverter.ToInt16(b, off)}",
    "i32" => $"{BitConverter.ToInt32(b, off)}",
    "f32" => BitConverter.ToSingle(b, off).ToString("F4", CultureInfo.InvariantCulture),
    "f64" => BitConverter.ToDouble(b, off).ToString("F4", CultureInfo.InvariantCulture),
    "ptr" => $"0x{BitConverter.ToUInt32(b, off):X8}",
    _ => $"0x{BitConverter.ToUInt32(b, off):X8}"
};

var previous = new Dictionary<string, string>();

do
{
    var parts = new List<string>();

    foreach (var (name, va, type, count) in specs)
    {
        int width = Width(type);
        byte[] buffer = new byte[width * count];
        nint live = (nint)((long)va + slide);

        if (!ReadProcessMemory(handle, live, buffer, buffer.Length, out nint read) || read != buffer.Length)
        {
            parts.Add($"{name}=<unreadable>");
            continue;
        }

        string value = count == 1
            ? Format(buffer, 0, type)
            : "[" + string.Join(' ', Enumerable.Range(0, count).Select(i => Format(buffer, i * width, type))) + "]";

        // A value that moved is the whole point of watching, so it is marked
        // rather than left for the reader to diff by eye.
        string mark = previous.TryGetValue(name, out string? old) && old != value ? "*" : " ";
        previous[name] = value;

        parts.Add($"{name}{mark}={value}");
    }

    Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff}  " + string.Join("  ", parts));

    if (watch > 0) Thread.Sleep(TimeSpan.FromSeconds(watch));
}
while (watch > 0 && !game.HasExited);

CloseHandle(handle);
return 0;
