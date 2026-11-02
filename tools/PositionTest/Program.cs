using GlintWin;
using System.Drawing;
static class Test {
static void Check(bool ok,string n){if(!ok)throw new Exception(n);Console.WriteLine("PASS "+n);}
static void Main(){var p=new PositionStabilizer();var win=new NativeMethods.RECT{Left=100,Top=100,Right=700,Bottom=300};
var row=new Rectangle(80,60,400,25);
Check(!p.TryGet(win,0,out _),"no fallback to decorative window");
p.Observe(new Rectangle(5,120,500,25),600,200,0);
Check(!p.TryGet(win,0,out _),"first noisy frame stays hidden");
p.Observe(row,600,200,80);Check(!p.TryGet(win,80,out _),"different startup row restarts confirmation");
p.Observe(row,600,200,81);Check(!p.TryGet(win,81,out _),"same instant cannot confirm startup");
p.Observe(row,600,200,160);p.TryGet(win,160,out var a);Check(a.Left==180&&a.Bottom==185,"two spaced agreeing frames confirm initial row");
p.Observe(new Rectangle(82,61,398,26),600,200,240);p.TryGet(win,240,out var b);Check(a.Left==b.Left&&a.Bottom==b.Bottom,"OCR jitter suppressed");
win.Left+=120;win.Right+=120;win.Top+=80;win.Bottom+=80;p.TryGet(win,240,out b);Check(b.Left==300&&b.Bottom==265,"real window movement follows immediately");
p.Observe(new Rectangle(5,120,500,25),600,200,320);p.TryGet(win,320,out b);Check(b.Left==300,"single outlier rejected");
p.Observe(new Rectangle(5,120,500,25),600,200,400);p.TryGet(win,400,out b);Check(b.Left==225&&b.Bottom==325,"two agreeing layout frames accepted");
Check(p.TryGet(win,600,out _),"brief OCR gap holds");Check(!p.TryGet(win,1300,out _),"stale row expires");
p.Observe(row,600,200,1400);Check(!p.TryGet(win,1400,out _),"expired row requires startup confirmation again");
p.Observe(row,600,200,1480);Check(p.TryGet(win,1480,out _),"expired row reacquired after confirmation");
p.Observe(new Rectangle(5,120,500,25),600,200,2100);
p.Observe(new Rectangle(15,130,500,25),600,200,2200);
Check(!p.TryGet(win,2300,out _),"outliers do not keep old anchor alive");
p.Reset();p.Observe(row,600,200,3000);p.Observe(row,600,200,3500);Check(!p.TryGet(win,3500,out _),"widely separated startup frames cannot confirm");
p.Reset();Check(!p.TryGet(win,3500,out _),"new session resets");var session=new WatchSession();session.Observe(true,false);var generation=session.Generation;
session.Observe(true,false);Check(session.Generation==generation,"repeated visible frames preserve session");
session.Observe(false,false);session.Observe(true,false);Check(session.Generation!=generation,"hide then show retains boundary even when queue drops hide");
generation=session.Generation;session.Observe(true,true);Check(session.Generation!=generation,"input method switch starts new session");
}}
