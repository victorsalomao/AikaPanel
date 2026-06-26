using System.IO.Compression;

// Usage: jit2png <in.jit> <out.png> [maxSize]
string inp = args[0], outp = args[1];
int maxSize = args.Length > 2 ? int.Parse(args[2]) : 0;
byte[] d = File.ReadAllBytes(inp);
string magic = System.Text.Encoding.ASCII.GetString(d, 0, 4);
int w = BitConverter.ToInt32(d, 4), h = BitConverter.ToInt32(d, 8);
bool dxt5 = magic == "JT35";
bool dxt1 = magic == "JT31";
Console.WriteLine($"{magic} {w}x{h}");
var rgb = new byte[w * h * 3];
int p = 12;
for (int by = 0; by < h; by += 4)
for (int bx = 0; bx < w; bx += 4)
{
    var a = new int[16];
    if (dxt1) { for (int i = 0; i < 16; i++) a[i] = 255; }
    else if (dxt5)
    {
        int a0 = d[p], a1 = d[p + 1];
        long bits = 0; for (int i = 0; i < 6; i++) bits |= (long)d[p + 2 + i] << (8 * i);
        for (int i = 0; i < 16; i++) {
            int idx = (int)((bits >> (3 * i)) & 7);
            int av;
            if (idx == 0) av = a0; else if (idx == 1) av = a1;
            else if (a0 > a1) av = ((8 - idx) * a0 + (idx - 1) * a1) / 7;
            else if (idx < 6) av = ((6 - idx) * a0 + (idx - 1) * a1) / 5;
            else av = (idx == 6) ? 0 : 255;
            a[i] = av;
        }
        p += 8;
    }
    else // DXT3: 4-bit alpha per pixel
    {
        for (int i = 0; i < 8; i++) { a[i*2] = (d[p+i] & 0x0F) * 17; a[i*2+1] = (d[p+i] >> 4) * 17; }
        p += 8;
    }
    ushort c0 = BitConverter.ToUInt16(d, p), c1 = BitConverter.ToUInt16(d, p + 2);
    uint cidx = BitConverter.ToUInt32(d, p + 4); p += 8;
    var col = new (int r,int g,int b)[4];
    col[0] = R5G6B5(c0); col[1] = R5G6B5(c1);
    col[2] = ((2*col[0].r+col[1].r)/3,(2*col[0].g+col[1].g)/3,(2*col[0].b+col[1].b)/3);
    col[3] = ((col[0].r+2*col[1].r)/3,(col[0].g+2*col[1].g)/3,(col[0].b+2*col[1].b)/3);
    for (int i = 0; i < 16; i++)
    {
        int px = bx + (i % 4), py = by + (i / 4);
        if (px >= w || py >= h) continue;
        var c = col[(int)((cidx >> (2*i)) & 3)];
        // composite alpha over white so transparent cells read as white background
        int al = a[i];
        int rr = (c.r*al + 255*(255-al))/255, gg=(c.g*al+255*(255-al))/255, bb=(c.b*al+255*(255-al))/255;
        int o = (py*w+px)*3; rgb[o]=(byte)rr; rgb[o+1]=(byte)gg; rgb[o+2]=(byte)bb;
    }
}
// optional crop: env CROP=x,y,w,h (full-res)
var cropEnv = Environment.GetEnvironmentVariable("CROP");
if (!string.IsNullOrEmpty(cropEnv)) {
  var pp = cropEnv.Split(','); int cx=int.Parse(pp[0]),cy=int.Parse(pp[1]),cw=int.Parse(pp[2]),ch=int.Parse(pp[3]);
  var cr=new byte[cw*ch*3];
  for(int y=0;y<ch;y++)for(int x=0;x<cw;x++){int so=((cy+y)*w+(cx+x))*3,o=(y*cw+x)*3;cr[o]=rgb[so];cr[o+1]=rgb[so+1];cr[o+2]=rgb[so+2];}
  WritePng(outp,cw,ch,cr); Console.WriteLine($"wrote crop {outp} {cw}x{ch}"); return;
}
// optional downscale (nearest) to keep PNG viewable
int ow=w, oh=h; byte[] orgb=rgb;
if (maxSize>0 && w>maxSize) { int s=(w+maxSize-1)/maxSize; ow=w/s; oh=h/s; orgb=new byte[ow*oh*3];
  for(int y=0;y<oh;y++)for(int x=0;x<ow;x++){int so=((y*s)*w+(x*s))*3,o=(y*ow+x)*3;orgb[o]=rgb[so];orgb[o+1]=rgb[so+1];orgb[o+2]=rgb[so+2];}}
WritePng(outp, ow, oh, orgb);
Console.WriteLine($"wrote {outp} {ow}x{oh}");

static (int,int,int) R5G6B5(ushort c) => (((c>>11)&31)*255/31, ((c>>5)&63)*255/63, (c&31)*255/31);

static void WritePng(string path, int w, int h, byte[] rgb)
{
    using var fs = File.Create(path);
    Span<byte> sig = stackalloc byte[]{137,80,78,71,13,10,26,10}; fs.Write(sig);
    var ihdr = new byte[13];
    WBE(ihdr,0,w); WBE(ihdr,4,h); ihdr[8]=8; ihdr[9]=2; // 8-bit RGB
    Chunk(fs,"IHDR",ihdr);
    // raw scanlines with filter 0
    var raw = new byte[h*(w*3+1)];
    for(int y=0;y<h;y++){ raw[y*(w*3+1)]=0; Array.Copy(rgb,y*w*3,raw,y*(w*3+1)+1,w*3); }
    var comp = ZlibCompress(raw);
    Chunk(fs,"IDAT",comp);
    Chunk(fs,"IEND",Array.Empty<byte>());
}
static void WBE(byte[]b,int o,int v){b[o]=(byte)(v>>24);b[o+1]=(byte)(v>>16);b[o+2]=(byte)(v>>8);b[o+3]=(byte)v;}
static void Chunk(Stream s,string type,byte[] data){
    var len=new byte[4]; WBE(len,0,data.Length); s.Write(len);
    var t=System.Text.Encoding.ASCII.GetBytes(type); s.Write(t); s.Write(data);
    uint c=Crc(t,data); var cb=new byte[4]; WBE(cb,0,(int)c); s.Write(cb);
}
static byte[] ZlibCompress(byte[] data){
    using var ms=new MemoryStream(); ms.WriteByte(0x78); ms.WriteByte(0x9C);
    using(var ds=new DeflateStream(ms,CompressionLevel.Optimal,true)) ds.Write(data,0,data.Length);
    uint a=Adler32(data); ms.WriteByte((byte)(a>>24));ms.WriteByte((byte)(a>>16));ms.WriteByte((byte)(a>>8));ms.WriteByte((byte)a);
    return ms.ToArray();
}
static uint Adler32(byte[] d){uint a=1,b=0;foreach(var x in d){a=(a+x)%65521;b=(b+a)%65521;}return (b<<16)|a;}
static uint Crc(byte[] t,byte[] d){uint c=0xffffffff;foreach(var x in t)c=Cr(c,x);foreach(var x in d)c=Cr(c,x);return c^0xffffffff;}
static uint Cr(uint c,byte x){c^=x;for(int i=0;i<8;i++)c=(c&1)!=0?(c>>1)^0xEDB88320:c>>1;return c;}
