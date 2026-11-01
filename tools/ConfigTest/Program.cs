using GlintWin;
using System.Text.Json;
var cfg = new Config();
var add = typeof(Config).GetMethod("RememberColor");
if (add == null) throw new Exception("FAIL: color history is missing");
void Remember(string hex) => add.Invoke(cfg, new object[]{hex});
List<string> Colors() => (List<string>)typeof(Config).GetProperty("RecentColors")!.GetValue(cfg)!;
void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); Console.WriteLine("PASS " + name); }
Remember("#aabbcc"); Remember("#FFAABBCC");
Check(Colors().Count == 1 && Colors()[0] == "#FFAABBCC", "normalize and deduplicate");
Remember("invalid"); Check(Colors().Count == 1, "ignore invalid color");
for (int i=0;i<20;i++) Remember($"#FF{i:X6}");
Check(Colors().Count == 8 && Colors()[0] == "#FF000013", "eight most recent colors");
Remember("#FF000010"); Check(Colors()[0] == "#FF000010" && Colors().Count == 8, "reuse moves to front");
var restored = JsonSerializer.Deserialize<Config>(JsonSerializer.Serialize(cfg))!;
Check(JsonSerializer.Serialize(restored) == JsonSerializer.Serialize(cfg), "history persistence roundtrip");
Check(JsonSerializer.Deserialize<Config>("{}") != null, "old config remains compatible");
