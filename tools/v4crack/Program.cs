using System.Text;
// v4crack <clientFile> <key1|key2> <headerLen> <recordSize> <recordIdx> <nameOff> <nameLen> [serverRawFile]
// Decifra o corpo (j local), mostra o Name do registro, prova round-trip byte-identico,
// e (opcional) compara estrutura com o cru do server.

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
var w1252 = Encoding.GetEncoding(1252);

string Key1 = "BEC6C0CCC5DB20B8AEBDBAC6AE20C0CEC4DAB5F920B7E7C6BEC0D4B4CFB4D92E20B7EAB7E720B6F6B6F32E2E2E20C0B82E2E2E20C1A4B8BB20C1A4B8BB20B1CDC2FAB4D92E20B1D7B7A1B5B520C7D8BEDFC7CFB4CF20BEEEC2BF20BCF620BEF8C1D22E2E2E20";
string Key2 = "C0CCB0C520C0D0C1F620B8B6BCBCBFE42E20C0D0C0B8B8E920B3AABBDBBBE7B6F7B5CBB4CFB4D92E20C1A6B9DF20C0FDB4EB20C0D0C1F6B8BBB0ED20C2F8C7D120BBE7B6F7B5EEB7CE20BBE7BCBCBFE42E20BEC6BCCCC1D23F20C1C1C0BABCBCBBF3B8B8B5ECBDC3B4D92E";

byte[] Hex(string h){var a=new byte[h.Length/2];for(int i=0;i<a.Length;i++)a[i]=Convert.ToByte(h.Substring(i*2,2),16);return a;}

string file=args[0]; var key=Hex(args[1]=="key1"?Key1:Key2); int hdr=int.Parse(args[2]);
int recSize=int.Parse(args[3]); int recIdx=int.Parse(args[4]); int nameOff=int.Parse(args[5]); int nameLen=int.Parse(args[6]);

var full=File.ReadAllBytes(file);
Console.WriteLine($"file={Path.GetFileName(file)} size={full.Length} header[{hdr}]='{w1252.GetString(full,0,Math.Min(hdr,16)).Replace("\0",".")}' keyLen={key.Length}");

// corpo = tudo apos o header
var body=new byte[full.Length-hdr];
Array.Copy(full,hdr,body,0,body.Length);

void Transform(byte[] buf,int sign){
  int size=buf.Length; bool par=(size%2)==0; int len=key.Length; int j=0;
  while(j<size-1){
    buf[j]=(byte)(buf[j]-sign*(key[j%len]+j)); j++;
    buf[j]=(byte)(buf[j]-sign*(key[j%len]+j)); j++;
    if(!par){ buf[j]=(byte)(buf[j]-sign*(key[j%len]+j)); j++; }
  }
}

var dec=(byte[])body.Clone();
Transform(dec,+1); // decrypt

string ReadName(byte[] b,int off,int len){int e=Array.IndexOf(b,(byte)0,off,len); if(e<0)e=off+len; return w1252.GetString(b,off,e-off);}
int ro=recIdx*recSize;
Console.WriteLine($"decifrado reg {recIdx} @ body+{ro}: Name='{ReadName(dec,ro+nameOff,nameLen)}'");
// dump primeiros 8 bytes do corpo (esperado = key[j]+j se reg0 zero no server)
Console.Write("body[0..7]="); for(int i=0;i<8;i++) Console.Write(body[i].ToString("X2")+" ");
Console.Write(" | key[j]+j="); for(int i=0;i<8;i++) Console.Write(((byte)(key[i%key.Length]+i)).ToString("X2")+" "); Console.WriteLine();

// PROVA round-trip: encrypt(decrypt(body)) == body ?
var re=(byte[])dec.Clone(); Transform(re,-1);
int match=0,first=-1; for(int i=0;i<body.Length;i++){ if(re[i]==body[i])match++; else if(first<0)first=i; }
Console.WriteLine($"ROUND-TRIP encrypt(decrypt(body)) vs body: {match}/{body.Length}" + (match==body.Length?"  -> BYTE-IDENTICO":$"  (1a diff @ {first})"));

// opcional: compara corpo decifrado com cru do server (estrutura/conteudo)
if(args.Length>7){
  var srv=File.ReadAllBytes(args[7]);
  int n=Math.Min(srv.Length,dec.Length); int m=0,f=-1;
  for(int i=0;i<n;i++){ if(srv[i]==dec[i])m++; else if(f<0)f=i; }
  Console.WriteLine($"decifrado vs server cru ({srv.Length} vs {dec.Length}): {m}/{n} iguais" + (m==n&&srv.Length==dec.Length?"  -> BYTE-IDENTICO":$"  (1a diff @ {f}; drift de conteudo se poucos)"));
}
