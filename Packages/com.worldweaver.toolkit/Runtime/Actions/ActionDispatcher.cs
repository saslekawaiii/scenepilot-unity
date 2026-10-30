using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

namespace WorldWeaver
{
    public sealed class ActionContext
    {
        public IntelligentNPC Npc;
        public Dictionary<string, WorldEntity> Entities;
        public HashSet<string> Allowed;
        public readonly Dictionary<Inventory, Dictionary<string, int>> Projected = new Dictionary<Inventory, Dictionary<string, int>>();
        public bool Reserve(Inventory source, Inventory destination, string item, int amount)
        {
            if (!source || !destination || source == destination || amount < 1 || amount > Npc.MaxTransferPerDecision || string.IsNullOrWhiteSpace(item)) return false;
            if (!Projected.TryGetValue(source, out var from)) Projected[source] = from = source.Snapshot();
            if (!Projected.TryGetValue(destination, out var to)) Projected[destination] = to = destination.Snapshot();
            int available = from.TryGetValue(item, out var n) ? n : 0;
            int current = to.TryGetValue(item, out var d) ? d : 0;
            if (available < amount || to.Values.Sum() + amount > destination.Capacity) return false;
            from[item] = available - amount; to[item] = current + amount; return true;
        }
    }
    public interface IGameAction
    {
        string Identifier { get; }
        string Validate(ActionContext context, ActionCommand command);
        void Execute(ActionContext context, ActionCommand command);
    }
    public sealed class ActionReport
    {
        public bool Accepted;
        public string Reason;
        public readonly List<string> Executed = new List<string>();
    }

    /// <summary>Validates the whole decision against projected inventory before any mutation.</summary>
    public sealed class ActionDispatcher
    {
        private readonly Dictionary<string, IGameAction> registry = new Dictionary<string, IGameAction>(StringComparer.Ordinal);
        public ActionDispatcher()
        {
            Register(new TransferAction(false)); Register(new TransferAction(true));
            foreach (string type in new[] { "MoveTo", "Follow", "Flee", "Wait", "Talk", "Interact" }) Register(new WorldAction(type));
        }
        public void Register(IGameAction action)
        { if (action == null || string.IsNullOrWhiteSpace(action.Identifier)) throw new ArgumentException("Action ID required."); registry[action.Identifier] = action; }
        public ActionReport Execute(IntelligentNPC npc, NpcDecision decision)
        {
            if (decision?.actions == null || decision.actions.Length > 4) return new ActionReport { Reason = "Invalid action batch." };
            var entities = new Dictionary<string, WorldEntity>(StringComparer.Ordinal);
            foreach (var entity in npc.KnownEntities)
            {
                if (!entity || string.IsNullOrWhiteSpace(entity.Id)) continue;
                if (entities.ContainsKey(entity.Id)) return new ActionReport { Reason = "Duplicate entity ID." };
                entities.Add(entity.Id, entity);
            }
            var context = new ActionContext { Npc = npc, Entities = entities, Allowed = new HashSet<string>(npc.AllowedActions, StringComparer.Ordinal) };
            long transferred = 0; int locomotion = 0;
            foreach (var command in decision.actions)
            {
                if (command == null || !context.Allowed.Contains(command.type ?? "") || !registry.TryGetValue(command.type, out var action))
                    return new ActionReport { Reason = "Action is not allowed or registered." };
                if (command.type == "GiveItem" || command.type == "TakeItem")
                { transferred += command.amount; if (transferred > npc.MaxTransferPerDecision) return new ActionReport { Reason = "Transfer budget exceeded." }; }
                if (new[] { "MoveTo", "Follow", "Flee", "Wait" }.Contains(command.type) && ++locomotion > 1) return new ActionReport { Reason = "Conflicting movement commands." };
                string error = action.Validate(context, command);
                if (error != null) return new ActionReport { Reason = error };
            }
            var report = new ActionReport { Accepted = true, Reason = "All preconditions satisfied." };
            foreach (var command in decision.actions)
            { registry[command.type].Execute(context, command); report.Executed.Add(command.type + "(" + command.target + "," + command.item + "," + command.amount + ")"); }
            return report;
        }
    }

    public sealed class TransferAction : IGameAction
    {
        private readonly bool take;
        public string Identifier => take ? "TakeItem" : "GiveItem";
        public TransferAction(bool take) { this.take = take; }
        public string Validate(ActionContext context, ActionCommand command)
        {
            if (!context.Entities.TryGetValue(command.target ?? "", out var target)) return "Unknown transfer target.";
            if (take && !context.Npc.AllowTakingItems) return "Taking items requires explicit permission.";
            if (Vector3.Distance(context.Npc.transform.position, target.transform.position) > context.Npc.InteractionDistance) return "Transfer target is too far away.";
            var own = context.Npc.GetComponent<Inventory>(); var other = target.GetComponent<Inventory>();
            return context.Reserve(take ? other : own, take ? own : other, command.item, command.amount) ? null : "Inventory, amount or transfer limit failed.";
        }
        public void Execute(ActionContext context, ActionCommand command)
        {
            var own = context.Npc.GetComponent<Inventory>(); var other = context.Entities[command.target].GetComponent<Inventory>();
            if (!Inventory.Transfer(take ? other : own, take ? own : other, command.item, command.amount)) throw new InvalidOperationException("Inventory changed during action dispatch.");
        }
    }
    public sealed class WorldAction : IGameAction
    {
        public string Identifier { get; }
        public WorldAction(string identifier) { Identifier = identifier; }
        public string Validate(ActionContext context, ActionCommand command)
        {
            if (!string.IsNullOrEmpty(command.item) || command.amount != 0) return "Non-inventory actions require empty item and zero amount.";
            if (Identifier == "Wait" || Identifier == "Talk") return string.IsNullOrEmpty(command.target) ? null : "Wait/Talk have no target.";
            if (!context.Entities.TryGetValue(command.target ?? "", out var target) || !target) return "Unknown action target.";
            if (Identifier == "Interact")
                return target.Interactable && Vector3.Distance(context.Npc.transform.position, target.transform.position) <= context.Npc.InteractionDistance ? null : "Interaction unavailable or too distant.";
            var agent = context.Npc.GetComponent<NavMeshAgent>();
            if (!agent || !agent.enabled || !agent.isOnNavMesh) return "NPC is not on a NavMesh.";
            Vector3 destination = Destination(context.Npc, target, Identifier);
            var path = new NavMeshPath();
            return NavMesh.SamplePosition(destination, out var hit, 3, agent.areaMask) && agent.CalculatePath(hit.position, path) && path.status == NavMeshPathStatus.PathComplete ? null : "Destination is unreachable.";
        }
        public static Vector3 Destination(IntelligentNPC npc, WorldEntity target, string action)
        {
            if (action != "Flee") return target.transform.position;
            var direction = npc.transform.position - target.transform.position; direction.y = 0;
            return npc.transform.position + (direction.sqrMagnitude < .01f ? Vector3.forward : direction.normalized) * 8;
        }
        public void Execute(ActionContext context, ActionCommand command)
        {
            if (Identifier == "Talk") return;
            if (Identifier == "Wait") { context.Npc.StopMovement(); return; }
            var target = context.Entities[command.target];
            if (Identifier == "Interact") { target.Interact(); return; }
            context.Npc.BeginMovement(target, Identifier);
        }
    }
}
