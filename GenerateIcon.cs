using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Collections.Generic;
class GenerateIcon
{
    static byte[] Draw(int size){using(var b=new Bitmap(size,size)){using(var g=Graphics.FromImage(b)){g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(Color.Transparent);using(var bg=new SolidBrush(Color.FromArgb(20,22,24)))g.FillEllipse(bg,1,1,size-2,size-2);using(var p=new Pen(Color.FromArgb(175,220,206),Math.Max(2,size/12f))){p.StartCap=p.EndCap=LineCap.Round;g.DrawArc(p,size*.23f,size*.2f,size*.54f,size*.58f,35,285);g.DrawLine(p,size*.52f,size*.51f,size*.73f,size*.51f);}}using(var m=new MemoryStream()){b.Save(m,ImageFormat.Png);return m.ToArray();}}}
    static void Main(string[] a){int[] sizes={16,32,48,256};var images=new List<byte[]>();foreach(int s in sizes)images.Add(Draw(s));using(var f=new BinaryWriter(File.Create(a[0]))){f.Write((short)0);f.Write((short)1);f.Write((short)sizes.Length);int offset=6+16*sizes.Length;for(int i=0;i<sizes.Length;i++){f.Write((byte)(sizes[i]==256?0:sizes[i]));f.Write((byte)(sizes[i]==256?0:sizes[i]));f.Write((byte)0);f.Write((byte)0);f.Write((short)1);f.Write((short)32);f.Write(images[i].Length);f.Write(offset);offset+=images[i].Length;}foreach(var image in images)f.Write(image);}}
}
