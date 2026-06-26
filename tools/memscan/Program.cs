using System.Diagnostics;
using System.Runtime.InteropServices;

// memscan <processName> — scans a running process's memory for the item-icon table.
// Signature: the proven A–Z run maps IconIDs 4113..4138 -> cells 145..170. We look for the
// subsequence 145,146,...,170 (the cells) and 4113,4114,...,4138 (the IconIDs) at various strides,
// because the in-memory per-IconID table (or segment table) must contain one of these runs.

const int A_LO = 4113, A_HI = 4138;            // IconIDs of A..Z
const int C_LO = 145,  C_HI = 170;             // cells of A..Z
const int RUN = A_HI - A_LO + 1;               // 26

Process proc;
if (args.Length > 0 && int.TryParse(args[0], out int pidArg))
{
    try { proc = Process.GetProcessById(pidArg); }
    catch { Console.WriteLine($"pid {pidArg} not found"); return 1; }
}
else
{
    string procName = args.Length > 0 ? args[0] : "AIKA";
    var procs = Process.GetProcessesByName(procName);
    if (procs.Length == 0) { Console.WriteLine($"process '{procName}' not found"); return 1; }
    proc = procs[0];
}
Console.WriteLine($"target: {proc.ProcessName} pid={proc.Id}");

IntPtr h = IntPtr.Zero;
foreach (uint mask in new uint[] { 0x0010u | 0x0400u, 0x0010u | 0x1000u, 0x0010u, 0x1FFFFFu })
{
    h = OpenProcess(mask, false, proc.Id);
    if (h != IntPtr.Zero) { Console.WriteLine($"OpenProcess ok mask=0x{mask:X}"); break; }
    Console.WriteLine($"OpenProcess mask=0x{mask:X} failed err={Marshal.GetLastWin32Error()}");
}
if (h == IntPtr.Zero) { Console.WriteLine("all OpenProcess attempts failed (likely integrity/elevation)"); return 1; }

// raw mode: memscan <pid> raw <startHex> <lenHex> <outfile>
if (args.Length >= 5 && args[1] == "raw")
{
    ulong start = Convert.ToUInt64(args[2], 16);
    int rlen = (int)Convert.ToUInt64(args[3], 16);
    var rbuf = new byte[rlen];
    if (ReadProcessMemory(h, (IntPtr)start, rbuf, rlen, out int got))
    { File.WriteAllBytes(args[4], rbuf); Console.WriteLine($"raw dump 0x{start:X} +{got} -> {args[4]}"); }
    else Console.WriteLine($"raw read failed err={Marshal.GetLastWin32Error()}");
    CloseHandle(h); return 0;
}

int[] strides = { 24 };          // record size proven = 24 bytes
long regions = 0, scanned = 0, hitsCells = 0, hitsIds = 0;
ulong cellsAnchor = 0;           // addr of the {cell=145, atlas=8, ...} record (slot of 'A')
ulong idsAnchor = 0;             // addr of the {iconID=4113, ...} record (same slot of 'A')
bool findTable = args.Contains("findtable");
bool findFloat = args.Contains("findfloat");
bool findA = args.Contains("findA");
bool findRect = args.Contains("findrect");
bool find3 = args.Contains("find3");
var tableHits = new List<(ulong p, int S, int D)>();
string dumpPath = (args.Length > 1 && args[1] != "findtable" && args[1] != "find3" && args[1] != "raw") ? args[1] : null;

