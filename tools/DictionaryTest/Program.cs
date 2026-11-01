using GlintWin;
var path=Path.Combine(Path.GetTempPath(),"glint-dict-"+Guid.NewGuid()+".json");
void Check(bool ok,string name){if(!ok)throw new Exception("FAIL "+name);Console.WriteLine("PASS "+name);}
try{
File.WriteAllText(path,"""{"学习":{"p":"xue2 xi2","s":["to learn (a skill); study","ignore third"]},"中国":{"s":["China"],"p":"Zhong1 guo2"},"去重":{"p":null,"s":[" ; (empty); Word","word","Other","third"]},"空义":{"p":"kong","s":null},"缺项":{}}""");
var dict=CedictDictionary.Load(path)!;
Check(dict.Count==5,"all entries loaded");Check(dict.Lookup("学习")=="learn · study","format semicolons and parentheses");
Check(dict.Lookup("去重")=="Word · Other","deduplicate case-insensitive and ignore empty senses");
Check(dict.PinyinOf("去重")==""&&dict.PinyinOf("缺项")=="","null and absent pinyin normalize");
Check(dict.Lookup("空义")==null&&dict.Lookup("缺项")==null,"null and absent senses");
Check(dict.Lookup("中国")=="China"&&dict.PinyinOf("中国")=="Zhong1 guo2","property order independent");
Check(dict.Lookup("学习中国")=="learn · study / China","segmented lookup unchanged");
Check(dict.FuzzyCorrect("学刁","xuexi")=="学习","fuzzy correction unchanged");
File.WriteAllText(path,"""{"中国":{"p":"zhong guo","s":["country"]},"新词":{"p":"xin ci","s":["new word"]}}""");dict.MergeFrom(path);
Check(dict.Lookup("中国")=="country"&&dict.Count==6,"pack overrides and additions");
for(int i=0;i<100;i++)dict.Lookup("学习");
long before=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<10000;i++)dict.Lookup("学习");long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
Check(allocated<=4096,$"repeated exact lookups avoid formatting allocations ({allocated} bytes)");
}finally{File.Delete(path);}
