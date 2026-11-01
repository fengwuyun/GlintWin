using GlintWin;
using System.Drawing;
static class Test {
static void Check(bool ok,string n){if(!ok)throw new Exception(n);Console.WriteLine("PASS "+n);}
static void Main(){var p=new PositionStabilizer();var win=new NativeMethods.RECT{Left=100,Top=100,Right=700,Bottom=300};
Check(!p.TryGet(win,0,out _),"no fallback to decorative window");
p.Observe(new Rectangle(80,60,400,25),600,200,0);p.TryGet(win,0,out var a);Check(a.Left==180&&a.Bottom==185,"initial row");
p.Observe(new Rectangle(82,61,398,26),600,200,50);p.TryGet(win,50,out var b);Check(a.Left==b.Left&&a.Bottom==b.Bottom,"OCR jitter suppressed");
win.Left+=120;win.Right+=120;win.Top+=80;win.Bottom+=80;p.TryGet(win,100,out b);Check(b.Left==300&&b.Bottom==265,"real window movement follows immediately");
p.Observe(new Rectangle(5,120,500,25),600,200,150);p.TryGet(win,150,out b);Check(b.Left==300,"single outlier rejected");
p.Observe(new Rectangle(5,120,500,25),600,200,200);p.TryGet(win,200,out b);Check(b.Left==225&&b.Bottom==325,"two agreeing layout frames accepted");
Check(p.TryGet(win,600,out _),"brief OCR gap holds");Check(!p.TryGet(win,1100,out _),"stale row expires");p.Reset();Check(!p.TryGet(win,200,out _),"new session resets");
}}