ulong addr = 0x10000;
const ulong MAX = 0x7FFF0000;
while (addr < MAX)
{
    if (VirtualQueryEx(h, (IntPtr)addr, out MEMORY_BASIC_INFORMATION mbi, (uint)Marshal.SizeOf<MEMORY_BASIC_INFORMATION>()) == 0) break;
    ulong baseA = (ulong)mbi.BaseAddress.ToInt64();
    ulong rsize = (ulong)mbi.RegionSize.ToInt64();
    if (rsize == 0) break;
    bool committed = mbi.State == 0x1000;            // MEM_COMMIT
    uint prot = mbi.Protect & 0xFF;
    bool readable = prot == 0x02 || prot == 0x04 || prot == 0x20 || prot == 0x40 || prot == 0x08 || prot == 0x80;
    bool guard = (mbi.Protect & 0x100) != 0 || (mbi.Protect & 0x01) != 0; // GUARD or NOACCESS
    if (committed && readable && !guard && rsize <= 256UL * 1024 * 1024)
    {
        regions++;
        var buf = new byte[rsize];
        if (ReadProcessMemory(h, (IntPtr)baseA, buf, (int)rsize, out int read) && read > 0)
        {
            scanned += read;
            ScanBuffer(baseA, buf, read);
        }
    }
    addr = baseA + rsize;
}
Console.WriteLine($"\nregions scanned={regions} bytes={scanned/1024/1024}MB  cellHits={hitsCells} idHits={hitsIds}  tableHits={tableHits.Count}");

// findtable: for each candidate (p=addr of record[4113].cellField, S=stride, D=atlas offset),
// dump record[iconID] = {cell=u16@(p+(i-4113)*S), atlas=u16@(+D)} for all iconIDs. Validate & write.
if (findTable)
{
    int best = -1, bestCount = 0; string bestJson = null;
    for (int hi = 0; hi < tableHits.Count; hi++)
    {
        var (p, S, D) = tableHits[hi];
        var rec = new byte[Math.Max(4, D + 2)];
        var found = new Dictionary<int, (int atlas, int cell)>();
        int sane = 0;
        for (int icon = 1; icon <= 8191; icon++)
        {
            ulong ca = (ulong)((long)p + (long)(icon - 4113) * S);
            if (!ReadProcessMemory(h, (IntPtr)ca, rec, rec.Length, out int rd) || rd < rec.Length) continue;
            int cell = rec[0] | (rec[1] << 8);
            int atlas = rec[D] | (rec[D + 1] << 8);
            if (atlas < 1 || atlas > 11) continue;
            if (cell < 0 || cell > 4095) continue;
            if (atlas != 10 && cell > 1023) continue;
            found[icon] = (atlas, cell); sane++;
        }
        Console.WriteLine($"candidate#{hi} @0x{p:X} S={S} D={D}: {sane} valid records");
        if (sane > bestCount)
        {
            bestCount = sane; best = hi;
            var sb = new System.Text.StringBuilder("{"); bool f = true;
            foreach (var kv in found.OrderBy(k => k.Key)) { if (!f) sb.Append(','); f = false; sb.Append($"\"{kv.Key}\":[{kv.Value.atlas},{kv.Value.cell}]"); }
            sb.Append('}'); bestJson = sb.ToString();
        }
    }
    if (bestJson != null)
    {
        string outp = "C:/Users/user/AppData/Local/Temp/claude/icons/iconmap.generated.json";
        File.WriteAllText(outp, bestJson);
        Console.WriteLine($"BEST candidate#{best} -> {bestCount} entries written to {outp}");
    }
}

// --- dump the IconID -> {atlas, cell} table from memory ---
// The descriptor array is indexed directly by IconID: descriptor[iconID] = {u16 cell@0, u16 atlas@2, ...},
// stride 24. base = cellsAnchor - 4113*24 (cellsAnchor = record for IconID 4113 = A = cell145/atlas8).
if (cellsAnchor != 0 && dumpPath != null)
{
    ulong baseAddr = (ulong)((long)cellsAnchor - (long)4113 * 24);
    Console.WriteLine($"\ncellsAnchor=0x{cellsAnchor:X}  base(record0)=0x{baseAddr:X}; reading descriptor[iconID]...");
    var rec = new byte[24];
    var found = new Dictionary<int, (int atlas, int cell)>();
    for (int icon = 1; icon <= 8191; icon++)
    {
        ulong a = baseAddr + (ulong)icon * 24;
        if (!ReadProcessMemory(h, (IntPtr)a, rec, 4, out int rd) || rd < 4) continue;
        int cell = rec[0] | (rec[1] << 8);
        int atlas = rec[2] | (rec[3] << 8);
        if (atlas < 1 || atlas > 11) continue;
        if (cell < 0 || cell > 4095) continue;
        if (atlas != 10 && cell > 1023) continue;
        found[icon] = (atlas, cell);
    }
    var sb = new System.Text.StringBuilder("{"); bool first = true;
    foreach (var kv in found.OrderBy(k => k.Key))
    { if (!first) sb.Append(','); first = false; sb.Append($"\"{kv.Key}\":[{kv.Value.atlas},{kv.Value.cell}]"); }
    sb.Append('}');
    File.WriteAllText(dumpPath, sb.ToString());
    Console.WriteLine($"wrote {found.Count} entries -> {dumpPath}");
}
else if (dumpPath != null) Console.WriteLine($"cells anchor not found — cannot dump");
CloseHandle(h);
return 0;

