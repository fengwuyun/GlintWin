using GlintWin;
var path = Path.Combine(Path.GetTempPath(), "glint-daily-" + Guid.NewGuid() + ".db");
int assertions = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); assertions++; }
var date = DateTime.Now.ToString("yyyy-MM-dd");
try {
using (var store = new DailyWordStore(path)) {
var c = new DailyCollector(store);
c.Observe("你好", "ni hao", null, 1, 10); c.Confirm(1,"ni hao",1,20);
Check(store.Count(date)==0,"default disabled"); c.Enabled=true;
c.Observe("你好", "ni hao", null, 1, 100); c.Confirm(1," NI   HAO ",1,200); c.Confirm(1,"ni hao",1,201);
Check(store.Query(date).Single().Count==1,"once per snapshot");
c.Observe("你好","ni hao",null,1,300); c.Confirm(1,"ni hao",1,301);
Check(store.Query(date).Single().Count==2,"two sessions");
foreach(var kind in new[]{"stale","cancel","disabled","foreground","spelling","second","missing","future"}) {
c.Enabled=true; c.Observe("跳过","tiao guo",null,1,100);
if(kind=="cancel" || kind=="missing") c.Cancel(); if(kind=="disabled")c.Enabled=false;
c.Confirm(kind=="second"?2:1,kind=="spelling"?"other":"tiao guo",kind=="foreground"?2:1,kind=="stale"?1301:kind=="future"?99:200);
} Check(store.Count(date)==1,"invalid confirmations skipped");
var e=store.Query(date).Single(); e.Meaning="hello ' 世界"; e.Status="已忽略"; e.NeedsReview=false; store.Update(e);
store.Record("你好","ni hao","replace",true); store.Flush(); e=store.Query(date,"已忽略").Single();
Check(e.Count==3 && e.Meaning=="hello ' 世界" && !e.NeedsReview,"manual state survives");
Check(store.Count(date,"待核对")==0,"review filter");
c.Enabled=true;
c.ObserveCandidates(new[] { ("候选一", (string?)"first"), ("候选二", (string?)"second") },"hou xuan",1,100);
c.Confirm(2,"hou xuan",1,1300); c.Confirm(2,"hou xuan",1,1300);
Check(store.Query(date,"","候选二").Single().Count==1 && store.Count(date,"","候选一")==0,"mapped second and inclusive age boundary");
c.ObserveCandidates(new[] { ("过期一", (string?)null), ("过期二", (string?)null) },"guo qi",1,100);
c.Confirm(2,"guo qi",1,1301); Check(store.Count(date,"","过期")==0,"mapped second stale skipped");
Check(store.Count(date,"待核对")==1,"mapped second is inferred");
store.Record("引'号","yin hao","quoted",true); Check(store.Count(date,"","引'号")==1,"bound quote");
store.Record("旧日","jiu ri",null,true,DateTimeOffset.Now.AddDays(-1)); Check(store.Count(DateTime.Now.AddDays(-1).ToString("yyyy-MM-dd"))==1,"date partition");
for(int i=0;i<5000;i++)store.Record("批量","pi liang",null,true);
store.Flush(); Check(store.Query(date,"","批量").Single().Count>0,"large bounded batch");
Check(store.Query(date,pageSize:1,sortByFrequency:true).Single().Word=="批量", "global frequency ordering");
var exportPath = path + ".csv";
store.ExportCsv(exportPath,date,"","引'号");
Check(File.ReadAllText(exportPath).Contains("引'号") && !File.ReadAllText(exportPath).Contains("你好"), "streamed filtered CSV export");
Check(store.Count(date,"已忽略|待翻译")==store.Count(date,"已忽略")+store.Count(date,"待翻译"),"multi-status union count");
Check(store.Count(date,"待核对|已忽略")==store.Count(date,"待核对")+store.Count(date,"已忽略"),"review and status union");
store.ExportCsv(exportPath,date,"已忽略|待翻译");
Check(File.ReadAllText(exportPath).Contains("你好")&&!File.ReadAllText(exportPath).Contains("候选二"),"multi-status export shares query filter");
store.Record("尾巴@","wei ba",null,true);store.Flush();Check(store.Query(date,"","尾巴").Single().Word=="尾巴","record strips OCR suffix");
var legacy=store.Query(date,"","尾巴").Single();legacy.Word="尾巴@";store.Update(legacy);
store.ExportCsv(exportPath,date,"","尾巴");
Check(File.ReadAllText(exportPath).Contains("\"尾巴\"")&&!File.ReadAllText(exportPath).Contains("尾巴@"),"legacy export strips suffix without rewriting stored history");
File.Delete(exportPath);
store.Record("删除","shan chu",null,true); store.DeleteDate(date); Check(store.Count(date)==0,"delete drains queue");
store.Record("重开","chong kai",null,true); store.Flush();
}
using(var store = new DailyWordStore(path)) Check(store.Query(date).Single().Word=="重开","durable reopen");
Console.WriteLine($"PASS {assertions} assertions");
} finally { foreach(var suffix in new[]{"","-wal","-shm"}) if(File.Exists(path+suffix))File.Delete(path+suffix); }
