using System.Collections.Generic;
using Newtonsoft.Json;
using NUnit.Framework;
using WorldWeaver.Editor.Assistant;

namespace WorldWeaver.Tests
{
    /// <summary>Pure presentation regression cases; no Editor window, scene, provider or request is created.</summary>
    public sealed class ChatTaskPresentationTests
    {
        static AssistantMessage Planned() => new AssistantMessage { Task = new AgentTaskState { MutationAuthorized = true } };
        static AssistantMessage Verified()
        {
            var message = Planned(); message.Status = "Проверки пройдены"; message.Task.Status = "checks_passed";
            message.Task.Requirements.Add(new AgentRequirement { Id = "npc_count", Description = "Один NPC" });
            message.Task.Checks.Add(new AgentCheckResult { RequirementId = "npc_count", Description = "Один NPC", Passed = true });
            return message;
        }

        [Test] public void TimeoutOverridesStalePlannedTaskWithoutClaimingPreparedOrExecutedResult()
        {
            var message = Planned(); message.Status = "Лимит времени"; message.IsError = true;
            var view = ChatTaskPresentation.From(message);
            Assert.AreEqual("task-card-error", view.CssClass); Assert.AreEqual("Лимит времени", view.Title);
            StringAssert.DoesNotContain("План подготовлен", view.Status); StringAssert.DoesNotContain("Отдельные критерии", view.Detail);
            StringAssert.Contains("не подтверждён", view.Detail);
        }
        [Test] public void InvalidJsonFailureOverridesDefaultPlannedState()
        {
            var message = Planned(); message.Status = "Не выполнено"; message.IsError = true;
            message.Attempts.Add(new EditorPlanAttempt { Accepted = false, Error = "Invalid JSON", Response = "{" });
            var view = ChatTaskPresentation.From(message);
            Assert.AreEqual("task-card-error", view.CssClass); Assert.AreEqual("Задача не выполнена", view.Title);
            Assert.AreNotEqual("План подготовлен", view.Status);
        }
        [Test] public void UserCancellationIsNotDisplayedAsSuccessEvenWithoutErrorFlag()
        {
            var message = Planned(); message.Status = "Остановлено";
            var view = ChatTaskPresentation.From(message);
            Assert.AreEqual("task-card-pending", view.CssClass); Assert.AreEqual("Запрос остановлен", view.Title);
        }
        [Test] public void FailureTakesPrecedenceOverOldPassedChecksAndAnUnappliedPlan()
        {
            var message = Verified(); message.PendingPlan = new EditorPlan(); message.IsError = true; message.Status = "Ошибка применения";
            Assert.AreEqual("task-card-error", ChatTaskPresentation.From(message).CssClass);
        }
        [Test] public void ProposedPlanIsPendingEvenWithOldPassedChecks()
        {
            var message = Verified(); message.PendingPlan = new EditorPlan(); message.Status = "План готов";
            var view = ChatTaskPresentation.From(message);
            Assert.AreEqual("task-card-pending", view.CssClass); Assert.AreEqual("План ожидает применения", view.Title);
            Assert.AreEqual("Этот план ещё не применён", view.Status);
        }
        [Test] public void AppliedActionsWithoutChecksRemainUnverified()
        {
            var message = Planned(); message.Status = "Изменения применены"; message.Task.Status = "actions_need_review";
            message.Steps.Add(new EditorCommandResult { Tool = "create_npc", Success = true });
            var view = ChatTaskPresentation.From(message);
            Assert.AreEqual("task-card-neutral", view.CssClass); Assert.AreEqual("Результаты действий", view.Title);
            StringAssert.Contains("не подтверждено", view.Status);
        }
        [Test] public void CompleteExplicitChecksAreTheOnlyGreenOutcome()
        {
            var message = Verified(); var view = ChatTaskPresentation.From(message);
            Assert.AreEqual("task-card-success", view.CssClass); Assert.AreEqual("Проверено 1 из 1", view.Title);
        }
        [Test] public void ARequirementWithoutItsOwnPassedCheckPreventsGreenOutcome()
        {
            var message = Verified(); message.Task.Requirements.Add(new AgentRequirement { Id = "behavior", Description = "Патруль" });
            Assert.AreNotEqual("task-card-success", ChatTaskPresentation.From(message).CssClass);
        }
        [Test] public void FailedCheckPreventsGreenEvenWithMisleadingReadyMessage()
        {
            var message = Verified(); message.Task.Checks[0].Passed = false;
            Assert.AreEqual("task-card-error", ChatTaskPresentation.From(message).CssClass);
        }
        [Test] public void PendingEditorJobPreventsGreen()
        {
            var message = Verified(); message.Task.PendingJobId = "pending-job";
            Assert.AreEqual("task-card-pending", ChatTaskPresentation.From(message).CssClass);
        }
        [Test] public void ActiveOperationPreventsPrematureGreen()
        {
            Assert.AreEqual("task-card-pending", ChatTaskPresentation.From(Verified(), true).CssClass);
        }
        [Test] public void DeferredContinuationPreventsGreen()
        {
            var message = Verified(); message.Task.ContinueAfterReload = true;
            Assert.AreEqual("task-card-pending", ChatTaskPresentation.From(message).CssClass);
        }
        [Test] public void OpenQuestionPreventsGreen()
        {
            var message = Verified(); message.Task.OpenQuestions.Add("Какой персонаж?");
            Assert.AreEqual("task-card-pending", ChatTaskPresentation.From(message).CssClass);
        }
        [Test] public void RestoredTaskDoesNotRetainVerifiedAppearance()
        {
            var message = Verified(); message.Task.Status = "restored"; message.Status = "Состояние восстановлено";
            var view = ChatTaskPresentation.From(message);
            Assert.AreEqual("task-card-neutral", view.CssClass); Assert.AreEqual("Изменения отменены", view.Title);
        }
        [Test] public void OutcomeOnlyChangeInvalidatesCardCacheWithoutChangingTask()
        {
            var message = Planned(); string taskBefore = JsonConvert.SerializeObject(message.Task);
            string before = ChatTaskPresentation.From(message).Signature(message.Task);
            message.IsError = true; message.Status = "Не выполнено";
            Assert.AreNotEqual(before, ChatTaskPresentation.From(message).Signature(message.Task));
            Assert.AreEqual(taskBefore, JsonConvert.SerializeObject(message.Task));
        }
        [Test] public void PendingPlanAndActiveOnlyChangesInvalidateCardCache()
        {
            var message = Planned(); string before = ChatTaskPresentation.From(message).Signature(message.Task);
            message.PendingPlan = new EditorPlan();
            Assert.AreNotEqual(before, ChatTaskPresentation.From(message).Signature(message.Task));
            message.PendingPlan = null;
            Assert.AreNotEqual(before, ChatTaskPresentation.From(message, true).Signature(message.Task));
        }
        [Test] public void ReloadedFailedHistoryRemainsFailedAndPresentationDoesNotMutateIt()
        {
            var message = Planned(); message.IsError = true; message.Status = "Не выполнено";
            string saved = JsonConvert.SerializeObject(message); var loaded = JsonConvert.DeserializeObject<AssistantMessage>(saved);
            Assert.AreEqual("task-card-error", ChatTaskPresentation.From(loaded).CssClass);
            Assert.AreEqual(saved, JsonConvert.SerializeObject(loaded));
        }
        [Test] public void ATaskWithoutAnyChecksDoesNotBecomeGreenFromCompletedStatusAlone()
        {
            var message = Planned(); message.Status = "Готово"; message.Task.Status = "checks_passed";
            Assert.AreEqual("task-card-neutral", ChatTaskPresentation.From(message).CssClass);
        }
        [Test] public void PureAskOrMissingTaskDoesNotShowAReadinessCard()
        {
            Assert.IsFalse(ChatTaskPresentation.From(new AssistantMessage()).Visible);
            Assert.IsFalse(ChatTaskPresentation.From(new AssistantMessage { Task = new AgentTaskState() }).Visible);
        }
    }
}