// findtable mode: a stride-S array where one column runs cell 145..170 AND a parallel column is constant atlas 8.
void ScanTable(ulong baseA, byte[] b, int len)
{
    int[] SS = { 4, 6, 8, 10, 12, 16, 20, 24, 28, 32, 40, 48, 64 };
    foreach (int S in SS)
    {
        int span = 25 * S + 2;
        for (int o = 0; o + span + S <= len; o++)
        {
            if (U16(b, o) != 145) continue;
            bool run = true;
            for (int k = 0; k < 26; k++) if (U16(b, o + k * S) != 145 + k) { run = false; break; }
            if (!run) continue;
            // look for a column D (offset within record) that is constant 8 across all 26 records
            for (int D = 2; D <= S - 2; D += 2)
            {
                bool c8 = true;
                for (int k = 0; k < 26; k++) { int idx = o + k * S + D; if (idx + 2 > len || U16(b, idx) != 8) { c8 = false; break; } }
                if (c8)
                {
                    ulong va = baseA + (ulong)o;
                    tableHits.Add((va, S, D));
                    Console.WriteLine($"\n@@@ ICON TABLE candidate @0x{va:X} stride={S} atlasOffset={D} @@@");
                    int f = Math.Max(0, o - S), t = Math.Min(len, o + 4 * S);
                    for (int p = f; p < t; p += 16)
                    { var sb = new System.Text.StringBuilder($"0x{baseA + (ulong)p:X8}  "); for (int i = 0; i < 16 && p + i < t; i++) sb.Append(b[p + i].ToString("x2") + " "); Console.WriteLine(sb.ToString()); }
                }
            }
        }
    }
}

// find3 mode: locate windows where iconID 4113 co-occurs with cell 145 AND atlas 8 (the real record for 'A')
void ScanTriple(ulong baseA, byte[] b, int len)
{
    const int W = 64;
    for (int o = 0; o + 2 <= len; o++)
    {
        if (U16(b, o) != 4113) continue;
        int lo = Math.Max(0, o - W), hi = Math.Min(len - 2, o + W);
        bool has145 = false, has8 = false, has170 = false;
        for (int p = lo; p <= hi; p += 2) { int v = U16(b, p); if (v == 145) has145 = true; if (v == 8) has8 = true; if (v == 170) has170 = true; }
        if (has145 && has8)
        {
            ulong va = baseA + (ulong)o;
            Console.WriteLine($"\n### 4113 + 145 + 8 (has170={has170}) @0x{va:X} ###");
            int f = Math.Max(0, o - 32), t = Math.Min(len, o + 80);
            for (int p = f; p < t; p += 16)
            {
                var sb = new System.Text.StringBuilder($"0x{baseA + (ulong)p:X8}  ");
                for (int i = 0; i < 16 && p + i < t; i++) sb.Append(b[p + i].ToString("x2") + " ");
                Console.WriteLine(sb.ToString());
            }
        }
    }
}

