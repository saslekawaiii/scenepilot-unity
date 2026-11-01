using System;
using UnityEngine;
namespace WorldWeaver
{
    public enum DeliveryQuestStage { NotStarted, CarryingLetter, Completed }
    /// <summary>Deterministic inventory-backed quest; interaction cannot duplicate a reward.</summary>
    public sealed class DeliveryQuest : MonoBehaviour
    {
        public string QuestId = Guid.NewGuid().ToString("N");
        public string Title = "Письмо стражу";
        public WorldPlace Giver, Recipient;
        public string LetterItem = "Письмо", RewardItem = "Монета";
        public int RewardAmount = 5;
        public float InteractionDistance = 3;
        public DeliveryQuestStage Stage;
        public string LastMessage { get; private set; }
        public string GiverName=>ParticipantName(Giver,"отправитель");
        public string RecipientName=>ParticipantName(Recipient,"получатель");
        public string ObjectiveText=>Stage==DeliveryQuestStage.Completed?"Доставка завершена. Получатель: "+RecipientName+".":Stage==DeliveryQuestStage.CarryingLetter?"Передайте «"+LetterItem+"». Получатель: "+RecipientName+".":"Получите «"+LetterItem+"». Отправитель: "+GiverName+".";
        static string ParticipantName(WorldPlace person,string fallback)
        {
            if(!person)return fallback;
            var npc=person.GetComponent<IntelligentNPC>();
            return npc&&!string.IsNullOrWhiteSpace(npc.NpcName)?npc.NpcName:!string.IsNullOrWhiteSpace(person.gameObject.name)?person.gameObject.name:fallback;
        }
        public bool Interact(WorldPlace person, Inventory player)
        {
            if (!person || !player || Vector3.Distance(player.transform.position, person.transform.position) > InteractionDistance) return Fail("Подойдите ближе к персонажу.");
            var giverNpc=Giver?Giver.GetComponent<IntelligentNPC>():null; var recipientNpc=Recipient?Recipient.GetComponent<IntelligentNPC>():null;
            if((giverNpc&&giverNpc.Busy)||(recipientNpc&&recipientNpc.Busy))return Fail("Дождитесь окончания разговора.");
            if (Stage == DeliveryQuestStage.Completed) return Fail("Доставка «"+LetterItem+"» уже завершена. Награда получена.");
            if (person == Giver)
            {
                if (Stage != DeliveryQuestStage.NotStarted) return Fail(ObjectiveText);
                if (!player.Add(LetterItem,1)) return Fail("Недостаточно места для предмета «"+LetterItem+"».");
                Stage = DeliveryQuestStage.CarryingLetter; Relationship(Giver,player,2);GetComponentInParent<WorldEventJournal>()?.Record("quest_accepted",Title+": получен предмет «"+LetterItem+"». Получатель: "+RecipientName+"."); if(giverNpc)giverNpc.RememberEvent("Передал игроку предмет «"+LetterItem+"». Получатель: "+RecipientName+". Доставка ещё не завершена."); LastMessage = ObjectiveText; return true;
            }
            if (person != Recipient || Stage != DeliveryQuestStage.CarryingLetter) return Fail("Сначала получите предмет «"+LetterItem+"». Отправитель: "+GiverName+".");
            if (player.Count(LetterItem) < 1 || !player.CanAdd(RewardItem,RewardAmount)) return Fail("Нет предмета «"+LetterItem+"» или места для награды.");
            if (!player.Remove(LetterItem,1)) return Fail("Предмет «"+LetterItem+"» отсутствует.");
            player.Add(RewardItem,RewardAmount); Stage = DeliveryQuestStage.Completed;Relationship(Giver,player,5);Relationship(Recipient,player,10);GetComponentInParent<WorldEventJournal>()?.Record("quest_completed",Title+": предмет «"+LetterItem+"» доставлен. Получатель: "+RecipientName+"; награда выдана."); if(giverNpc)giverNpc.RememberEvent("Предмет «"+LetterItem+"» доставлен. Получатель: "+RecipientName+"."); if(recipientNpc)recipientNpc.RememberEvent("Получил от игрока предмет «"+LetterItem+"». Отправитель: "+GiverName+". Выдал награду: "+RewardAmount+" "+RewardItem+".");
            LastMessage = ObjectiveText+" Награда: " + RewardAmount + " " + RewardItem + "."; return true;
        }
        static void Relationship(WorldPlace person,Inventory player,int amount)
        {
            var relationship=person?person.GetComponent<NpcRelationships>():null;var actor=player.GetComponent<WorldPlace>();if(relationship&&actor)relationship.ChangeTrust(actor.PersistentId,amount);
        }
        private bool Fail(string message) { LastMessage = message; return false; }
    }
}
