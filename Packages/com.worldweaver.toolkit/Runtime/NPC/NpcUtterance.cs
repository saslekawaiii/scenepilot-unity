using System;
namespace WorldWeaver
{
    /// <summary>A bounded factual transcript entry. IsAi identifies a real provider response.</summary>
    [Serializable] public sealed class NpcUtterance
    {
        public string Id, SpeakerId, SpeakerName, TargetId, Text, Timestamp;
        public bool IsPlayer, IsAi;
        public NpcUtterance Copy() => new NpcUtterance { Id=Id, SpeakerId=SpeakerId, SpeakerName=SpeakerName, TargetId=TargetId, Text=Text, Timestamp=Timestamp, IsPlayer=IsPlayer, IsAi=IsAi };
    }
    public sealed class NpcDialogueContext
    {
        public string SpeakerId, SpeakerName;
        public bool IsPlayer = true;
    }
}