// findfloat: runs of float32 where consecutive values increase by ~1/32 (UV step for adjacent atlas cells)
int floatHits = 0;
void ScanFloat(ulong baseA, byte[] b, int len)
{
    for (int o = 0; o + 4 * 10 <= len; o += 4)
    {
        int run = 1; float prev = BitConverter.ToSingle(b, o);
        if (prev < 0f || prev > 1.0001f) continue;
        for (int k = 1; k < 40 && o + (k + 1) * 4 <= len; k++)
        {
            float v = BitConverter.ToSingle(b, o + k * 4);
            if (Math.Abs(v - (prev + 1f / 32f)) < 1e-4f) { run++; prev = v; } else break;
        }
        if (run >= 8)
        {
            floatHits++;
            if (floatHits <= 20)
            {
                ulong va = baseA + (ulong)o;
                var vals = new System.Text.StringBuilder();
                for (int k = 0; k < run + 1 && o + k * 4 + 4 <= len; k++) vals.Append(BitConverter.ToSingle(b, o + k * 4).ToString("0.#####") + ",");
                Console.WriteLine($"\n~~~ FLOAT u-run @0x{va:X} len={run}: {vals}");
            }
            o += run * 4;
        }
    }
}

// findA: locate IconID 4113 (letter A) icon descriptor by its UV. atlas8 cell145 = row4,col17.
// u0 candidates: 17/32=0.53125 or (17*32+1)/1024=0.5322266 ; v0: 4/32=0.125 or 129/1024=0.1259766
int aHits = 0;
void ScanA(ulong baseA, byte[] b, int len)
{
    float[] us = { 0.53125f, 0.5322266f, 0.531738f };
    float[] vs = { 0.125f, 0.1259766f, 0.125488f };
    for (int o = 0; o + 4 <= len; o += 4)
    {
        float f = BitConverter.ToSingle(b, o);
        bool isU = false; foreach (var u in us) if (Math.Abs(f - u) < 6e-4f) isU = true;
        if (!isU) continue;
        int lo = Math.Max(0, o - 48), hi = Math.Min(len - 4, o + 48);
        bool hasV = false;
        for (int p = lo; p <= hi; p += 4) { float g = BitConverter.ToSingle(b, p); foreach (var v in vs) if (Math.Abs(g - v) < 6e-4f) hasV = true; }
        if (hasV && aHits < 30)
        {
            aHits++;
            ulong va = baseA + (ulong)o;
            Console.WriteLine($"\n=== A-UV @0x{va:X} (u={f:0.######}) ===");
            int ff = Math.Max(0, o - 32), tt = Math.Min(len, o + 48);
            var fl = new System.Text.StringBuilder("  floats: ");
            for (int p = ff; p + 4 <= tt; p += 4) fl.Append(BitConverter.ToSingle(b, p).ToString("0.####") + ",");
            Console.WriteLine(fl.ToString());
            var hx = new System.Text.StringBuilder("  hex: ");
            for (int p = ff; p < tt; p++) hx.Append(b[p].ToString("x2") + (p % 4 == 3 ? "|" : " "));
            Console.WriteLine(hx.ToString());
        }
    }
}

// findrect: A's UV rect = a uniform 32px cell (atlas8 row4 col17). Among 4 consecutive floats find
// {u0,u1} with u1-u0≈0.03125 in [0.50,0.58] AND {v0,v1} with v1-v0≈0.03125 in [0.11,0.17].
int rectHits = 0;
void ScanRect(ulong baseA, byte[] b, int len)
{
    const float step = 1f / 32f, tol = 8e-4f;
    bool near(float x, float t) => Math.Abs(x - t) < tol;
    bool pair(float a, float c, float lo, float hi) => Math.Abs((c - a) - step) < tol && a > lo && a < hi;
    for (int o = 0; o + 16 <= len; o += 4)
    {
        float f0 = BitConverter.ToSingle(b, o), f1 = BitConverter.ToSingle(b, o + 4), f2 = BitConverter.ToSingle(b, o + 8), f3 = BitConverter.ToSingle(b, o + 12);
        // try orders (u0,v0,u1,v1) and (u0,u1,v0,v1)
        bool m1 = pair(f0, f2, 0.50f, 0.58f) && pair(f1, f3, 0.11f, 0.17f);
        bool m2 = pair(f0, f1, 0.50f, 0.58f) && pair(f2, f3, 0.11f, 0.17f);
        if (m1 || m2)
        {
            rectHits++;
            if (rectHits <= 40)
            {
                ulong va = baseA + (ulong)o;
                var fl = new System.Text.StringBuilder();
                int ff = Math.Max(0, o - 24), tt = Math.Min(len, o + 40);
                for (int p = ff; p + 4 <= tt; p += 4) fl.Append(BitConverter.ToSingle(b, p).ToString("0.####") + ",");
                Console.WriteLine($"\n+++ RECT @0x{va:X} order={(m1 ? "u0v0u1v1" : "u0u1v0v1")} : {fl}");
            }
        }
    }
}

