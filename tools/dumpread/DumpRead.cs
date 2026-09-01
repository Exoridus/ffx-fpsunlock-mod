// Minimal minidump reader: exception record, module list, unloaded module list,
// and a scan of the faulting thread's stack for return addresses inside loaded
// modules. Enough to decide whether a fault address belongs to a module that is
// no longer mapped.

using System.Buffers.Binary;
using System.Text;

string path = args.Length > 0 ? args[0] : throw new ArgumentException("dump path required");
byte[] d = File.ReadAllBytes(path);

uint U32(int o) => BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(o, 4));
ulong U64(int o) => BinaryPrimitives.ReadUInt64LittleEndian(d.AsSpan(o, 8));

string Str(uint rva)
{
    if (rva == 0 || rva + 4 > d.Length) return "";
    uint len = U32((int)rva);
    return Encoding.Unicode.GetString(d, (int)rva + 4, (int)len);
}

if (U32(0) != 0x504D444D) throw new InvalidDataException("not a minidump");

uint streams = U32(8);
uint dirRva = U32(12);

var dir = new Dictionary<uint, (uint Size, uint Rva)>();
for (int i = 0; i < streams; i++)
{
    int o = (int)dirRva + i * 12;
    dir[U32(o)] = (U32(o + 4), U32(o + 8));
}

Console.WriteLine($"streams: {string.Join(",", dir.Keys.OrderBy(k => k))}");

// SystemInfoStream = 7
if (dir.TryGetValue(7, out var si))
{
    ushort arch = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan((int)si.Rva, 2));
    Console.WriteLine($"arch: {arch} (0 = x86, 9 = x64)");
}

var modules = new List<(ulong Base, uint Size, string Name)>();
if (dir.TryGetValue(4, out var ml))
{
    uint n = U32((int)ml.Rva);
    for (int i = 0; i < n; i++)
    {
        int o = (int)ml.Rva + 4 + i * 108;
        modules.Add((U64(o), U32(o + 8), Str(U32(o + 20))));
    }
}
Console.WriteLine($"\nloaded modules: {modules.Count}");

var unloaded = new List<(ulong Base, uint Size, string Name)>();
if (dir.TryGetValue(14, out var um))
{
    uint sizeOfHeader = U32((int)um.Rva);
    uint sizeOfEntry = U32((int)um.Rva + 4);
    uint n = U32((int)um.Rva + 8);
    for (int i = 0; i < n; i++)
    {
        int o = (int)(um.Rva + sizeOfHeader + i * sizeOfEntry);
        unloaded.Add((U64(o), U32(o + 8), Str(U32(o + 20))));
    }
}
Console.WriteLine($"unloaded modules: {unloaded.Count}");
foreach (var u in unloaded)
    Console.WriteLine($"  UNLOADED {u.Base:X8}..{u.Base + u.Size:X8}  {u.Name}");

string Where(ulong a)
{
    foreach (var m in modules)
        if (a >= m.Base && a < m.Base + m.Size)
            return $"{Path.GetFileName(m.Name)}+0x{a - m.Base:X}";
    foreach (var u in unloaded)
        if (a >= u.Base && a < u.Base + u.Size)
            return $"*** UNLOADED {Path.GetFileName(u.Name)}+0x{a - u.Base:X}";
    return "<no module>";
}

uint faultThread = 0;
ulong eip = 0, esp = 0;

// ExceptionStream = 6
if (dir.TryGetValue(6, out var ex))
{
    int o = (int)ex.Rva;
    faultThread = U32(o);
    uint code = U32(o + 8);
    uint flags = U32(o + 12);
    ulong addr = U64(o + 24);
    uint nparam = U32(o + 32);

    Console.WriteLine($"\nEXCEPTION 0x{code:X8} flags 0x{flags:X} thread {faultThread}");
    Console.WriteLine($"  address {addr:X8}  -> {Where(addr)}");
    for (int i = 0; i < nparam && i < 15; i++)
        Console.WriteLine($"  param[{i}] = 0x{U64(o + 40 + i * 8):X}");

    uint ctxSize = U32(o + 160);
    uint ctxRva = U32(o + 164);
    if (ctxRva != 0 && ctxSize >= 0xCC)
    {
        eip = U32((int)ctxRva + 0xB8);
        esp = U32((int)ctxRva + 0xC4);
        Console.WriteLine($"  eip {eip:X8} -> {Where(eip)}");
        Console.WriteLine($"  esp {esp:X8}");
        Console.WriteLine($"  eax {U32((int)ctxRva + 0xB0):X8} ebx {U32((int)ctxRva + 0xA4):X8} " +
                          $"ecx {U32((int)ctxRva + 0xAC):X8} edx {U32((int)ctxRva + 0xA8):X8} " +
                          $"esi {U32((int)ctxRva + 0xA0):X8} edi {U32((int)ctxRva + 0x9C):X8} " +
                          $"ebp {U32((int)ctxRva + 0xB4):X8}");
    }
}

// Memory64ListStream = 9 or MemoryListStream = 5, to find the stack bytes.
var regions = new List<(ulong Start, ulong Size, long FileOffset)>();
if (dir.TryGetValue(9, out var m64))
{
    ulong n = U64((int)m64.Rva);
    ulong baseRva = U64((int)m64.Rva + 8);
    long off = (long)baseRva;
    for (ulong i = 0; i < n; i++)
    {
        int o = (int)m64.Rva + 16 + (int)i * 16;
        ulong start = U64(o), size = U64(o + 8);
        regions.Add((start, size, off));
        off += (long)size;
    }
}
else if (dir.TryGetValue(5, out var m32))
{
    uint n = U32((int)m32.Rva);
    for (int i = 0; i < n; i++)
    {
        int o = (int)m32.Rva + 4 + i * 16;
        ulong start = U64(o);
        uint size = U32(o + 8);
        uint rva = U32(o + 12);
        regions.Add((start, size, rva));
    }
}
Console.WriteLine($"\nmemory regions: {regions.Count}");

if (esp != 0)
{
    var stack = regions.FirstOrDefault(r => esp >= r.Start && esp < r.Start + r.Size);
    if (stack.Size == 0) { Console.WriteLine("stack region not in dump"); }
    else
    {
        Console.WriteLine($"\nstack scan from {esp:X8}, region {stack.Start:X8}..{stack.Start + stack.Size:X8}");
        ulong end = Math.Min(stack.Start + stack.Size, esp + 0x4000);
        int shown = 0;
        for (ulong a = esp; a + 4 <= end && shown < 120; a += 4)
        {
            long fo = stack.FileOffset + (long)(a - stack.Start);
            if (fo + 4 > d.Length) break;
            uint v = U32((int)fo);
            string w = Where(v);
            if (w != "<no module>")
            {
                Console.WriteLine($"  {a:X8}  {v:X8}  {w}");
                shown++;
            }
        }
    }
}

Console.WriteLine("\nmodule map:");
foreach (var m in modules.OrderBy(m => m.Base))
    Console.WriteLine($"  {m.Base:X8}..{m.Base + m.Size:X8}  {Path.GetFileName(m.Name)}");
