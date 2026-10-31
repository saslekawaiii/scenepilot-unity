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
        public bool Interact(WorldPlace person, Inventory player)
        {
            if (!person || !player || Vector3.Distance(player.transform.position, person.transform.position) > InteractionDistance) return Fail("Подойдите ближе к персонажу.");
            var giverNpc=Giver?Giver.GetComponent<IntelligentNPC>():null; var recipientNpc=Recipient?Recipient.GetComponent<IntelligentNPC>():null;
            if((giverNpc&&giverNpc.Busy)||(recipientNpc&&recipientNpc.Busy))return Fail("Дождитесь окончания разговора.");
            if (Stage == DeliveryQuestStage.Completed) return Fail("Письмо уже доставлено. Награда получена.");
            if (person == Giver)
            {
                if (Stage != DeliveryQuestStage.NotStarted) return Fail("Доставьте письмо стражу у северных ворот.");
                if (!player.Add(LetterItem,1)) return Fail("Недостаточно места для письма.");
                Stage = DeliveryQuestStage.CarryingLetter; Relationship(Giver,player,2);GetComponentInParent<WorldEventJournal>()?.Record("quest_accepted",Title+": письмо получено."); if(giverNpc)giverNpc.RememberEvent("Передал игроку письмо для стража. Доставка ещё не завершена."); LastMessage = "Отнесите письмо стражу у северных ворот."; return true;
            }
            if (person != Recipient || Stage != DeliveryQuestStage.CarryingLetter) return Fail("Сначала получите письмо у жителя на площади.");
            if (player.Count(LetterItem) < 1 || !player.CanAdd(RewardItem,RewardAmount)) return Fail("Нет письма или места для награды.");
            if (!player.Remove(LetterItem,1)) return Fail("Письмо отсутствует.");
            player.Add(RewardItem,RewardAmount); Stage = DeliveryQuestStage.Completed;Relationship(Giver,player,5);Relationship(Recipient,player,10);GetComponentInParent<WorldEventJournal>()?.Record("quest_completed",Title+": письмо доставлено, награда выдана."); if(giverNpc)giverNpc.RememberEvent("Письмо доставлено стражу."); if(recipientNpc)recipientNpc.RememberEvent("Получил письмо игрока и выдал награду: "+RewardAmount+" "+RewardItem+".");
            LastMessage = "Письмо доставлено. Награда: " + RewardAmount + " " + RewardItem + "."; return true;
        }
        static void Relationship(WorldPlace person,Inventory player,int amount)
        {
            var relationship=person?person.GetComponent<NpcRelationships>():null;var actor=player.GetComponent<WorldPlace>();if(relationship&&actor)relationship.ChangeTrust(actor.PersistentId,amount);
        }
        private bool Fail(string message) { LastMessage = message; return false; }
    }
}