void ScanBuffer(ulong baseA, byte[] b, int len)
{
    if (findRect) { ScanRect(baseA, b, len); return; }
    if (findA) { ScanA(baseA, b, len); return; }
    if (findFloat) { ScanFloat(baseA, b, len); return; }
    if (findTable) { ScanTable(baseA, b, len); return; }
    if (find3) { ScanTriple(baseA, b, len); return; }
    foreach (int st in strides)
    {
        // need RUN samples at stride st: span = (RUN-1)*st + 2
        int span = (RUN - 1) * st + 2;
        for (int o = 0; o + span <= len; o++)
        {
            // cells run 145..170 ?
            if (U16(b, o) == C_LO && U16(b, o + st) == C_LO + 1)
            {
                bool ok = true;
                for (int k = 0; k < RUN; k++) if (U16(b, o + k * st) != C_LO + k) { ok = false; break; }
                if (ok)
                {
                    hitsCells++; Report("CELLS 145..170", baseA, o, st, b, len);
                    // the {cell,atlas} record for slot 'A' has atlas (u16@+2) == 8
                    if (st == 24 && cellsAnchor == 0 && o + 4 <= len && U16(b, o + 2) == 8)
                        cellsAnchor = baseA + (ulong)o;
                }
            }
            // ids run 4113..4138 ?
            if (U16(b, o) == A_LO && U16(b, o + st) == A_LO + 1)
            {
                bool ok = true;
                for (int k = 0; k < RUN; k++) if (U16(b, o + k * st) != A_LO + k) { ok = false; break; }
                if (ok) { hitsIds++; Report("IDS 4113..4138", baseA, o, st, b, len); if (st == 24 && idsAnchor == 0) idsAnchor = baseA + (ulong)o; }
            }
        }
    }
}

void Report(string what, ulong baseA, int o, int st, byte[] b, int len)
{
    ulong va = baseA + (ulong)o;
    Console.WriteLine($"\n*** {what} @0x{va:X} stride={st} ***");
    // dump 48 bytes before and 96 after as hex, to expose record layout (atlas field etc.)
    int from = Math.Max(0, o - 48), to = Math.Min(len, o + 96);
    for (int p = from; p < to; p += 16)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($"0x{baseA + (ulong)p:X8}  ");
        for (int i = 0; i < 16 && p + i < to; i++) sb.Append(b[p + i].ToString("x2") + " ");
        Console.WriteLine(sb.ToString());
    }
    // also print as u16 array around the run
    var u = new System.Text.StringBuilder("  u16: ");
    for (int k = -2; k < RUN + 4; k++) { int idx = o + k * st; if (idx >= 0 && idx + 2 <= len) u.Append(U16(b, idx) + ","); }
    Console.WriteLine(u.ToString());
}

static ushort U16(byte[] b, int o) => (ushort)(b[o] | (b[o + 1] << 8));

[DllImport("kernel32.dll", SetLastError = true)]
static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
[DllImport("kernel32.dll", SetLastError = true)]
static extern bool CloseHandle(IntPtr h);
[DllImport("kernel32.dll", SetLastError = true)]
static extern bool ReadProcessMemory(IntPtr h, IntPtr addr, byte[] buf, int size, out int read);
[DllImport("kernel32.dll", SetLastError = true)]
static extern int VirtualQueryEx(IntPtr h, IntPtr addr, out MEMORY_BASIC_INFORMATION mbi, uint len);

[StructLayout(LayoutKind.Sequential)]
struct MEMORY_BASIC_INFORMATION
{
    public IntPtr BaseAddress;
    public IntPtr AllocationBase;
    public uint AllocationProtect;
    public IntPtr RegionSize;
    public uint State;
    public uint Protect;
    public uint Type;
}
