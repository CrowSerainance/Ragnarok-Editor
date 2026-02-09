using System;

namespace RoDbEditor.Models;

public sealed class MobSkillDbRow
{
    public int MobId { get; set; }
    public string Dummy { get; set; } = "";
    public string State { get; set; } = "";
    public int SkillId { get; set; }
    public int SkillLv { get; set; }
    public int Rate { get; set; }              // 10000 = 100%
    public int CastTimeMs { get; set; }
    public int DelayMs { get; set; }
    public int Cancelable { get; set; }        // 0/1
    public string Target { get; set; } = "";
    public string ConditionType { get; set; } = "";
    public int ConditionValue { get; set; }
    public int Val1 { get; set; }
    public int Val2 { get; set; }
    public int Val3 { get; set; }
    public int Val4 { get; set; }
    public int Val5 { get; set; }
    public int Emotion { get; set; }
    public string Chat { get; set; } = "";

    // provenance (helps debugging + future "double click to open file")
    public string SourceFile { get; set; } = "";
    public int SourceLine { get; set; }
}
